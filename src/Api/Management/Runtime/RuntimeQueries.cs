using System.Diagnostics;
using System.Globalization;
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
    private readonly IDeviceConnectionTester? _tester;

    public RuntimeQueries(
        ConfigStore store,
        IConfiguration configuration,
        ILogger<RuntimeQueries> logger,
        IEnumerable<ICollectorControl> collectors,
        IFocasConnectProbe focas,
        IEnumerable<IDeviceConnectionTester> testers)
    {
        _store = store;
        _configuration = configuration;
        _logger = logger;
        _collector = collectors.FirstOrDefault();
        _focas = focas;
        _tester = testers.FirstOrDefault();
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

            var template = published.PointTemplates.FirstOrDefault(item =>
                string.Equals(item.Metadata.Id, device.Spec.PointTemplateId, StringComparison.Ordinal));
            var overrides = published.PointSets.FirstOrDefault(set =>
                string.Equals(set.Metadata.DeviceId, device.Metadata.Id, StringComparison.Ordinal));
            var points = PointExpansion.EffectivePoints(template, overrides);
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

    public Task<DeviceTestResult> TestDeviceAsync(string id, CancellationToken cancellationToken)
    {
        return TestCoreAsync(_store.GetDevice(id), cancellationToken);
    }

    public Task<DeviceTestResult> TestUnsavedAsync(DeviceDocument? device, CancellationToken cancellationToken)
    {
        if (device?.Spec is null || string.IsNullOrWhiteSpace(device.Spec.Adapter))
        {
            return Task.FromResult(new DeviceTestResult
            {
                Ok = false,
                Message = "请选择适配器后再测试连接。"
            });
        }

        device.Metadata ??= new DeviceMetadata();
        device.Spec.Connection ??= new DeviceConnection();
        return TestCoreAsync(device, cancellationToken);
    }

    private async Task<DeviceTestResult> TestCoreAsync(DeviceDocument device, CancellationToken cancellationToken)
    {
        var id = string.IsNullOrWhiteSpace(device.Metadata?.Id) ? "unsaved" : device.Metadata.Id;
        var adapter = device.Spec.Adapter ?? "";
        if (string.Equals(adapter, "fanuc.fake", StringComparison.Ordinal)
            || adapter.EndsWith(".sim", StringComparison.Ordinal))
        {
            var fake = new DeviceTestResult
            {
                DeviceId = id,
                Ok = true,
                Handshake = true,
                Adapter = adapter,
                SdkStatus = "none",
                Message = string.Equals(adapter, "fanuc.fake", StringComparison.Ordinal)
                    ? "Fake 适配器握手成功（未连接真实机床）"
                    : "模拟器握手成功（未连接真实机床）",
                LatencyMs = 1,
                HandshakeMs = 1
            };
            _store.AppendLog($"设备 {id} 连接测试成功：{adapter}");
            return fake;
        }

        if (string.Equals(adapter, "fanuc.focas", StringComparison.Ordinal))
        {
            return ProbeFocas(device, id, cancellationToken);
        }

        if (_tester is null || !_tester.CanTest(adapter))
        {
            return new DeviceTestResult
            {
                DeviceId = id,
                Ok = false,
                Adapter = adapter,
                Message = "该适配器没有连接测试。"
            };
        }

        var report = await _tester.TestAsync(ToRequest(device), cancellationToken).ConfigureAwait(false);
        _store.AppendLog(report.Ok
            ? $"设备 {id} 连接测试成功：{adapter}"
            : $"设备 {id} 连接测试失败：{adapter} {report.Error}");
        return new DeviceTestResult
        {
            DeviceId = id,
            Ok = report.Ok,
            Reachable = report.Reachable,
            Handshake = report.Handshake,
            Adapter = adapter,
            Message = report.Message,
            Error = report.Error,
            SdkStatus = report.SdkStatus,
            LatencyMs = report.LatencyMs,
            ReachableMs = report.ReachableMs,
            HandshakeMs = report.HandshakeMs,
            Samples = report.Samples.Select(sample => new DeviceTestSample
            {
                Point = sample.Point,
                Value = sample.Value,
                Quality = sample.Quality,
                Unit = sample.Unit
            }).ToList()
        };
    }

    private DeviceTestResult ProbeFocas(DeviceDocument device, string id, CancellationToken cancellationToken)
    {
        var host = device.Spec.Connection.Host;
        var port = device.Spec.Connection.Port;
        var timeoutMs = device.Spec.Connection.FocasTimeoutMs ?? device.Spec.Connection.TimeoutMs ?? 3000;
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
            _store.AppendLog($"设备 {id} FOCAS 握手异常");
            return new DeviceTestResult
            {
                DeviceId = id,
                Ok = false,
                Adapter = device.Spec.Adapter,
                LatencyMs = (int)watch.ElapsedMilliseconds,
                Message = $"FOCAS 握手失败，进程仍在运行：{ex.Message}"
            };
        }

        watch.Stop();
        _store.AppendLog(result.Ok
            ? $"设备 {id} FOCAS 握手成功 {host}:{port}"
            : $"设备 {id} FOCAS 握手失败 {host}:{port} {result.Code}");
        return new DeviceTestResult
        {
            DeviceId = id,
            Ok = result.Ok,
            Handshake = result.Ok,
            Adapter = device.Spec.Adapter,
            SdkStatus = result.Message.Contains("未找到", StringComparison.Ordinal) ? "missing" : null,
            LatencyMs = (int)watch.ElapsedMilliseconds,
            HandshakeMs = (int)watch.ElapsedMilliseconds,
            Message = result.Message
        };
    }

    private static DeviceConnectionRequest ToRequest(DeviceDocument device)
    {
        var connection = device.Spec.Connection ?? new DeviceConnection();
        var options = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        if (connection.Parameters is not null)
        {
            foreach (var pair in connection.Parameters)
            {
                options[pair.Key] = pair.Value;
            }
        }

        options["host"] = connection.Host;
        options["port"] = connection.Port.ToString(CultureInfo.InvariantCulture);
        options["timeoutMs"] = (connection.TimeoutMs ?? connection.FocasTimeoutMs ?? 3000).ToString(CultureInfo.InvariantCulture);
        options["path"] = connection.Path;
        options["namespace"] = connection.Namespace;
        options["username"] = connection.Username;
        options["password"] = connection.Password;
        return new DeviceConnectionRequest
        {
            DeviceId = device.Metadata?.Id ?? "",
            Adapter = device.Spec.Adapter,
            BrandId = device.Spec.BrandId,
            ControllerModelId = device.Spec.ControllerModelId,
            Options = options
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
        var simulator = device.Spec.Adapter == "fanuc.fake"
            || device.Spec.Adapter.EndsWith(".sim", StringComparison.Ordinal);
        var status = !device.Spec.Enabled
            ? "disabled"
            : simulator
                ? "online"
                : "offline";
        var message = status switch
        {
            "online" => device.Spec.Adapter == "fanuc.fake" ? "Fake 适配器模拟在线" : "模拟器在线",
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
