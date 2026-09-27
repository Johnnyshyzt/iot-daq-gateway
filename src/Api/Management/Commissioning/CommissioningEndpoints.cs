using System.Text.Json;
using Studio.Contracts;
using Studio.Host.Auth;
using Studio.Host.Config;
using Studio.Host.Endpoints;
using Studio.Host.Licensing;

namespace Studio.Host.Commissioning;

public static class CommissioningEndpoints
{
    public static void Map(RouteGroupBuilder api)
    {
        api.MapPost("/devices/{id}/self-test", async (string id, SelfTestRunner runner, HttpContext http, CancellationToken cancellationToken) =>
        {
            var report = await runner.RunAsync(id, cancellationToken);
            ConfigAudit.Write(http, http.RequestServices.GetRequiredService<ConfigStore>().Database, "device.self-test", id, report.Summary);
            return ApiResults.Ok(report);
        });

        api.MapGet("/devices/{id}/self-test", (string id, SelfTestRunner runner) =>
        {
            var report = runner.Latest(id);
            return report is null
                ? ApiResults.Error(StatusCodes.Status404NotFound, "self_test_none", "这台设备还没有自检记录。")
                : ApiResults.Ok(report);
        });

        api.MapGet("/ops/self-tests", (int? limit, SelfTestRunner runner) =>
            ApiResults.Ok(new { runs = runner.List(limit ?? 20) }));

        api.MapPost("/devices/{id}/trace", (string id, TraceStart? body, ProtocolTraceBuffer traces, ConfigStore store) =>
        {
            var device = store.GetDevice(id);
            var secrets = new List<string>();
            if (!string.IsNullOrWhiteSpace(device.Spec.Connection.Password))
            {
                secrets.Add(device.Spec.Connection.Password);
            }

            if (device.Spec.Connection.Parameters is not null)
            {
                foreach (var pair in device.Spec.Connection.Parameters)
                {
                    if (pair.Key.Contains("password", StringComparison.OrdinalIgnoreCase)
                        || pair.Key.Contains("secret", StringComparison.OrdinalIgnoreCase)
                        || pair.Key.Contains("token", StringComparison.OrdinalIgnoreCase))
                    {
                        secrets.Add(pair.Value);
                    }
                }
            }

            var session = traces.Start(id, body?.Seconds ?? 60, body?.MaxBytes ?? 1_000_000, secrets);
            return ApiResults.Ok(new
            {
                deviceId = id,
                active = true,
                stopAtUnixMs = session.StopAtUnixMs,
                maxBytes = session.MaxBytes,
                message = "已开始记录。到时或达到大小上限会自动停止。口令已打码。"
            });
        });

        api.MapPost("/devices/{id}/trace/stop", (string id, ProtocolTraceBuffer traces) =>
        {
            var session = traces.Stop(id, "手动停止");
            return session is null
                ? ApiResults.Error(StatusCodes.Status404NotFound, "trace_none", "这台设备没有正在进行的报文记录。")
                : ApiResults.Ok(new { deviceId = id, stopped = true, reason = session.Reason });
        });

        api.MapGet("/devices/{id}/trace", (string id, int? limit, ProtocolTraceBuffer traces) =>
            ApiResults.Ok(traces.Read(id, limit ?? 200)));

        api.MapGet("/devices/{id}/trace/download", (string id, ProtocolTraceBuffer traces) =>
        {
            var path = traces.FileForDownload(id);
            return path is null
                ? ApiResults.Error(StatusCodes.Status404NotFound, "trace_none", "没有可下载的报文。")
                : Results.File(path, "application/x-ndjson", id + ".jsonl");
        });

        api.MapPost("/ops/diagnose", (ConfigStore store, LicenseService licensing, ProtocolTraceBuffer traces, HttpContext http) =>
        {
            var path = DiagnosticBundle.WriteZip(DiagnosticBundle.DefaultPath(store.DataDirectory), store, licensing, traces);
            ConfigAudit.Write(http, store.Database, "ops.diagnose", "bundle", "导出诊断包");
            return Results.File(path, "application/zip", Path.GetFileName(path));
        });

        api.MapGet("/ops/checklist", () =>
        {
            var text = ReadChecklist();
            return ApiResults.Ok(new { title = "现场调试清单", markdown = text });
        });
    }

    public static string ReadChecklist()
    {
        foreach (var candidate in Candidates())
        {
            if (File.Exists(candidate))
            {
                return File.ReadAllText(candidate);
            }
        }

        return "现场调试清单文件不在当前目录。请查看安装包中的 docs/manual/现场调试清单.md。";
    }

    private static IEnumerable<string> Candidates()
    {
        yield return Path.Combine(AppContext.BaseDirectory, "docs", "现场调试清单.md");
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            yield return Path.Combine(dir.FullName, "docs", "manual", "现场调试清单.md");
            dir = dir.Parent;
        }
    }
}

public sealed class TraceStart
{
    public int Seconds { get; set; } = 60;

    public int MaxBytes { get; set; } = 1_000_000;
}
