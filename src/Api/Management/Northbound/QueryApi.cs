using System.Security.Cryptography;
using Gateway.Abstractions.Contract;
using Gateway.Abstractions.Security;
using IotDaq.Persistence;
using IotDaq.Persistence.Visualization;
using Studio.Contracts;
using Studio.Host.Config;
using Studio.Host.Endpoints;
using Studio.Host.Visualization;

namespace Studio.Host.Northbound;

public static class QueryApi
{
    public static void Map(WebApplication app)
    {
        var api = app.MapGroup("/api/query/v1");
        api.MapGet("/devices", (ConfigStore store, GatewayPersistence database) =>
        {
            var (gatewayId, site) = Identity(store);
            var links = database.ListLinkStatus().ToDictionary(row => row.DeviceId, StringComparer.OrdinalIgnoreCase);
            var devices = store.ReadPublished().Devices.Select(device =>
            {
                links.TryGetValue(device.Metadata.Id, out var link);
                return new
                {
                    id = device.Metadata.Id,
                    displayName = device.Metadata.DisplayName,
                    workshop = device.Spec.Workshop ?? "",
                    line = device.Spec.Line ?? "",
                    adapter = device.Spec.Adapter,
                    enabled = device.Spec.Enabled,
                    status = link?.Status ?? "",
                    message = link?.Message ?? ""
                };
            }).ToList();
            return Results.Json(new { schema = NorthboundPayload.SchemaId, gatewayId, site, devices }, NorthboundPayload.Json);
        });

        api.MapGet("/devices/{id}/values", (string id, ConfigStore store, GatewayPersistence database) =>
        {
            var (gatewayId, site) = Identity(store);
            var values = database.Latest(id).Select(sample => Point(gatewayId, site, sample)).ToList();
            return Results.Json(new { schema = NorthboundPayload.SchemaId, gatewayId, site, deviceId = id, values }, NorthboundPayload.Json);
        });

        api.MapGet("/devices/{id}/history", (string id, string? point, string? from, string? to, long? bucketMs, ConfigStore store, GatewayPersistence database) =>
        {
            var (gatewayId, site) = Identity(store);
            var end = ParseTime(to, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
            var start = ParseTime(from, end - 3_600_000);
            if (end < start)
            {
                (start, end) = (end, start);
            }

            var points = string.IsNullOrWhiteSpace(point)
                ? Array.Empty<string>()
                : point.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            var rows = database.History(id, points, start, end, bucketMs ?? 0);
            var values = rows.Select(sample => Point(gatewayId, site, sample)).ToList();
            return Results.Json(new
            {
                schema = NorthboundPayload.SchemaId,
                gatewayId,
                site,
                deviceId = id,
                from = DateTimeOffset.FromUnixTimeMilliseconds(start),
                to = DateTimeOffset.FromUnixTimeMilliseconds(end),
                values
            }, NorthboundPayload.Json);
        });

        api.MapGet("/alarms", (string? deviceId, bool? active, string? from, string? to, int? limit, ConfigStore store, GatewayPersistence database) =>
        {
            var (gatewayId, site) = Identity(store);
            long? start = string.IsNullOrWhiteSpace(from) ? null : ParseTime(from, 0);
            long? end = string.IsNullOrWhiteSpace(to) ? null : ParseTime(to, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
            var rows = database.QueryAlarms(deviceId, active, null, null, start, end, limit ?? 200);
            var alarms = rows.Select(alarm =>
            {
                var action = alarm.Active ? "raise" : "clear";
                var when = alarm.Active ? alarm.RaisedUnixMs : alarm.ClearedUnixMs ?? alarm.RaisedUnixMs;
                return NorthboundPayload.Parse(NorthboundPayload.Alarm(
                    gatewayId, site, alarm.DeviceId, alarm.Id, action, alarm.Code, alarm.Message, alarm.Severity, alarm.PointId,
                    DateTimeOffset.FromUnixTimeMilliseconds(when)));
            }).ToList();
            return Results.Json(new { schema = NorthboundPayload.SchemaId, gatewayId, site, alarms }, NorthboundPayload.Json);
        });

        api.MapGet("/utilization", (string? deviceId, string? from, string? to, ConfigStore store, VisualizationService visualization) =>
        {
            var (gatewayId, site) = Identity(store);
            var end = ParseTime(to, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
            var start = ParseTime(from, end - 86_400_000);
            var report = visualization.Utilization(start, end, deviceId);
            var devices = report.Shifts
                .GroupBy(row => row.DeviceId, StringComparer.Ordinal)
                .Select(group =>
                {
                    var first = group.First();
                    var run = group.Sum(row => row.RunMs);
                    var planned = group.Sum(row => row.PlannedMs);
                    return new NorthboundPayload.UtilizationDevice
                    {
                        DeviceId = first.DeviceId,
                        Workshop = first.Workshop,
                        Line = first.Line,
                        RunningMs = run,
                        IdleMs = group.Sum(row => row.IdleMs),
                        AlarmMs = group.Sum(row => row.AlarmMs),
                        OfflineMs = group.Sum(row => row.OfflineMs),
                        Utilization = UtilizationMath.Ratio(run, planned),
                        PartCount = group.Sum(row => row.PartCount)
                    };
                })
                .ToList();
            var json = NorthboundPayload.Utilization(
                gatewayId,
                site,
                DateTimeOffset.FromUnixTimeMilliseconds(start),
                DateTimeOffset.FromUnixTimeMilliseconds(end),
                devices,
                DateTimeOffset.UtcNow);
            return Results.Text(json, "application/json");
        });
    }

    private static object Point(string gatewayId, string site, SampleView sample)
    {
        object? value = sample.NumericValue is double number ? number : sample.Value;
        return NorthboundPayload.Parse(NorthboundPayload.Point(
            gatewayId,
            site,
            sample.DeviceId,
            sample.PointId,
            value,
            string.IsNullOrWhiteSpace(sample.Quality) ? "good" : sample.Quality,
            sample.Unit,
            DateTimeOffset.FromUnixTimeMilliseconds(sample.TimestampUnixMs),
            versioned: true));
    }

    public static (string GatewayId, string Site) Identity(ConfigStore store)
    {
        var site = store.ReadPublished().Gateway.Metadata.SiteId;
        if (string.IsNullOrWhiteSpace(site))
        {
            site = "default";
        }

        return ("gw-" + site, site);
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
}

public static class ApiKeyEndpoints
{
    public static void Map(RouteGroupBuilder api)
    {
        api.MapGet("/api-keys", (GatewayPersistence database) =>
            ApiResults.Ok(new { keys = database.ListApiKeys().Select(ToView).ToList() }));

        api.MapPost("/api-keys", (ApiKeyWrite? body, HttpContext http, GatewayPersistence database) =>
        {
            var name = body?.Name?.Trim() ?? "";
            if (name.Length == 0 || name.Length > 80)
            {
                return ApiResults.Error(StatusCodes.Status400BadRequest, "invalid_api_key", "请填写 1 到 80 个字符的名称");
            }

            var raw = "daq_" + Base64Url(RandomNumberGenerator.GetBytes(32));
            var row = new ApiKeyRow
            {
                Id = Guid.NewGuid().ToString("N"),
                Name = name,
                Prefix = raw[..12],
                KeyHash = SecretHash.Sha256Hex(raw),
                CreatedUnixMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                CreatedBy = http.Items["studio.user"] as string ?? ""
            };
            database.InsertApiKey(row);
            ConfigAudit.Write(http, database, "apikey.create", row.Id, "创建只读查询密钥 " + row.Name + " (" + row.Prefix + ")");
            return ApiResults.Ok(new
            {
                id = row.Id,
                name = row.Name,
                prefix = row.Prefix,
                key = raw,
                createdUnixMs = row.CreatedUnixMs
            });
        }).RequireWriter();

        api.MapPost("/api-keys/{id}/revoke", (string id, HttpContext http, GatewayPersistence database) =>
        {
            var existing = database.ListApiKeys().FirstOrDefault(row => row.Id == id);
            if (existing is null)
            {
                return ApiResults.Error(StatusCodes.Status404NotFound, "not_found", "密钥不存在");
            }

            database.RevokeApiKey(id, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
            ConfigAudit.Write(http, database, "apikey.revoke", id, "吊销查询密钥 " + existing.Name + " (" + existing.Prefix + ")");
            return ApiResults.Ok(new { revoked = true });
        }).RequireWriter();
    }

    private static object ToView(ApiKeyRow row) => new
    {
        id = row.Id,
        name = row.Name,
        prefix = row.Prefix,
        createdUnixMs = row.CreatedUnixMs,
        createdBy = row.CreatedBy,
        revokedUnixMs = row.RevokedUnixMs,
        lastUsedUnixMs = row.LastUsedUnixMs
    };

    private static string Base64Url(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

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

public sealed class ApiKeyWrite
{
    public string? Name { get; set; }
}

public sealed class ApiKeyMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context, GatewayPersistence database)
    {
        if (!context.Request.Path.StartsWithSegments("/api/query"))
        {
            await next(context);
            return;
        }

        if (HttpMethods.IsGet(context.Request.Method)
            && context.Request.Path.Equals("/api/query/v1/openapi.json", StringComparison.OrdinalIgnoreCase))
        {
            await next(context);
            return;
        }

        var presented = ReadKey(context.Request);
        if (string.IsNullOrEmpty(presented))
        {
            await Reject(context, "需要只读查询密钥");
            return;
        }

        var row = database.FindApiKeyByHash(SecretHash.Sha256Hex(presented));
        if (row is null || row.RevokedUnixMs is not null || !SecretHash.Sha256Equals(presented, row.KeyHash))
        {
            await Reject(context, "查询密钥无效或已吊销");
            return;
        }

        database.TouchApiKeyUsed(row.Id, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
        context.Items["apikey.id"] = row.Id;
        context.Items["apikey.name"] = row.Name;
        await next(context);
    }

    private static string? ReadKey(HttpRequest request)
    {
        if (request.Headers.TryGetValue("X-Api-Key", out var header) && !string.IsNullOrWhiteSpace(header))
        {
            return header.ToString().Trim();
        }

        var authorization = request.Headers.Authorization.ToString();
        const string prefix = "Bearer ";
        if (authorization.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            var token = authorization[prefix.Length..].Trim();
            if (token.StartsWith("daq_", StringComparison.Ordinal))
            {
                return token;
            }
        }

        return null;
    }

    private static async Task Reject(HttpContext context, string message)
    {
        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
        await context.Response.WriteAsJsonAsync(
            new ApiError { Code = "unauthorized", Message = message },
            StudioJson.Options);
    }
}
