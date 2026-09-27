using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using Adapters.Cnc;
using Gateway.Abstractions.Models;
using IotDaq.Persistence;

var devices = ArgInt("--devices", 200);
var minutes = ArgInt("--minutes", 40);
var intervalMs = ArgInt("--interval-ms", 2000);
var jsonPath = ArgValue("--out") ?? Path.Combine(Path.GetTempPath(), "soak-report.json");
var markdownPath = ArgValue("--markdown");
var data = ArgValue("--data") ?? Directory.CreateTempSubdirectory("iot-daq-soak").FullName;

string[] points = ["state", "alarm", "alarmNumber", "program", "partCount", "spindleSpeed", "spindleLoad", "feedRate"];
var ids = Enumerable.Range(1, devices).Select(index => $"sim-{index:000}").ToArray();
var database = GatewayPersistence.Open(data, "Sqlite", null);
database.EnsureReady();

var process = Process.GetCurrentProcess();
var cpuStart = process.TotalProcessorTime;
var wall = Stopwatch.StartNew();
var samples = new List<Sample>();
var latencies = new List<double>();
var deadline = DateTimeOffset.UtcNow.AddMinutes(minutes);
var nextSample = DateTimeOffset.UtcNow;
var sweeps = 0;
var observations = 0L;
Console.WriteLine($"soak devices={devices} minutes={minutes} intervalMs={intervalMs} data={data}");

while (DateTimeOffset.UtcNow < deadline)
{
    var started = Stopwatch.StartNew();
    var now = DateTimeOffset.UtcNow;
    var batch = new List<Observation>(devices * points.Length);
    foreach (var id in ids)
    {
        batch.AddRange(BrandSimulatorAdapter.Sample(id, "fanuc", now, points));
    }

    database.Write(batch);
    started.Stop();
    latencies.Add(started.Elapsed.TotalMilliseconds);
    sweeps++;
    observations += batch.Count;
    if (now >= nextSample)
    {
        samples.Add(Capture(process, sweeps, observations, latencies[^1]));
        Console.WriteLine(
            $"{now:HH:mm:ss} sweep={sweeps} batchMs={latencies[^1]:0.0} wsMB={samples[^1].WorkingSetMb:0.0} managedMB={samples[^1].ManagedMb:0.0} threads={samples[^1].Threads}");
        nextSample = now.AddSeconds(30);
    }

    var remain = intervalMs - (int)started.ElapsedMilliseconds;
    if (remain > 0)
    {
        Thread.Sleep(remain);
    }
}

GC.Collect();
GC.WaitForPendingFinalizers();
GC.Collect();
var afterGc = Capture(process, sweeps, observations, latencies.Count == 0 ? 0 : latencies[^1]);
wall.Stop();
process.Refresh();
var cpu = process.TotalProcessorTime - cpuStart;
var cpuRatio = wall.Elapsed.TotalMilliseconds <= 0
    ? 0
    : cpu.TotalMilliseconds / wall.Elapsed.TotalMilliseconds / Math.Max(1, Environment.ProcessorCount);

latencies.Sort();
var report = new
{
    startedUtc = DateTimeOffset.UtcNow.Add(-wall.Elapsed),
    finishedUtc = DateTimeOffset.UtcNow,
    durationSeconds = Math.Round(wall.Elapsed.TotalSeconds, 1),
    devices,
    pointsPerDevice = points.Length,
    intervalMs,
    sweeps,
    observations,
    dataDirectory = data,
    databaseBytes = new FileInfo(Path.Combine(data, "gateway.db")).Length,
    cpuRatio,
    processorCount = Environment.ProcessorCount,
    latencyMs = new
    {
        mean = latencies.Count == 0 ? 0 : Math.Round(latencies.Average(), 2),
        p50 = Percentile(latencies, 0.50),
        p95 = Percentile(latencies, 0.95),
        max = latencies.Count == 0 ? 0 : Math.Round(latencies[^1], 2)
    },
    gc = new
    {
        gen0 = GC.CollectionCount(0),
        gen1 = GC.CollectionCount(1),
        gen2 = GC.CollectionCount(2)
    },
    workingSetMb = new
    {
        first = samples.Count == 0 ? afterGc.WorkingSetMb : samples[0].WorkingSetMb,
        max = samples.Count == 0 ? afterGc.WorkingSetMb : samples.Max(sample => sample.WorkingSetMb),
        last = samples.Count == 0 ? afterGc.WorkingSetMb : samples[^1].WorkingSetMb,
        afterGc = afterGc.ManagedMb
    },
    managedMb = new
    {
        first = samples.Count == 0 ? afterGc.ManagedMb : samples[0].ManagedMb,
        max = samples.Count == 0 ? afterGc.ManagedMb : samples.Max(sample => sample.ManagedMb),
        last = samples.Count == 0 ? afterGc.ManagedMb : samples[^1].ManagedMb,
        afterGc = afterGc.ManagedMb
    },
    threads = new
    {
        first = samples.Count == 0 ? afterGc.Threads : samples[0].Threads,
        max = samples.Count == 0 ? afterGc.Threads : samples.Max(sample => sample.Threads),
        last = samples.Count == 0 ? afterGc.Threads : samples[^1].Threads
    },
    samples
};

Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(jsonPath))!);
var json = JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true });
File.WriteAllText(jsonPath, json);
Console.WriteLine(json);
if (!string.IsNullOrWhiteSpace(markdownPath))
{
    File.WriteAllText(markdownPath, Markdown(report));
}

static Sample Capture(Process process, int sweeps, long observations, double lastBatchMs)
{
    process.Refresh();
    return new Sample
    {
        Utc = DateTimeOffset.UtcNow,
        Sweeps = sweeps,
        Observations = observations,
        LastBatchMs = Math.Round(lastBatchMs, 2),
        WorkingSetMb = process.WorkingSet64 / 1024d / 1024d,
        ManagedMb = GC.GetTotalMemory(false) / 1024d / 1024d,
        Threads = process.Threads.Count,
        Gen0 = GC.CollectionCount(0),
        Gen1 = GC.CollectionCount(1),
        Gen2 = GC.CollectionCount(2)
    };
}

static double Percentile(List<double> sorted, double p)
{
    if (sorted.Count == 0)
    {
        return 0;
    }

    var index = (int)Math.Clamp(Math.Ceiling(p * sorted.Count) - 1, 0, sorted.Count - 1);
    return Math.Round(sorted[index], 2);
}

static int ArgInt(string name, int fallback)
{
    var text = ArgValue(name);
    return int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) ? value : fallback;
}

static string? ArgValue(string name)
{
    var args = Environment.GetCommandLineArgs();
    for (var i = 0; i < args.Length - 1; i++)
    {
        if (string.Equals(args[i], name, StringComparison.Ordinal))
        {
            return args[i + 1];
        }
    }

    return null;
}

static string Markdown(dynamic report)
{
    return $"""
        # 采集浸泡报告 / Soak report

        实测，不是估算。工具是 `tools/soak`，路径是模拟器采样加 `SampleIngest` 写入 SQLite，和现场模拟器设备走的是同一段入库代码。

        | 项 | 值 |
        | --- | --- |
        | 设备 | {report.devices} |
        | 每台点数 | {report.pointsPerDevice} |
        | 周期 | {report.intervalMs} ms |
        | 时长 | {report.durationSeconds} s |
        | 扫描轮数 | {report.sweeps} |
        | 观测条数 | {report.observations} |
        | 批延迟 p50 / p95 / max | {report.latencyMs.p50} / {report.latencyMs.p95} / {report.latencyMs.max} ms |
        | CPU 占整机比例 | {report.cpuRatio:0.000} （核数 {report.processorCount}，进程 CPU 时间 / 墙钟 / 核数） |
        | 工作集 首 / 最大 / 末 | {report.workingSetMb.first:0.0} / {report.workingSetMb.max:0.0} / {report.workingSetMb.last:0.0} MB |
        | 托管堆 首 / 最大 / 末 / GC 后 | {report.managedMb.first:0.0} / {report.managedMb.max:0.0} / {report.managedMb.last:0.0} / {report.managedMb.afterGc:0.0} MB |
        | 线程 首 / 最大 / 末 | {report.threads.first} / {report.threads.max} / {report.threads.last} |
        | GC gen0 / gen1 / gen2 | {report.gc.gen0} / {report.gc.gen1} / {report.gc.gen2} |
        | gateway.db | {report.databaseBytes} bytes |

        """;
}

sealed class Sample
{
    public DateTimeOffset Utc { get; set; }

    public int Sweeps { get; set; }

    public long Observations { get; set; }

    public double LastBatchMs { get; set; }

    public double WorkingSetMb { get; set; }

    public double ManagedMb { get; set; }

    public int Threads { get; set; }

    public int Gen0 { get; set; }

    public int Gen1 { get; set; }

    public int Gen2 { get; set; }
}
