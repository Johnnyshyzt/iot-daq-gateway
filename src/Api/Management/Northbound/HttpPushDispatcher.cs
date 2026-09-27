using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Gateway.Abstractions.Contract;
using Gateway.Abstractions.Reliability;
using IotDaq.Persistence;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using IotDaq.Licensing;
using Studio.Host.Config;
using Studio.Host.Licensing;

namespace Studio.Host.Northbound;

public sealed class HttpPushDispatcher
{
    private readonly GatewayPersistence _database;
    private readonly ConfigStore _store;
    private readonly ILogger<HttpPushDispatcher> _logger;
    private readonly LicenseService? _license;
    private readonly HttpClient _http;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly Dictionary<string, DiskForwardSpool> _spools = new(StringComparer.Ordinal);
    private readonly Dictionary<string, DateTimeOffset> _nextAttempt = new(StringComparer.Ordinal);
    private readonly Dictionary<string, int> _attempts = new(StringComparer.Ordinal);
    private readonly Dictionary<string, long> _lastPeriodic = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _lastValue = new(StringComparer.Ordinal);

    public HttpPushDispatcher(GatewayPersistence database, ConfigStore store, ILogger<HttpPushDispatcher> logger, LicenseService? license = null)
    {
        _database = database;
        _store = store;
        _license = license;
        _logger = logger;
        _http = new HttpClient { Timeout = Timeout.InfiniteTimeSpan };
    }

    public async Task TickAsync(CancellationToken cancellationToken)
    {
        if (_license is not null && !_license.Allows(LicenseFeatures.HttpPush))
        {
            return;
        }

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            foreach (var target in _database.ListHttpPushTargets().Where(row => row.Enabled))
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    await TickOneAsync(target, cancellationToken).ConfigureAwait(false);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    _logger.LogWarning(ex, "HTTP push tick failed for {Target}", target.Id);
                }
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<string?> SendTestAsync(string id, CancellationToken cancellationToken)
    {
        var target = _database.FindHttpPushTarget(id);
        if (target is null)
        {
            return "目标不存在";
        }

        var (gatewayId, site) = Identity();
        var sample = NorthboundPayload.Point(gatewayId, site, "test", "state", "RUNNING", "good", null, DateTimeOffset.UtcNow, versioned: true);
        var body = NorthboundPayload.Batch(gatewayId, site, DateTimeOffset.UtcNow, [NorthboundPayload.Parse(sample)]);
        var error = await SendAsync(target, body, cancellationToken).ConfigureAwait(false);
        _database.NoteHttpPushAttempt(id, error is null, error ?? "", Spool(id).Dropped);
        return error;
    }

    public int SpoolDepth(string id) => Spool(id).Depth;

    public void Forget(string id)
    {
        _spools.Remove(id);
        var dir = SpoolDirectory(id);
        if (Directory.Exists(dir))
        {
            try
            {
                Directory.Delete(dir, recursive: true);
            }
            catch (IOException)
            {
                // The next process start will see leftover files for a deleted target and ignore them.
            }
        }
    }

    public static int BackoffMs(int attempt, int initial, int max)
    {
        var start = Math.Clamp(initial, 50, 600_000);
        var cap = Math.Clamp(max, start, 3_600_000);
        var shift = Math.Clamp(attempt - 1, 0, 16);
        var scaled = (long)start << shift;
        if (scaled <= 0 || scaled > cap)
        {
            return cap;
        }

        return (int)scaled;
    }

    private async Task TickOneAsync(HttpPushTargetRow target, CancellationToken cancellationToken)
    {
        EnsureCursors(target.Id);
        Collect(target);
        var now = DateTimeOffset.UtcNow;
        if (_nextAttempt.TryGetValue(target.Id, out var next) && now < next)
        {
            return;
        }

        var spool = Spool(target.Id);
        var item = spool.PeekOldest(now);
        if (item is null)
        {
            return;
        }

        var error = await SendAsync(target, item.Payload, cancellationToken).ConfigureAwait(false);
        if (error is null)
        {
            spool.Acknowledge(item.Seq);
            _attempts[target.Id] = 0;
            _nextAttempt.Remove(target.Id);
            _database.NoteHttpPushAttempt(target.Id, true, "", spool.Dropped);
            return;
        }

        var attempt = _attempts.GetValueOrDefault(target.Id) + 1;
        _attempts[target.Id] = attempt;
        var delay = BackoffMs(attempt, target.BackoffInitialMs, target.BackoffMaxMs);
        _nextAttempt[target.Id] = now.AddMilliseconds(delay);
        _database.NoteHttpPushAttempt(target.Id, false, error, spool.Dropped);
        _logger.LogWarning("HTTP push {Name} failed ({Error}); retry in {Delay}ms, spool {Depth}", target.Name, error, delay, spool.Depth);
    }

    private void Collect(HttpPushTargetRow target)
    {
        var (gatewayId, site) = Identity();
        var events = new List<JsonElement>();
        var max = Math.Clamp(target.BatchMax, 1, 500);
        if (target.SendAlarms)
        {
            CollectAlarms(target, gatewayId, site, events, max);
        }

        if (target.SendStatus && events.Count < max)
        {
            CollectStatus(target, gatewayId, site, events, max);
        }

        if (target.SendValues && events.Count < max)
        {
            if (string.Equals(target.ValueMode, "periodic", StringComparison.OrdinalIgnoreCase))
            {
                CollectPeriodic(target, gatewayId, site, events, max);
            }
            else
            {
                CollectChanges(target, gatewayId, site, events, max);
            }
        }

        if (events.Count == 0)
        {
            return;
        }

        var body = NorthboundPayload.Batch(gatewayId, site, DateTimeOffset.UtcNow, events);
        Spool(target.Id).Enqueue(body, DateTimeOffset.UtcNow);
    }

    private void CollectAlarms(HttpPushTargetRow target, string gatewayId, string site, List<JsonElement> events, int max)
    {
        var raiseKey = CursorKey(target.Id, "raise");
        var clearKey = CursorKey(target.Id, "clear");
        var raiseCursor = NorthboundTime.ReadCursor(_database.GetSetting(raiseKey));
        var clearCursor = NorthboundTime.ReadCursor(_database.GetSetting(clearKey));
        foreach (var alarm in _database.AlarmsRaisedAfter(raiseCursor, max))
        {
            if (events.Count >= max)
            {
                break;
            }

            events.Add(NorthboundPayload.Parse(NorthboundPayload.Alarm(
                gatewayId, site, alarm.DeviceId, alarm.Id, "raise", alarm.Code, alarm.Message, alarm.Severity, alarm.PointId,
                DateTimeOffset.FromUnixTimeMilliseconds(alarm.RaisedUnixMs))));
            raiseCursor = alarm.RaisedUnixMs;
        }

        foreach (var alarm in _database.AlarmsClearedAfter(clearCursor, max))
        {
            if (events.Count >= max)
            {
                break;
            }

            var when = alarm.ClearedUnixMs ?? alarm.RaisedUnixMs;
            events.Add(NorthboundPayload.Parse(NorthboundPayload.Alarm(
                gatewayId, site, alarm.DeviceId, alarm.Id, "clear", alarm.Code, alarm.Message, alarm.Severity, alarm.PointId,
                DateTimeOffset.FromUnixTimeMilliseconds(when))));
            clearCursor = when;
        }

        var raiseText = NorthboundTime.Cursor(raiseCursor)!;
        var clearText = NorthboundTime.Cursor(clearCursor)!;
        if (_database.GetSetting(raiseKey) != raiseText)
        {
            _database.SetSetting(raiseKey, raiseText);
        }

        if (_database.GetSetting(clearKey) != clearText)
        {
            _database.SetSetting(clearKey, clearText);
        }
    }

    private void CollectStatus(HttpPushTargetRow target, string gatewayId, string site, List<JsonElement> events, int max)
    {
        var key = CursorKey(target.Id, "status");
        var cursor = NorthboundTime.ReadCursor(_database.GetSetting(key));
        foreach (var row in _database.ListLinkStatus().Where(row => row.UnixMs > cursor).OrderBy(row => row.UnixMs))
        {
            if (events.Count >= max)
            {
                break;
            }

            events.Add(NorthboundPayload.Parse(NorthboundPayload.Status(
                gatewayId, site, row.DeviceId, row.Status, row.Message, DateTimeOffset.FromUnixTimeMilliseconds(row.UnixMs), versioned: true)));
            cursor = row.UnixMs;
        }

        var text = NorthboundTime.Cursor(cursor)!;
        if (_database.GetSetting(key) != text)
        {
            _database.SetSetting(key, text);
        }
    }

    private void CollectChanges(HttpPushTargetRow target, string gatewayId, string site, List<JsonElement> events, int max)
    {
        var key = CursorKey(target.Id, "sample");
        var cursor = NorthboundTime.ReadCursor(_database.GetSetting(key));
        foreach (var sample in _database.LatestSince(cursor, max))
        {
            if (events.Count >= max)
            {
                break;
            }

            var token = sample.DeviceId + "\n" + sample.PointId;
            var text = sample.NumericValue?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? sample.Value ?? "";
            if (_lastValue.TryGetValue(target.Id + "\n" + token, out var previous) && previous == text)
            {
                cursor = Math.Max(cursor, sample.TimestampUnixMs);
                continue;
            }

            _lastValue[target.Id + "\n" + token] = text;
            events.Add(PointEvent(gatewayId, site, sample));
            cursor = Math.Max(cursor, sample.TimestampUnixMs);
        }

        var stored = NorthboundTime.Cursor(cursor)!;
        if (_database.GetSetting(key) != stored)
        {
            _database.SetSetting(key, stored);
        }
    }

    private void CollectPeriodic(HttpPushTargetRow target, string gatewayId, string site, List<JsonElement> events, int max)
    {
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var period = Math.Clamp(target.PeriodicSeconds, 1, 86_400) * 1000L;
        if (_lastPeriodic.TryGetValue(target.Id, out var last) && now - last < period)
        {
            return;
        }

        _lastPeriodic[target.Id] = now;
        foreach (var sample in _database.LatestAll(max))
        {
            if (events.Count >= max)
            {
                break;
            }

            events.Add(PointEvent(gatewayId, site, sample));
        }
    }

    private static JsonElement PointEvent(string gatewayId, string site, SampleView sample)
    {
        object? value = sample.NumericValue is double number ? number : sample.Value;
        var json = NorthboundPayload.Point(
            gatewayId,
            site,
            sample.DeviceId,
            sample.PointId,
            value,
            string.IsNullOrWhiteSpace(sample.Quality) ? "good" : sample.Quality,
            sample.Unit,
            DateTimeOffset.FromUnixTimeMilliseconds(sample.TimestampUnixMs),
            versioned: true,
            computed: sample.Computed);
        return NorthboundPayload.Parse(json);
    }

    private void EnsureCursors(string id)
    {
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString(System.Globalization.CultureInfo.InvariantCulture);
        foreach (var name in new[] { "raise", "clear", "status", "sample" })
        {
            var key = CursorKey(id, name);
            if (_database.GetSetting(key) is null)
            {
                _database.SetSetting(key, now);
            }
        }
    }

    private async Task<string?> SendAsync(HttpPushTargetRow target, string body, CancellationToken cancellationToken)
    {
        if (!Uri.TryCreate(target.Url, UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            return "URL 需要是 http 或 https";
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(Math.Clamp(target.TimeoutMs, 200, 120_000));
        using var request = new HttpRequestMessage(new HttpMethod(string.Equals(target.Method, "PUT", StringComparison.OrdinalIgnoreCase) ? "PUT" : "POST"), uri);
        var bytes = Encoding.UTF8.GetBytes(body);
        request.Content = new ByteArrayContent(bytes);
        request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
        ApplyHeaders(target, request);
        ApplyAuth(target, request, bytes);
        try
        {
            using var response = await _http.SendAsync(request, timeout.Token).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                return "HTTP " + ((int)response.StatusCode).ToString(System.Globalization.CultureInfo.InvariantCulture);
            }

            return null;
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            if (cancellationToken.IsCancellationRequested && !timeout.IsCancellationRequested)
            {
                throw;
            }

            return ex.Message;
        }
    }

    private static void ApplyHeaders(HttpPushTargetRow target, HttpRequestMessage request)
    {
        foreach (var header in ReadHeaders(target.HeadersJson))
        {
            if (string.IsNullOrWhiteSpace(header.Name) || header.Name.Equals("content-type", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (!request.Headers.TryAddWithoutValidation(header.Name, header.Value))
            {
                request.Content?.Headers.TryAddWithoutValidation(header.Name, header.Value);
            }
        }
    }

    private static void ApplyAuth(HttpPushTargetRow target, HttpRequestMessage request, byte[] body)
    {
        var kind = (target.AuthKind ?? "none").Trim().ToLowerInvariant();
        if (kind is "basic" or "bearer")
        {
            request.Headers.Remove("Authorization");
        }

        if (kind == "basic")
        {
            var token = Convert.ToBase64String(Encoding.UTF8.GetBytes(target.AuthUser + ":" + target.AuthSecret));
            request.Headers.TryAddWithoutValidation("Authorization", "Basic " + token);
        }
        else if (kind == "bearer")
        {
            request.Headers.TryAddWithoutValidation("Authorization", "Bearer " + target.AuthSecret);
        }
        else if (kind == "hmac-sha256")
        {
            var header = string.IsNullOrWhiteSpace(target.SignatureHeader) ? "X-DAQ-Signature" : target.SignatureHeader.Trim();
            request.Headers.Remove(header);
            var hex = Convert.ToHexString(HMACSHA256.HashData(Encoding.UTF8.GetBytes(target.AuthSecret), body)).ToLowerInvariant();
            request.Headers.TryAddWithoutValidation(header, "sha256=" + hex);
        }
    }

    internal static List<HeaderPair> ReadHeaders(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return [];
        }

        try
        {
            return JsonSerializer.Deserialize<List<HeaderPair>>(json, NorthboundPayload.Json) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    internal static string WriteHeaders(IEnumerable<HeaderPair>? headers) =>
        JsonSerializer.Serialize(headers?.Where(header => !string.IsNullOrWhiteSpace(header.Name)).ToList() ?? [], NorthboundPayload.Json);

    private (string GatewayId, string Site) Identity()
    {
        var site = _store.ReadPublished().Gateway.Metadata.SiteId;
        if (string.IsNullOrWhiteSpace(site))
        {
            site = "default";
        }

        return ("gw-" + site, site);
    }

    private DiskForwardSpool Spool(string id)
    {
        if (_spools.TryGetValue(id, out var spool))
        {
            return spool;
        }

        spool = new DiskForwardSpool(SpoolDirectory(id));
        _spools[id] = spool;
        return spool;
    }

    private string SpoolDirectory(string id)
    {
        var safe = new string(id.Where(char.IsLetterOrDigit).ToArray());
        if (safe.Length == 0)
        {
            safe = "target";
        }

        return Path.Combine(_store.DataDirectory, "http-spool", safe);
    }

    private static string CursorKey(string id, string name) => "httppush." + id + "." + name;
}

public sealed class HeaderPair
{
    public string Name { get; set; } = "";

    public string Value { get; set; } = "";
}

public sealed class HttpPushWorker(HttpPushDispatcher dispatcher, ILogger<HttpPushWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await dispatcher.TickAsync(stoppingToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning(ex, "HTTP push tick failed");
            }

            try
            {
                await Task.Delay(TimeSpan.FromSeconds(1), stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }
}
