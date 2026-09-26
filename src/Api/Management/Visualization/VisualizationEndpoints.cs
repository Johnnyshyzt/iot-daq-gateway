using System.Text;
using IotDaq.Persistence;
using Microsoft.AspNetCore.Routing;
using Studio.Host.Endpoints;
using Studio.Host.Runtime;

namespace Studio.Host.Visualization;

public static class VisualizationEndpoints
{
    public static void Map(RouteGroupBuilder api)
    {
        api.MapGet("/dashboard/overview", async (VisualizationService viz, RuntimeQueries runtime, CancellationToken cancellationToken) =>
        {
            var status = await runtime.StatusAsync(cancellationToken);
            return ApiResults.Ok(viz.Overview(DateTimeOffset.UtcNow, status.Devices));
        });

        api.MapGet("/live/stream", async (HttpContext http, VisualizationService viz, RuntimeQueries runtime, GatewayPersistence database, CancellationToken cancellationToken) =>
        {
            http.Response.Headers.ContentType = "text/event-stream";
            http.Response.Headers.CacheControl = "no-cache";
            http.Response.Headers.Append("X-Accel-Buffering", "no");
            var pending = 0;
            using var subscription = database.SubscribeSamples(() => Interlocked.Exchange(ref pending, 1));
            await WriteSnapshot(http, viz, runtime, cancellationToken);
            var last = DateTime.UtcNow;
            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    break;
                }

                var due = Interlocked.Exchange(ref pending, 0) == 1 || DateTime.UtcNow - last > TimeSpan.FromSeconds(12);
                if (!due)
                {
                    continue;
                }

                if (!await WriteSnapshot(http, viz, runtime, cancellationToken))
                {
                    break;
                }

                last = DateTime.UtcNow;
            }
        });

        api.MapGet("/devices/{id}/detail", async (string id, VisualizationService viz, RuntimeQueries runtime, CancellationToken cancellationToken) =>
        {
            var status = await runtime.StatusAsync(cancellationToken);
            var detail = viz.Detail(id, DateTimeOffset.UtcNow, status.Devices);
            return detail is null
                ? ApiResults.Error(StatusCodes.Status404NotFound, "not_found", "设备不存在")
                : ApiResults.Ok(detail);
        });

        api.MapGet("/samples/series", (string? devices, string? points, string? from, string? to, long? bucketMs, VisualizationService viz) =>
        {
            var end = ParseTime(to, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
            var start = ParseTime(from, end - 8 * 60 * 60 * 1000);
            return ApiResults.Ok(viz.Series(devices, points, start, end, bucketMs ?? 0));
        });

        api.MapGet("/samples/series.csv", (string? devices, string? points, string? from, string? to, long? bucketMs, VisualizationService viz) =>
        {
            var end = ParseTime(to, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
            var start = ParseTime(from, end - 8 * 60 * 60 * 1000);
            var body = viz.SeriesCsv(viz.Series(devices, points, start, end, bucketMs ?? 0));
            return Results.File(Bom(body), "text/csv; charset=utf-8", "history.csv");
        });

        api.MapGet("/alarms/stats", (string? deviceId, string? from, string? to, int? top, VisualizationService viz) =>
        {
            var end = string.IsNullOrWhiteSpace(to) ? (long?)null : ParseTime(to, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
            var start = string.IsNullOrWhiteSpace(from) ? (long?)null : ParseTime(from, 0);
            var stats = viz.AlarmStatistics(start, end, deviceId, top ?? 10);
            return ApiResults.Ok(new { byDevice = stats.ByDevice, byCode = stats.ByCode });
        });

        api.MapGet("/alarms.csv", (string? deviceId, string? from, string? to, GatewayPersistence database, VisualizationService viz) =>
        {
            var end = string.IsNullOrWhiteSpace(to) ? (long?)null : ParseTime(to, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
            var start = string.IsNullOrWhiteSpace(from) ? (long?)null : ParseTime(from, 0);
            var alarms = database.QueryAlarms(deviceId, null, null, null, start, end, 2000);
            return Results.File(Bom(viz.AlarmsCsv(alarms)), "text/csv; charset=utf-8", "alarms.csv");
        });

        api.MapPost("/alarms/{id}/ack", (string id, HttpContext http, GatewayPersistence database) =>
        {
            var user = http.Items["studio.user"] as string ?? "";
            var alarm = database.AcknowledgeAlarm(id, user, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
            return alarm is null
                ? ApiResults.Error(StatusCodes.Status404NotFound, "not_found", "报警不存在")
                : ApiResults.Ok(alarm);
        }).RequireWriter();

        api.MapGet("/utilization", (string? deviceId, string? from, string? to, VisualizationService viz) =>
        {
            var (start, end) = Range(from, to);
            return ApiResults.Ok(viz.Utilization(start, end, deviceId));
        });

        api.MapGet("/utilization.csv", (string? deviceId, string? from, string? to, string? view, VisualizationService viz) =>
        {
            var (start, end) = Range(from, to);
            var report = viz.Utilization(start, end, deviceId);
            var rows = view == "day" ? report.Days : view == "line" ? report.Lines : report.Shifts;
            return Results.File(Bom(viz.UtilizationCsv(rows)), "text/csv; charset=utf-8", "utilization.csv");
        });

        api.MapGet("/utilization.xls", (string? deviceId, string? from, string? to, string? view, VisualizationService viz) =>
        {
            var (start, end) = Range(from, to);
            var report = viz.Utilization(start, end, deviceId);
            var rows = view == "day" ? report.Days : view == "line" ? report.Lines : report.Shifts;
            return Results.File(viz.UtilizationExcel(rows), "application/vnd.ms-excel", "utilization.xls");
        });

        api.MapGet("/viz/settings", (VisualizationService viz) => ApiResults.Ok(viz.Settings()));

        api.MapPut("/viz/settings", (VizSettings? body, VisualizationService viz) =>
        {
            if (body is null)
            {
                return ApiResults.Error(StatusCodes.Status400BadRequest, "invalid_json", "请求无效");
            }

            var issues = viz.SaveSettings(body);
            if (issues.Count > 0)
            {
                return ApiResults.Error(StatusCodes.Status400BadRequest, "invalid_settings", string.Join("；", issues));
            }

            return ApiResults.Ok(viz.Settings());
        }).RequireWriter();
    }

    private static async Task<bool> WriteSnapshot(HttpContext http, VisualizationService viz, RuntimeQueries runtime, CancellationToken cancellationToken)
    {
        try
        {
            var status = await runtime.StatusAsync(cancellationToken);
            var json = System.Text.Json.JsonSerializer.Serialize(viz.Overview(DateTimeOffset.UtcNow, status.Devices), StudioJson.Options);
            await http.Response.WriteAsync("event: overview\ndata: ", cancellationToken);
            await http.Response.WriteAsync(json, cancellationToken);
            await http.Response.WriteAsync("\n\n", cancellationToken);
            await http.Response.Body.FlushAsync(cancellationToken);
            return true;
        }
        catch (OperationCanceledException)
        {
            return false;
        }
        catch (IOException)
        {
            return false;
        }
    }

    private static (long From, long To) Range(string? from, string? to)
    {
        var end = ParseTime(to, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
        var start = ParseTime(from, end - 24 * 60 * 60 * 1000);
        return (start, end);
    }

    private static long ParseTime(string? text, long fallback)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return fallback;
        }

        if (long.TryParse(text, out var unix))
        {
            return unix;
        }

        return DateTimeOffset.TryParse(text, out var parsed) ? parsed.ToUnixTimeMilliseconds() : fallback;
    }

    private static byte[] Bom(string text) => Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(text)).ToArray();

    private static RouteHandlerBuilder RequireWriter(this RouteHandlerBuilder builder) =>
        builder.AddEndpointFilter(async (context, next) =>
        {
            var role = context.HttpContext.Items["studio.role"] as string;
            if (role is not ("admin" or "engineer"))
            {
                return ApiResults.Error(StatusCodes.Status403Forbidden, "forbidden", "当前角色无权修改配置");
            }

            return await next(context);
        });
}
