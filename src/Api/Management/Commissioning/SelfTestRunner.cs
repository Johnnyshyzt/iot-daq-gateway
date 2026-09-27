using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using Adapters.Cnc.Drivers;
using Cnc.Catalog;
using Studio.Contracts;
using Studio.Host.Config;
using Studio.Host.Runtime;

namespace Studio.Host.Commissioning;

public sealed class SelfTestRunner(ConfigStore store, RuntimeQueries runtime, ProtocolTraceBuffer traces)
{
    public async Task<SelfTestReport> RunAsync(string deviceId, CancellationToken cancellationToken)
    {
        var device = store.GetDevice(deviceId);
        var started = DateTimeOffset.UtcNow;
        var stages = new List<SelfTestStage>();
        var adapter = CncCatalog.Current.FindAdapter(device.Spec.Adapter);
        var protocol = adapter?.Protocol ?? "";
        var simulated = adapter is null
            ? device.Spec.Adapter.Contains("fake", StringComparison.OrdinalIgnoreCase) || device.Spec.Adapter.EndsWith(".sim", StringComparison.OrdinalIgnoreCase)
            : adapter.Kind == "simulator" || adapter.Protocol is "sim" or "fake";
        var connection = device.Spec.Connection ?? new DeviceConnection();
        var host = (connection.Host ?? "").Trim();
        var port = connection.Port;
        var parameters = connection.Parameters ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var parameterView = parameters.ToDictionary(pair => pair.Key, pair => (string?)pair.Value, StringComparer.OrdinalIgnoreCase);
        var brand = device.Spec.BrandId ?? adapter?.BrandId ?? "";
        var hint = ProtocolHints.For(protocol, brand, parameterView, null);

        stages.Add(await DnsStage(host, simulated, hint, cancellationToken));
        stages.Add(await TcpStage(host, port, simulated, protocol, hint, cancellationToken));

        var probeWatch = Stopwatch.StartNew();
        var probe = await runtime.TestDeviceAsync(deviceId, cancellationToken);
        probeWatch.Stop();
        var sdk = probe.SdkStatus;
        hint = ProtocolHints.For(protocol, brand, parameterView, sdk);
        stages.Add(new SelfTestStage
        {
            Id = "handshake",
            Title = "协议握手",
            Status = probe.Handshake || probe.Ok ? "pass" : "fail",
            ElapsedMs = probe.HandshakeMs > 0 ? probe.HandshakeMs : (int)probeWatch.ElapsedMilliseconds,
            Detail = string.IsNullOrWhiteSpace(probe.Message) ? (probe.Error ?? "没有握手结果") : probe.Message,
            Hint = probe.Handshake || probe.Ok ? "" : hint
        });

        var sampleCount = probe.Samples?.Count ?? 0;
        string sampleStatus;
        string sampleDetail;
        if (simulated && (probe.Ok || probe.Handshake))
        {
            sampleStatus = "pass";
            sampleDetail = "模拟器握手成功，不读取真实机床点值。";
        }
        else if (!(probe.Handshake || probe.Ok))
        {
            sampleStatus = stages.Any(stage => stage.Id == "tcp" && stage.Status == "fail") ? "skip" : "fail";
            sampleDetail = "握手未通过，没有读取点值。";
        }
        else if (sampleCount == 0)
        {
            sampleStatus = "fail";
            sampleDetail = "握手成功，但没有读到已配置的点。";
        }
        else
        {
            sampleStatus = "pass";
            sampleDetail = "读到 " + sampleCount.ToString(System.Globalization.CultureInfo.InvariantCulture) + " 个点："
                + string.Join("、", probe.Samples!.Take(8).Select(sample => sample.Point));
        }

        stages.Add(new SelfTestStage
        {
            Id = "sample",
            Title = "读取样例点",
            Status = sampleStatus,
            ElapsedMs = 0,
            Detail = sampleDetail,
            Hint = sampleStatus == "pass" ? "" : hint
        });

        var interval = device.Spec.IntervalMs <= 0 ? 1000 : device.Spec.IntervalMs;
        var timingMs = stages.Where(stage => stage.Id is "tcp" or "handshake").Sum(stage => stage.ElapsedMs);
        var timingStatus = simulated || timingMs <= Math.Max(interval, 3000) ? "pass" : timingMs > 10_000 ? "fail" : "pass";
        stages.Add(new SelfTestStage
        {
            Id = "timing",
            Title = "耗时",
            Status = timingStatus,
            ElapsedMs = timingMs,
            Detail = simulated
                ? "模拟器耗时可以忽略。"
                : "网络加握手约 " + timingMs.ToString(System.Globalization.CultureInfo.InvariantCulture) + " ms，采集周期 " + interval.ToString(System.Globalization.CultureInfo.InvariantCulture) + " ms。",
            Hint = timingStatus == "fail" || (!simulated && timingMs > interval)
                ? "耗时大于采集周期时，现场应加大周期或减少每次读取的点数。"
                : ""
        });

        foreach (var stage in stages)
        {
            traces.Note(deviceId, "self-test", stage.Title + " " + stage.Status + " " + stage.Detail);
        }

        var passed = stages.All(stage => stage.Status != "fail");
        var summary = passed ? "自检通过" : "自检未通过：" + string.Join("；", stages.Where(stage => stage.Status == "fail").Select(stage => stage.Title));
        var finished = DateTimeOffset.UtcNow;
        var json = JsonSerializer.Serialize(stages, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        var id = store.Database.InsertSelfTest(deviceId, passed, summary, json, started.ToUnixTimeMilliseconds(), finished.ToUnixTimeMilliseconds());
        return new SelfTestReport
        {
            Id = id,
            DeviceId = deviceId,
            Adapter = device.Spec.Adapter,
            Protocol = protocol,
            Passed = passed,
            Summary = summary,
            StartedUnixMs = started.ToUnixTimeMilliseconds(),
            FinishedUnixMs = finished.ToUnixTimeMilliseconds(),
            Stages = stages,
            Hint = hint
        };
    }

    public SelfTestReport? Latest(string deviceId)
    {
        var row = store.Database.LatestSelfTest(deviceId);
        return row is null ? null : FromRow(row);
    }

    public IReadOnlyList<SelfTestReport> List(int limit) =>
        store.Database.ListSelfTests(limit).Select(FromRow).ToList();

    private static SelfTestReport FromRow(IotDaq.Persistence.SelfTestRunRow row)
    {
        List<SelfTestStage> stages;
        try
        {
            stages = JsonSerializer.Deserialize<List<SelfTestStage>>(row.StagesJson, new JsonSerializerOptions(JsonSerializerDefaults.Web)) ?? [];
        }
        catch (JsonException)
        {
            stages = [];
        }

        return new SelfTestReport
        {
            Id = row.Id,
            DeviceId = row.DeviceId,
            Passed = row.Passed,
            Summary = row.Summary,
            StartedUnixMs = row.StartedUnixMs,
            FinishedUnixMs = row.FinishedUnixMs,
            Stages = stages
        };
    }

    private static async Task<SelfTestStage> DnsStage(string host, bool simulated, string hint, CancellationToken cancellationToken)
    {
        if (simulated)
        {
            return new SelfTestStage
            {
                Id = "dns",
                Title = "名字解析",
                Status = "pass",
                Detail = "模拟器不解析机床主机名。",
                Hint = hint
            };
        }

        if (string.IsNullOrWhiteSpace(host))
        {
            return new SelfTestStage
            {
                Id = "dns",
                Title = "名字解析",
                Status = "fail",
                Detail = "没有填写主机名或 IP。",
                Hint = "在设备连接里填写机床或 Agent 的 IP，或填写采集机能解析的主机名。"
            };
        }

        if (IPAddress.TryParse(host, out _))
        {
            return new SelfTestStage
            {
                Id = "dns",
                Title = "名字解析",
                Status = "pass",
                Detail = host + " 已是 IP，跳过 DNS。"
            };
        }

        var watch = Stopwatch.StartNew();
        try
        {
            var addresses = await Dns.GetHostAddressesAsync(host, cancellationToken);
            watch.Stop();
            if (addresses.Length == 0)
            {
                return new SelfTestStage
                {
                    Id = "dns",
                    Title = "名字解析",
                    Status = "fail",
                    ElapsedMs = (int)watch.ElapsedMilliseconds,
                    Detail = "没有解析到地址。",
                    Hint = "检查 DNS 或改填 IP。采集机和机床通常应在同一网段。"
                };
            }

            return new SelfTestStage
            {
                Id = "dns",
                Title = "名字解析",
                Status = "pass",
                ElapsedMs = (int)watch.ElapsedMilliseconds,
                Detail = "解析到 " + string.Join(", ", addresses.Take(4))
            };
        }
        catch (Exception ex) when (ex is SocketException or IOException)
        {
            watch.Stop();
            return new SelfTestStage
            {
                Id = "dns",
                Title = "名字解析",
                Status = "fail",
                ElapsedMs = (int)watch.ElapsedMilliseconds,
                Detail = ex.Message,
                Hint = "主机名无法解析。现场请改用 IP，或检查采集机的 DNS。"
            };
        }
    }

    private static async Task<SelfTestStage> TcpStage(string host, int port, bool simulated, string protocol, string hint, CancellationToken cancellationToken)
    {
        if (simulated)
        {
            return new SelfTestStage
            {
                Id = "tcp",
                Title = "端口连通",
                Status = "pass",
                Detail = "模拟器不连接真实端口。",
                Hint = hint
            };
        }

        var tcp = await TcpProbe.TryAsync(host, port, 3000, cancellationToken);
        return new SelfTestStage
        {
            Id = "tcp",
            Title = "端口连通",
            Status = tcp.Ok ? "pass" : "fail",
            ElapsedMs = tcp.ElapsedMs,
            Detail = tcp.Ok
                ? host + ":" + port.ToString(System.Globalization.CultureInfo.InvariantCulture) + " 可以连接。"
                : "连不上 " + host + ":" + port.ToString(System.Globalization.CultureInfo.InvariantCulture) + "。" + (tcp.Error ?? ""),
            Hint = tcp.Ok ? "" : hint + " 协议 " + protocol + "。"
        };
    }
}

public sealed class SelfTestStage
{
    public string Id { get; set; } = "";

    public string Title { get; set; } = "";

    public string Status { get; set; } = "";

    public string Detail { get; set; } = "";

    public string Hint { get; set; } = "";

    public int ElapsedMs { get; set; }
}

public sealed class SelfTestReport
{
    public long Id { get; set; }

    public string DeviceId { get; set; } = "";

    public string Adapter { get; set; } = "";

    public string Protocol { get; set; } = "";

    public bool Passed { get; set; }

    public string Summary { get; set; } = "";

    public long StartedUnixMs { get; set; }

    public long FinishedUnixMs { get; set; }

    public string Hint { get; set; } = "";

    public List<SelfTestStage> Stages { get; set; } = [];
}
