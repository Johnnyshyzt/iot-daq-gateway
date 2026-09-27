using System.Globalization;
using Adapters.Fanuc.Focas;
using Gateway.Abstractions.Configuration;
using Gateway.Abstractions.Contract;
using Gateway.Abstractions.Contracts;
using Gateway.Abstractions.Models;
using Gateway.Abstractions.Topics;
using Gateway.Host.Configuration;
using Gateway.Host.Logging;
using Gateway.Abstractions.Reliability;
using Gateway.Host.Reliability;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Sinks.Mqtt;

namespace Gateway.Host.Acquisition;

internal sealed class LiveGateway : IHostedService, ICollectorControl, IContractPublisher
{
    private const int MaxObservations = 200;
    private const int MaxErrors = 8;

    private readonly IRuntimeConfigSource _configs;
    private readonly GatewayConfigHolder _holder;
    private readonly IReadOnlyList<ISouthboundAdapterFactory> _factories;
    private readonly ILoggerFactory _loggerFactory;
    private readonly ILogger<LiveGateway> _logger;
    private readonly IReadOnlyList<ISampleWriter> _samples;
    private readonly IReadOnlyList<ILinkStatusWriter> _linkStatus;
    private readonly ReliabilityOptions _reliability;
    private readonly MqttSpool _spool;
    private readonly MqttBufferStatus _buffer;
    private readonly DeviceLinkBook _links = new();
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly object _stateLock = new();
    private readonly object _ctsLock = new();
    private readonly Dictionary<string, CancellationTokenSource> _collectCts = new(StringComparer.OrdinalIgnoreCase);
    private readonly Queue<Observation> _observations = new();
    private readonly List<string> _recentErrors = [];
    private Dictionary<string, DeviceHealth> _health = new(StringComparer.OrdinalIgnoreCase);
    private Session? _session;
    private DateTimeOffset _lastSweepCompleted = DateTimeOffset.UtcNow;

    public LiveGateway(
        IRuntimeConfigSource configs,
        GatewayConfigHolder holder,
        IEnumerable<ISouthboundAdapterFactory> factories,
        ILoggerFactory loggerFactory,
        ILogger<LiveGateway> logger,
        IEnumerable<ISampleWriter> samples,
        IEnumerable<ILinkStatusWriter> linkStatus,
        ReliabilityOptions reliability,
        MqttSpool spool,
        MqttBufferStatus buffer)
    {
        _configs = configs;
        _holder = holder;
        _factories = factories.ToList();
        _loggerFactory = loggerFactory;
        _logger = logger;
        _samples = samples.ToList();
        _linkStatus = linkStatus.ToList();
        _reliability = reliability;
        _spool = spool;
        _buffer = buffer;
    }

    public string? ActiveRevision
    {
        get
        {
            lock (_stateLock)
            {
                return _session?.Revision;
            }
        }
    }

    public TimeSpan SweepInterval
    {
        get
        {
            lock (_stateLock)
            {
                return _session?.Config.Pipeline.SweepInterval ?? TimeSpan.FromSeconds(2);
            }
        }
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var loaded = Load();
        await ApplyAsync(loaded, cancellationToken).ConfigureAwait(false);
        _logger.LogInformation(
            "Gateway session ready source={Path} revision={Revision} devices={Count}",
            loaded.SourcePath,
            loaded.Revision ?? "(none)",
            loaded.Configuration.Devices.Count);
        var configuration = loaded.Configuration;
        _logger.LogInformation(
            "MQTT {Host}:{Port} clientId={ClientId} tls={Tls}",
            configuration.Mqtt.Host,
            configuration.Mqtt.Port,
            configuration.Mqtt.ClientId,
            configuration.Mqtt.Tls);
        if (configuration.Devices.Any(device =>
                device.Enabled && string.Equals(device.Adapter, FocasFanucAdapter.Kind, StringComparison.OrdinalIgnoreCase)))
        {
            if (FocasLibraryFiles.TryLoad(out var focasError))
            {
                _logger.LogInformation("Fwlib64.dll loaded (searched {Path})", FocasLibraryFiles.ExpectedPath);
            }
            else
            {
                _logger.LogWarning("Fwlib64.dll not loaded: {Reason}", focasError);
            }
        }
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            Session? session;
            lock (_stateLock)
            {
                session = _session;
                _session = null;
            }

            if (session is not null)
            {
                await session.DisposeAsync().ConfigureAwait(false);
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<bool> TryReloadAsync(CancellationToken cancellationToken)
    {
        LoadedGateway loaded;
        try
        {
            loaded = Load();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Config reload failed; keeping the running session");
            NoteError(ex.Message);
            return false;
        }

        try
        {
            await ApplyAsync(loaded, cancellationToken).ConfigureAwait(false);
            _logger.LogInformation(
                "Reloaded config from {Path} revision={Revision}",
                loaded.SourcePath,
                loaded.Revision ?? "(none)");
            return true;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Config reload failed; keeping the running session");
            NoteError(ex.Message);
            return false;
        }
    }

    private LoadedGateway Load()
    {
        var snapshot = _configs.Load();
        return new LoadedGateway(
            snapshot.Configuration,
            snapshot.Source,
            snapshot.Revision,
            snapshot.DisplayName,
            IsBundle: true);
    }

    public async Task SweepAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var session = CurrentSession();
            foreach (var adapter in session.Adapters.ToArray())
            {
                await SweepAdapterAsync(session, adapter, cancellationToken).ConfigureAwait(false);
            }

            _lastSweepCompleted = DateTimeOffset.UtcNow;
        }
        finally
        {
            _gate.Release();
        }
    }

    public int HealStalled(DateTimeOffset now)
    {
        var stalled = _links.Stalled(now, _reliability.Stall);
        var cancelled = 0;
        foreach (var deviceId in stalled)
        {
            CancellationTokenSource? cts;
            lock (_ctsLock)
            {
                _collectCts.TryGetValue(deviceId, out cts);
            }

            if (cts is null)
            {
                continue;
            }

            try
            {
                cts.Cancel();
                cancelled++;
                _logger.LogWarning("Watchdog cancelling stalled collect for {DeviceId}", deviceId);
            }
            catch (ObjectDisposedException)
            {
                // The sweep already finished this collect.
            }
        }

        if (cancelled == 0 && now - _lastSweepCompleted > _reliability.Stall + _reliability.Stall)
        {
            _logger.LogWarning(
                "Acquisition loop has not completed a sweep since {Since}",
                _lastSweepCompleted);
        }

        return cancelled;
    }

    public object StatusDocument()
    {
        Session? session;
        Dictionary<string, DeviceHealth> health;
        List<string> errors;
        lock (_stateLock)
        {
            session = _session;
            health = new Dictionary<string, DeviceHealth>(_health, StringComparer.OrdinalIgnoreCase);
            errors = _recentErrors.ToList();
        }

        var config = session?.Config ?? _holder.Current;
        var devices = config.Devices.Select(device =>
        {
            var displayName = OptionText(device.Options, "displayName") ?? device.Id;
            var link = _links.Find(device.Id);
            if (!device.Enabled)
            {
                return DeviceStatus(device.Id, displayName, false, device.Adapter, "disabled", null, "设备已禁用", config, link);
            }

            if (!health.TryGetValue(device.Id, out var item))
            {
                return DeviceStatus(device.Id, displayName, true, device.Adapter, "offline", null, "尚未完成首轮扫描", config, link);
            }

            return DeviceStatus(
                device.Id,
                displayName,
                true,
                device.Adapter,
                item.Status.ToString().ToLowerInvariant(),
                item.Timestamp,
                item.Message ?? "",
                config,
                link);
        }).ToList();

        return new
        {
            name = session?.DisplayName ?? config.Gateway.Id,
            siteId = config.Gateway.Site,
            state = "running",
            mode = "live",
            activeRevision = session?.Revision ?? "",
            utcNow = DateTimeOffset.UtcNow,
            mqttConnected = _buffer.Connected,
            mqttSpoolDepth = _buffer.Depth,
            mqttSpoolDropped = _buffer.Dropped,
            devices,
            recentErrors = errors
        };
    }

    public object ObservationsDocument(string? deviceId, int limit)
    {
        limit = Math.Clamp(limit, 1, MaxObservations);
        List<Observation> snapshot;
        GatewayConfiguration config;
        lock (_stateLock)
        {
            snapshot = _observations.ToList();
            config = _session?.Config ?? _holder.Current;
        }

        IEnumerable<Observation> rows = snapshot;
        if (!string.IsNullOrWhiteSpace(deviceId))
        {
            rows = rows.Where(item => string.Equals(item.DeviceId, deviceId, StringComparison.Ordinal));
        }

        var observations = rows
            .Reverse()
            .Take(limit)
            .Select(item => new
            {
                deviceId = item.DeviceId,
                point = item.Point,
                value = FormatValue(item.Value),
                quality = item.Quality,
                unit = item.Unit,
                timestamp = item.Timestamp,
                topic = DaqTopics.PointTopic(config.Mqtt.TopicTemplate, config.Gateway.Site, item.DeviceId, item.Point)
            })
            .ToList();

        return new { observations };
    }

    public IReadOnlyList<string> TailLogs(int lines) => GatewayLogFiles.Tail(lines);

    private async Task ApplyAsync(LoadedGateway loaded, CancellationToken cancellationToken)
    {
        var session = await Session.OpenAsync(loaded, _factories, _loggerFactory, _logger, _spool, _buffer, cancellationToken)
            .ConfigureAwait(false);
        Session? previous = null;
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            lock (_stateLock)
            {
                previous = _session;
                _session = session;
                _holder.Current = session.Config;
                _health = new Dictionary<string, DeviceHealth>(StringComparer.OrdinalIgnoreCase);
                _observations.Clear();
                _links.Reset();
            }
        }
        finally
        {
            _gate.Release();
        }

        if (previous is not null)
        {
            await previous.DisposeAsync().ConfigureAwait(false);
        }
    }

    private async Task SweepAdapterAsync(Session session, ISouthboundAdapter adapter, CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        if (!_links.ShouldAttempt(adapter.DeviceId, now))
        {
            var waiting = _links.Find(adapter.DeviceId);
            var health = new DeviceHealth
            {
                DeviceId = adapter.DeviceId,
                Status = AdapterStatus.Offline,
                Message = BackoffMessage(waiting),
                Timestamp = now
            };
            Remember(health);
            NoteSampleStatus(health);
            await TryPublishStatusAsync(session, health, cancellationToken).ConfigureAwait(false);
            return;
        }

        var reconnect = _links.Find(adapter.DeviceId)?.Phase != DeviceLinkPhase.Connected;
        _links.BeginAttempt(adapter.DeviceId, now);
        using var stall = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        stall.CancelAfter(_reliability.Stall);
        TrackCollect(adapter.DeviceId, stall);
        try
        {
            if (reconnect)
            {
                try
                {
                    await adapter.ConnectAsync(stall.Token).ConfigureAwait(false);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    _logger.LogDebug(ex, "Connect failed for {DeviceId}", adapter.DeviceId);
                }
            }

            var observations = await adapter.CollectAsync(stall.Token).ConfigureAwait(false);
            foreach (var writer in _samples)
            {
                try
                {
                    writer.Write(observations);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    _logger.LogError(ex, "Failed to store samples for {DeviceId}", adapter.DeviceId);
                }
            }

            foreach (var observation in observations)
            {
                Remember(observation);
                if (!session.Filter.ShouldPublish(observation))
                {
                    continue;
                }

                await session.Sink.PublishObservationAsync(observation, cancellationToken).ConfigureAwait(false);
            }

            var health = await adapter.GetHealthAsync(stall.Token).ConfigureAwait(false);
            if (health.Status == AdapterStatus.Offline)
            {
                NoteLinkFailure(adapter.DeviceId, string.IsNullOrWhiteSpace(health.Message) ? "设备离线" : health.Message!);
            }
            else
            {
                _links.Connected(adapter.DeviceId, DateTimeOffset.UtcNow);
            }

            Remember(health);
            NoteSampleStatus(health);
            await TryPublishStatusAsync(session, health, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            await FailAsync(session, adapter, "采集循环停滞，已重启该设备采集", restart: true, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Sweep failed for {DeviceId} ({Kind})", adapter.DeviceId, adapter.AdapterKind);
            NoteError($"{adapter.DeviceId}: {ex.Message}");
            await FailAsync(session, adapter, ex.Message, restart: false, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            UntrackCollect(adapter.DeviceId, stall);
        }
    }

    private async Task FailAsync(Session session, ISouthboundAdapter adapter, string error, bool restart, CancellationToken cancellationToken)
    {
        NoteLinkFailure(adapter.DeviceId, error);
        var waiting = _links.Find(adapter.DeviceId);
        var health = new DeviceHealth
        {
            DeviceId = adapter.DeviceId,
            Status = AdapterStatus.Offline,
            Message = BackoffMessage(waiting),
            Timestamp = DateTimeOffset.UtcNow
        };
        Remember(health);
        NoteSampleStatus(health);
        await TryPublishStatusAsync(session, health, cancellationToken).ConfigureAwait(false);
        if (!restart || cancellationToken.IsCancellationRequested)
        {
            return;
        }

        try
        {
            await ReplaceAdapterAsync(session, adapter, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Failed to restart device worker {DeviceId}", adapter.DeviceId);
        }
    }

    private async Task ReplaceAdapterAsync(Session session, ISouthboundAdapter current, CancellationToken cancellationToken)
    {
        var device = session.Config.Devices.FirstOrDefault(item =>
            string.Equals(item.Id, current.DeviceId, StringComparison.OrdinalIgnoreCase));
        if (device is null)
        {
            return;
        }

        var replacement = AdapterFactory.CreateOne(device, _factories);
        var index = session.Adapters.FindIndex(item => string.Equals(item.DeviceId, current.DeviceId, StringComparison.OrdinalIgnoreCase));
        if (index >= 0)
        {
            session.Adapters[index] = replacement;
        }
        else
        {
            session.Adapters.Add(replacement);
        }

        try
        {
            await current.DisposeAsync().ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogDebug(ex, "Disposing stalled adapter {DeviceId}", current.DeviceId);
        }

        _logger.LogInformation("Restarted device worker {DeviceId}", current.DeviceId);
        try
        {
            await replacement.ConnectAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogDebug(ex, "Reconnect after restart failed for {DeviceId}", current.DeviceId);
        }
    }

    private void NoteLinkFailure(string deviceId, string error)
    {
        _links.Failed(deviceId, error, DateTimeOffset.UtcNow, _reliability, Random.Shared.NextDouble());
    }

    private void TrackCollect(string deviceId, CancellationTokenSource cts)
    {
        lock (_ctsLock)
        {
            _collectCts[deviceId] = cts;
        }
    }

    private void UntrackCollect(string deviceId, CancellationTokenSource cts)
    {
        lock (_ctsLock)
        {
            if (_collectCts.TryGetValue(deviceId, out var current) && ReferenceEquals(current, cts))
            {
                _collectCts.Remove(deviceId);
            }
        }
    }

    private async Task TryPublishStatusAsync(Session session, DeviceHealth health, CancellationToken cancellationToken)
    {
        try
        {
            await session.Sink.PublishStatusAsync(health, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception publishEx) when (publishEx is not OperationCanceledException)
        {
            _logger.LogDebug(publishEx, "Status publish failed for {DeviceId}", health.DeviceId);
        }
    }

    private static string BackoffMessage(DeviceLink? link)
    {
        if (link is null)
        {
            return "等待重连";
        }

        var when = link.NextRetryUtc?.ToString("HH:mm:ss", CultureInfo.InvariantCulture) ?? "";
        var error = string.IsNullOrWhiteSpace(link.LastError) ? "连接失败" : link.LastError;
        return string.IsNullOrEmpty(when) ? error : $"{error}；下次重试 {when} UTC";
    }

    private object DeviceStatus(
        string id,
        string displayName,
        bool enabled,
        string adapter,
        string status,
        DateTimeOffset? lastSeen,
        string message,
        GatewayConfiguration config,
        DeviceLink? link)
    {
        var phase = link?.Phase switch
        {
            DeviceLinkPhase.Connected => "connected",
            DeviceLinkPhase.Backoff => "backoff",
            _ => enabled ? "connecting" : ""
        };
        return new
        {
            id,
            displayName,
            enabled,
            adapter,
            status,
            lastSeen,
            message,
            statusTopic = StatusTopic(config, id),
            linkPhase = phase,
            nextRetry = link?.NextRetryUtc,
            lastError = link?.LastError ?? "",
            attempt = link?.Attempt ?? 0
        };
    }

    private Session CurrentSession()
    {
        lock (_stateLock)
        {
            return _session ?? throw new InvalidOperationException("Gateway session is not running.");
        }
    }

    private void Remember(Observation observation)
    {
        lock (_stateLock)
        {
            _observations.Enqueue(observation);
            while (_observations.Count > MaxObservations)
            {
                _observations.Dequeue();
            }
        }
    }

    private void NoteSampleStatus(DeviceHealth health)
    {
        foreach (var writer in _samples)
        {
            try
            {
                writer.NoteStatus(health.DeviceId, health.Status.ToString(), health.Timestamp);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogDebug(ex, "Failed to record status for {DeviceId}", health.DeviceId);
            }
        }
    }

    public bool TryLease(out ContractLease? lease)
    {
        lock (_stateLock)
        {
            if (_session is null)
            {
                lease = null;
                return false;
            }

            var sink = _session.Sink;
            var config = _session.Config;
            lease = new ContractLease
            {
                GatewayId = config.Gateway.Id,
                Site = config.Gateway.Site,
                ContractVersion = config.Mqtt.ContractVersion,
                TopicTemplate = config.Mqtt.TopicTemplate,
                PublishAsync = (topic, json, cancellationToken) => sink.PublishDocumentAsync(topic, json, cancellationToken)
            };
            return true;
        }
    }

    private void Remember(DeviceHealth health)
    {
        DeviceHealth? previous;
        lock (_stateLock)
        {
            _health.TryGetValue(health.DeviceId, out previous);
            _health[health.DeviceId] = health;
        }

        if (previous is not null
            && previous.Status == health.Status
            && string.Equals(previous.Message, health.Message, StringComparison.Ordinal))
        {
            return;
        }

        foreach (var writer in _linkStatus)
        {
            try
            {
                writer.Upsert(health.DeviceId, health.Status.ToString().ToLowerInvariant(), health.Message, health.Timestamp);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogDebug(ex, "Link status write failed for {DeviceId}", health.DeviceId);
            }
        }
    }

    private void NoteError(string message)
    {
        lock (_stateLock)
        {
            _recentErrors.Add(message);
            while (_recentErrors.Count > MaxErrors)
            {
                _recentErrors.RemoveAt(0);
            }
        }
    }

    private static string StatusTopic(GatewayConfiguration config, string deviceId) =>
        DaqTopics.StatusTopic(config.Mqtt.StatusTopic, config.Gateway.Site, deviceId);

    private static string? OptionText(IReadOnlyDictionary<string, object?> options, string key)
    {
        if (!options.TryGetValue(key, out var value) || value is null)
        {
            return null;
        }

        return Convert.ToString(value, CultureInfo.InvariantCulture);
    }

    private static string? FormatValue(object? value) => value switch
    {
        null => null,
        string text => text,
        IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
        _ => value.ToString()
    };

    private sealed class Session : IAsyncDisposable
    {
        private Session(
            GatewayConfiguration config,
            string? revision,
            string displayName,
            INorthboundSink sink,
            List<ISouthboundAdapter> adapters,
            ChangeOnlyFilter filter)
        {
            Config = config;
            Revision = revision;
            DisplayName = displayName;
            Sink = sink;
            Adapters = adapters;
            Filter = filter;
        }

        public GatewayConfiguration Config { get; }

        public string? Revision { get; }

        public string DisplayName { get; }

        public INorthboundSink Sink { get; }

        public List<ISouthboundAdapter> Adapters { get; }

        public ChangeOnlyFilter Filter { get; }

        public static async Task<Session> OpenAsync(
            LoadedGateway loaded,
            IReadOnlyList<ISouthboundAdapterFactory> factories,
            ILoggerFactory loggerFactory,
            ILogger logger,
            MqttSpool spool,
            MqttBufferStatus buffer,
            CancellationToken cancellationToken)
        {
            var adapters = AdapterFactory.Create(loaded.Configuration, factories);
            var sink = new MqttObservationSink(
                loaded.Configuration,
                loggerFactory.CreateLogger<MqttObservationSink>(),
                spool,
                buffer);
            try
            {
                await sink.StartAsync(cancellationToken).ConfigureAwait(false);
                foreach (var adapter in adapters)
                {
                    try
                    {
                        await adapter.ConnectAsync(cancellationToken).ConfigureAwait(false);
                    }
                    catch (Exception ex) when (ex is not OperationCanceledException)
                    {
                        logger.LogError(ex, "Failed to connect adapter {DeviceId}", adapter.DeviceId);
                    }
                }

                return new Session(
                    loaded.Configuration,
                    loaded.Revision,
                    loaded.DisplayName,
                    sink,
                    adapters,
                    new ChangeOnlyFilter(loaded.Configuration.Pipeline.ChangeOnly));
            }
            catch
            {
                await sink.DisposeAsync().ConfigureAwait(false);
                foreach (var adapter in adapters)
                {
                    await adapter.DisposeAsync().ConfigureAwait(false);
                }

                throw;
            }
        }

        public async ValueTask DisposeAsync()
        {
            foreach (var adapter in Adapters)
            {
                await adapter.DisposeAsync().ConfigureAwait(false);
            }

            await Sink.DisposeAsync().ConfigureAwait(false);
        }
    }
}
