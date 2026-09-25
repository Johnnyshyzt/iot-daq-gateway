using System.Diagnostics;
using System.Text.Json;
using Gateway.Abstractions.Contracts;
using Gateway.Abstractions.Topics;
using Studio.Contracts;
using Studio.Host.Config;

namespace Studio.Host.Runtime;

public sealed class RuntimeQueries
{
    private static readonly JsonSerializerOptions LiveJson = new(StudioJson.Options)
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly ConfigStore _store;
    private readonly IConfiguration _configuration;
    private readonly ILogger<RuntimeQueries> _logger;
    private readonly ICollectorControl? _collector;
    private readonly IFocasConnectProbe _focas;

    public RuntimeQueries(
        ConfigStore store,
        IConfiguration configuration,
        ILogger<RuntimeQueries> logger,
        IEnumerable<ICollectorControl> collectors,
        IFocasConnectProbe focas)
    {
        _store = store;
        _configuration = configuration;
        _logger = logger;
        _collector = collectors.FirstOrDefault();
        _focas = focas;
    }

    public async Task<RuntimeStatus> StatusAsync(CancellationToken cancellationToken)
    {
        if (_collector is not null)
        {
            return ReadDocument<RuntimeStatus>(_collector.StatusDocument()) ?? MockStatus();
        }

        var live = await TryGetAsync<RuntimeStatus>("/api/v1/runtime/status", cancellationToken);
        return live ?? MockStatus();
    }

    public async Task<ObservationList> ObservationsAsync(string? deviceId, int limit, CancellationToken cancellationToken)
    {
        if (_collector is not null)
        {
            return ReadDocument<ObservationList>(_collector.ObservationsDocument(deviceId, limit))
                ?? MockObservations(deviceId, limit);
        }

        var query = string.IsNullOrWhiteSpace(deviceId)
            ? $"?limit={limit}"
            : $"?deviceId={Uri.EscapeDataString(deviceId)}&limit={limit}";
        var live = await TryGetAsync<ObservationList>("/api/v1/runtime/observations" + query, cancellationToken);
        return live ?? MockObservations(deviceId, limit);
    }

    public async Task<LogTail> LogsAsync(int lines, CancellationToken cancellationToken)
    {
        if (_collector is not null)
        {
            var collected = _collector.TailLogs(lines);
            if (collected.Count > 0)
            {
                return new LogTail { Lines = collected.ToList() };
            }
        }

        var live = await TryGetAsync<LogTail>($"/api/v1/runtime/logs/tail?lines={lines}", cancellationToken);
        return live ?? Logs(lines);
    }

    private static T? ReadDocument<T>(object document)
    {
        var json = JsonSerializer.Serialize(document, LiveJson);
        return JsonSerializer.Deserialize<T>(json, LiveJson);
    }

    public RuntimeStatus MockStatus()
    {
        var published = _store.ReadPublished();
        var now = DateTimeOffset.UtcNow;
        var devices = published.Devices
            .OrderBy(device => device.Metadata.Id, StringComparer.Ordinal)
            .Select(device => ToHealth(device, now, published))
            .ToList();

        return new RuntimeStatus
        {
            Name = published.Gateway.Metadata.Name,
            SiteId = published.Gateway.Metadata.SiteId,
            State = "running",
            Mode = "mock",
            ActiveRevision = _store.ActiveRevision(),
            UtcNow = now,
            Devices = devices,
            RecentErrors = RecentErrors()
        };
    }

    private async Task<T?> TryGetAsync<T>(string path, CancellationToken cancellationToken)
    {
        var baseUrl = _configuration["Studio:GatewayLoopback"];
        if (string.IsNullOrWhiteSpace(baseUrl))
        {
            return default;
        }

        try
        {
            using var client = new HttpClient { Timeout = TimeSpan.FromMilliseconds(800) };
            using var response = await client.GetAsync(baseUrl.TrimEnd('/') + path, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                return default;
            }

            return await response.Content.ReadFromJsonAsync<T>(LiveJson, cancellationToken);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            _logger.LogDebug(ex, "Gateway loopback unavailable; using mock runtime");
            return default;
        }
    }

    private ObservationList MockObservations(string? deviceId, int limit)
    {
        limit = Math.Clamp(limit, 1, 200);
        var published = _store.ReadPublished();
        var now = DateTimeOffset.UtcNow;
        var phase = now.ToUnixTimeSeconds() % 60;
        var (state, alarm, quality) = phase switch
        {
            < 20 => ("IDLE", "0", "good"),
            < 50 => ("RUNNING", "0", "good"),
            _ => ("ALARM", "100", "uncertain")
        };

        var rows = new List<ObservationView>();
        foreach (var device in published.Devices.OrderBy(device => device.Metadata.Id, StringComparer.Ordinal))
        {
            if (!device.Spec.Enabled)
            {
                continue;
            }

            if (!string.IsNullOrWhiteSpace(deviceId)
                && !string.Equals(device.Metadata.Id, deviceId, StringComparison.Ordinal))
            {
                continue;
            }

            var points = published.PointSets
                .FirstOrDefault(set => string.Equals(set.Metadata.DeviceId, device.Metadata.Id, StringComparison.Ordinal))
                ?.Spec.Points ?? [];
            foreach (var point in points.Where(point => point.Enabled))
            {
                rows.Add(new ObservationView
                {
                    DeviceId = device.Metadata.Id,
                    Point = point.Id,
                    Value = Sample(point, state, alarm),
                    Quality = quality,
                    Unit = point.Unit,
                    Topic = TopicOrEmpty(
                        () => DaqTopics.PointTopic(
                            published.Mqtt.Spec.TopicTemplate,
                            published.Gateway.Metadata.SiteId,
                            device.Metadata.Id,
                            point.Id)),
                    Timestamp = now
                });
            }
        }

        return new ObservationList { Observations = rows.Take(limit).ToList() };
    }

    public LogTail Logs(int lines) => new()
    {
        Lines = _store.ReadLogTail(lines).ToList()
    };

    public async Task<DeviceTestResult> TestDeviceAsync(string id, CancellationToken cancellationToken)
    {
        var device = _store.GetDevice(id);
        if (string.Equals(device.Spec.Adapter, "fanuc.fake", StringComparison.Ordinal))
        {
            var fake = new DeviceTestResult
            {
                DeviceId = id,
                Ok = true,
                Adapter = device.Spec.Adapter,
                Message = "Fake 适配器握手成功（未连接真实机床）",
                LatencyMs = 1
            };
            _store.AppendLog($"设备 {id} 连接测试成功：Fake");
            return fake;
        }

        if (!string.Equals(device.Spec.Adapter, "fanuc.focas", StringComparison.Ordinal))
        {
            return new DeviceTestResult
            {
                DeviceId = id,
                Ok = false,
                Adapter = device.Spec.Adapter,
                Message = "M1 只支持 fanuc.fake 与 fanuc.focas"
            };
        }

        var host = device.Spec.Connection.Host;
        var port = device.Spec.Connection.Port;
        var timeoutMs = device.Spec.Connection.FocasTimeoutMs ?? 3000;
        var watch = Stopwatch.StartNew();
        FocasConnectProbeResult result;
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            result = _focas.Probe(host, port, timeoutMs);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            watch.Stop();
            var crashed = new DeviceTestResult
            {
                DeviceId = id,
                Ok = false,
                Adapter = device.Spec.Adapter,
                LatencyMs = (int)watch.ElapsedMilliseconds,
                Message = $"FOCAS 握手失败，进程仍在运行：{ex.Message}"
            };
            _store.AppendLog($"设备 {id} FOCAS 握手异常");
            return crashed;
        }

        watch.Stop();
        _store.AppendLog(result.Ok
            ? $"设备 {id} FOCAS 握手成功 {host}:{port}"
            : $"设备 {id} FOCAS 握手失败 {host}:{port} {result.Code}");
        return new DeviceTestResult
        {
            DeviceId = id,
            Ok = result.Ok,
            Adapter = device.Spec.Adapter,
            LatencyMs = (int)watch.ElapsedMilliseconds,
            Message = result.Message
        };
    }

    private List<string> RecentErrors()
    {
        return _store.ReadLogTail(200)
            .Where(line => line.Contains("失败", StringComparison.Ordinal) || line.Contains("错误", StringComparison.OrdinalIgnoreCase) || line.Contains("error", StringComparison.OrdinalIgnoreCase))
            .TakeLast(5)
            .Reverse()
            .ToList();
    }

    private static DeviceHealthView ToHealth(DeviceDocument device, DateTimeOffset now, ConfigBundle published)
    {
        var status = !device.Spec.Enabled
            ? "disabled"
            : device.Spec.Adapter == "fanuc.fake"
                ? "online"
                : "offline";
        var message = status switch
        {
            "online" => "Fake 适配器模拟在线",
            "offline" => "采集未启动，当前是模拟运行态",
            _ => "设备已禁用"
        };
        return new DeviceHealthView
        {
            Id = device.Metadata.Id,
            DisplayName = device.Metadata.DisplayName,
            Enabled = device.Spec.Enabled,
            Adapter = device.Spec.Adapter,
            Status = status,
            LastSeen = status == "online" ? now : null,
            Message = message,
            StatusTopic = TopicOrEmpty(
                () => DaqTopics.StatusTopic(
                    published.Mqtt.Spec.StatusTopic,
                    published.Gateway.Metadata.SiteId,
                    device.Metadata.Id))
        };
    }

    private static string TopicOrEmpty(Func<string> expand)
    {
        try
        {
            return expand();
        }
        catch (ArgumentException)
        {
            return "";
        }
    }

    private static string Sample(PointDefinition point, string state, string alarm)
    {
        return point.Id switch
        {
            "state" => state,
            "alarm" => alarm,
            "program" => "O0001",
            _ => point.DataType switch
            {
                "bool" => "true",
                "int" or "float" or "number" => "0",
                _ => "mock"
            }
        };
    }
}
