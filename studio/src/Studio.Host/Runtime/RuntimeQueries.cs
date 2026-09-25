using System.Diagnostics;
using System.Net.Sockets;
using System.Text.Json;
using Studio.Contracts;
using Studio.Host.Config;

namespace Studio.Host.Runtime;

public sealed class RuntimeQueries(ConfigStore store, IConfiguration configuration, ILogger<RuntimeQueries> logger)
{
    private static readonly JsonSerializerOptions LiveJson = new(StudioJson.Options)
    {
        PropertyNameCaseInsensitive = true
    };

    public async Task<RuntimeStatus> StatusAsync(CancellationToken cancellationToken)
    {
        var live = await TryGetAsync<RuntimeStatus>("/api/v1/runtime/status", cancellationToken);
        return live ?? MockStatus();
    }

    public async Task<ObservationList> ObservationsAsync(string? deviceId, int limit, CancellationToken cancellationToken)
    {
        var query = string.IsNullOrWhiteSpace(deviceId)
            ? $"?limit={limit}"
            : $"?deviceId={Uri.EscapeDataString(deviceId)}&limit={limit}";
        var live = await TryGetAsync<ObservationList>("/api/v1/runtime/observations" + query, cancellationToken);
        return live ?? MockObservations(deviceId, limit);
    }

    public async Task<LogTail> LogsAsync(int lines, CancellationToken cancellationToken)
    {
        var live = await TryGetAsync<LogTail>($"/api/v1/runtime/logs/tail?lines={lines}", cancellationToken);
        return live ?? Logs(lines);
    }

    public RuntimeStatus MockStatus()
    {
        var published = store.ReadPublished();
        var now = DateTimeOffset.UtcNow;
        var devices = published.Devices
            .OrderBy(device => device.Metadata.Id, StringComparer.Ordinal)
            .Select(device => ToHealth(device, now))
            .ToList();

        return new RuntimeStatus
        {
            Name = published.Gateway.Metadata.Name,
            SiteId = published.Gateway.Metadata.SiteId,
            State = "running",
            Mode = "mock",
            ActiveRevision = store.ActiveRevision(),
            UtcNow = now,
            Devices = devices,
            RecentErrors = RecentErrors()
        };
    }

    private async Task<T?> TryGetAsync<T>(string path, CancellationToken cancellationToken)
    {
        var baseUrl = configuration["Studio:GatewayLoopback"];
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
            logger.LogDebug(ex, "Gateway loopback unavailable; using mock runtime");
            return default;
        }
    }

    private ObservationList MockObservations(string? deviceId, int limit)
    {
        limit = Math.Clamp(limit, 1, 200);
        var published = store.ReadPublished();
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
                    Timestamp = now
                });
            }
        }

        return new ObservationList { Observations = rows.Take(limit).ToList() };
    }

    public LogTail Logs(int lines) => new()
    {
        Lines = store.ReadLogTail(lines).ToList()
    };

    public async Task<DeviceTestResult> TestDeviceAsync(string id, CancellationToken cancellationToken)
    {
        var device = store.GetDevice(id);
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
            store.AppendLog($"设备 {id} 连接测试成功：Fake");
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
        var watch = Stopwatch.StartNew();
        try
        {
            using var client = new TcpClient();
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(2));
            await client.ConnectAsync(host, port, timeout.Token);
            watch.Stop();
            var ok = new DeviceTestResult
            {
                DeviceId = id,
                Ok = true,
                Adapter = device.Spec.Adapter,
                LatencyMs = (int)watch.ElapsedMilliseconds,
                Message = "TCP 端口可达。完整 FOCAS 握手需在带 Fwlib64.dll 的网关进程中进行，Studio 独立进程只探测端口。"
            };
            store.AppendLog($"设备 {id} 端口探测成功 {host}:{port}");
            return ok;
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            watch.Stop();
            var failed = new DeviceTestResult
            {
                DeviceId = id,
                Ok = false,
                Adapter = device.Spec.Adapter,
                LatencyMs = (int)watch.ElapsedMilliseconds,
                Message = $"无法连接 {host}:{port}。{ex.Message}"
            };
            store.AppendLog($"设备 {id} 端口探测失败 {host}:{port}");
            return failed;
        }
    }

    private List<string> RecentErrors()
    {
        return store.ReadLogTail(200)
            .Where(line => line.Contains("失败", StringComparison.Ordinal) || line.Contains("错误", StringComparison.OrdinalIgnoreCase) || line.Contains("error", StringComparison.OrdinalIgnoreCase))
            .TakeLast(5)
            .Reverse()
            .ToList();
    }

    private static DeviceHealthView ToHealth(DeviceDocument device, DateTimeOffset now)
    {
        var status = !device.Spec.Enabled
            ? "disabled"
            : device.Spec.Adapter == "fanuc.fake"
                ? "online"
                : "offline";
        var message = status switch
        {
            "online" => "Fake 适配器模拟在线",
            "offline" => "FOCAS 运行态未接入，Studio 独立进程只提供模拟",
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
            Message = message
        };
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
