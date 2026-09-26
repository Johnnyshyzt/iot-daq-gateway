using Adapters.Cnc.Drivers;
using IotDaq.Persistence;
using Studio.Contracts;
using Studio.Host.Auth;
using Studio.Host.Config;
using Studio.Host.Runtime;

namespace Studio.Host.Endpoints;

public static class StudioEndpoints
{
    public static void MapStudioApi(this WebApplication app)
    {
        var api = app.MapGroup("/api/v1");

        api.MapPost("/auth/login", (LoginRequest? body, TokenService tokens) =>
        {
            var username = body?.Username?.Trim();
            if (body is null || !tokens.TryLogin(username, body.Password, out var token, out var role, out var expires))
            {
                return ApiResults.Error(StatusCodes.Status401Unauthorized, "invalid_credentials", "用户名或密码错误");
            }

            return ApiResults.Ok(new LoginResponse
            {
                Token = token,
                Username = username ?? "",
                Role = role,
                ExpiresAt = expires,
                MustChangePassword = tokens.MustChangePassword(username)
            });
        });

        api.MapGet("/auth/posture", (AccountStore accounts) => ApiResults.Ok(accounts.Posture()));

        api.MapPost("/auth/password", (ChangePasswordRequest? body, HttpContext http, AccountStore accounts) =>
        {
            var username = http.Items["studio.user"] as string ?? "";
            if (!accounts.TryChangePassword(username, body?.CurrentPassword, body?.NewPassword, out var error))
            {
                return ApiResults.Error(StatusCodes.Status400BadRequest, "invalid_password", error);
            }

            return ApiResults.Ok(new ChangePasswordResult());
        });

        api.MapGet("/auth/me", (HttpContext http, AccountStore accounts) => ApiResults.Ok(CurrentUser(http, accounts)));

        api.MapGet("/config", (ConfigStore store) => ApiResults.Ok(store.GetView()));
        api.MapGet("/config/diff", (ConfigStore store) => ApiResults.Ok(store.Diff()));
        api.MapGet("/config/devices", (ConfigStore store) => ApiResults.Ok(store.ListDevices()));
        api.MapGet("/config/devices/{id}", (string id, ConfigStore store) => ApiResults.Ok(store.GetDevice(id)));
        api.MapPut("/config/devices/{id}", (string id, DeviceDocument body, ConfigStore store) =>
            ApiResults.Ok(store.UpsertDevice(id, body))).RequireWriter();
        api.MapDelete("/config/devices/{id}", (string id, ConfigStore store) =>
        {
            store.DeleteDevice(id);
            return Results.NoContent();
        }).RequireWriter();

        api.MapGet("/config/point-templates", (ConfigStore store) => ApiResults.Ok(store.ListPointTemplates()));
        api.MapGet("/config/point-templates/{id}", (string id, ConfigStore store) => ApiResults.Ok(store.GetPointTemplate(id)));
        api.MapPut("/config/point-templates/{id}", (string id, PointTemplateDocument body, ConfigStore store) =>
            ApiResults.Ok(store.UpsertPointTemplate(id, body))).RequireWriter();
        api.MapDelete("/config/point-templates/{id}", (string id, ConfigStore store) =>
        {
            store.DeletePointTemplate(id);
            return Results.NoContent();
        }).RequireWriter();

        api.MapGet("/catalog/brands", (GatewayPersistence database) => ApiResults.Ok(database.ReadCatalog()));
        api.MapGet("/catalog/items", (GatewayPersistence database) => ApiResults.Ok(database.ReadCatalog().Items));
        api.MapGet("/catalog/support", (string? brandId) => ApiResults.Ok(DriverCatalog.Describe(brandId)));
        api.MapGet("/groups", (GatewayPersistence database) => ApiResults.Ok(database.ListGroups()));
        api.MapPut("/groups/{id}", (string id, GroupWrite body, GatewayPersistence database) =>
        {
            if (!ConfigValidator.IsSafeId(id))
            {
                return ApiResults.Error(StatusCodes.Status400BadRequest, "id_invalid", "分组 Id 不合法");
            }

            return ApiResults.Ok(database.UpsertGroup(id, body.Name ?? id, body.Workshop ?? "", body.Line ?? ""));
        }).RequireWriter();
        api.MapDelete("/groups/{id}", (string id, GatewayPersistence database) =>
        {
            database.DeleteGroup(id);
            return Results.NoContent();
        }).RequireWriter();

        api.MapGet("/devices/{id}/latest", (string id, ConfigStore store) =>
        {
            store.GetDevice(id);
            return ApiResults.Ok(new { deviceId = id, samples = store.Database.Latest(id) });
        });
        api.MapGet("/samples/history", (string? deviceId, string? items, string? from, string? to, long? bucketMs, ConfigStore store) =>
        {
            if (string.IsNullOrWhiteSpace(deviceId))
            {
                return ApiResults.Error(StatusCodes.Status400BadRequest, "device_required", "请指定 deviceId");
            }

            store.GetDevice(deviceId);
            var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            var start = ParseTime(from, now - (long)TimeSpan.FromHours(1).TotalMilliseconds);
            var end = ParseTime(to, now);
            var names = string.IsNullOrWhiteSpace(items)
                ? Array.Empty<string>()
                : items.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            return ApiResults.Ok(new
            {
                deviceId,
                from = start,
                to = end,
                bucketMs = bucketMs ?? 0,
                samples = store.Database.History(deviceId, names, start, end, bucketMs ?? 0)
            });
        });
        api.MapGet("/alarms", (string? deviceId, int? limit, GatewayPersistence database) =>
            ApiResults.Ok(new { alarms = database.ListAlarms(deviceId, limit ?? 100) }));

        api.MapGet("/config/export", (string? format, string? slot, ConfigStore store) =>
        {
            var published = string.Equals(slot, "published", StringComparison.OrdinalIgnoreCase);
            if (string.Equals(format, "yaml", StringComparison.OrdinalIgnoreCase))
            {
                return Results.Text(store.ExportYaml(published), "application/yaml; charset=utf-8");
            }

            return Results.Text(store.ExportJson(published), "application/json; charset=utf-8");
        });
        api.MapPost("/config/import", async (HttpRequest request, string? format, ConfigStore store) =>
        {
            using var reader = new StreamReader(request.Body);
            var body = await reader.ReadToEndAsync();
            var yaml = string.Equals(format, "yaml", StringComparison.OrdinalIgnoreCase)
                || body.TrimStart().StartsWith("apiVersion:", StringComparison.Ordinal)
                || body.TrimStart().StartsWith("gateway:", StringComparison.OrdinalIgnoreCase);
            if (yaml)
            {
                store.ImportYaml(body);
            }
            else
            {
                store.ImportJson(body);
            }

            return ApiResults.Ok(store.GetView());
        }).RequireWriter();

        api.MapGet("/catalog/points", (string? adapter) =>
        {
            var kind = (adapter ?? "").Trim().ToLowerInvariant();
            if (!FanucPointCatalog.IsFanuc(kind))
            {
                return ApiResults.Error(
                    StatusCodes.Status404NotFound,
                    "catalog_unsupported",
                    "M1 只提供发那科适配器点位目录（fanuc.fake、fanuc.focas）。其他品牌还没有目录，不能按通用地址编辑。");
            }

            return ApiResults.Ok(FanucPointCatalog.Describe(kind));
        });

        api.MapGet("/config/points/{deviceId}", (string deviceId, ConfigStore store) => ApiResults.Ok(store.GetPoints(deviceId)));
        api.MapPut("/config/points/{deviceId}", (string deviceId, PointSetDocument body, ConfigStore store) =>
            ApiResults.Ok(store.UpsertPoints(deviceId, body))).RequireWriter();

        api.MapGet("/config/sinks/mqtt", (ConfigStore store) => ApiResults.Ok(store.GetMqtt()));
        api.MapPut("/config/sinks/mqtt", (MqttSinkDocument body, ConfigStore store) =>
            ApiResults.Ok(store.UpsertMqtt(body))).RequireWriter();

        api.MapGet("/config/gateway", (ConfigStore store) => ApiResults.Ok(store.GetGateway()));
        api.MapPut("/config/gateway", (GatewayDocument body, ConfigStore store) =>
            ApiResults.Ok(store.UpsertGateway(body))).RequireWriter();

        api.MapPost("/config/validate", (ConfigStore store) => ApiResults.Ok(store.Validate())).RequireWriter();
        api.MapPost("/config/publish", async (PublishRequest? body, ConfigStore store, GatewayReloadClient reload, CancellationToken cancellationToken) =>
        {
            var outcome = store.Publish(body?.Note);
            if (!outcome.Published)
            {
                return ApiResults.Error(StatusCodes.Status400BadRequest, "validation_failed", "草稿未通过校验，未发布", new { outcome.Issues });
            }

            await reload.NotifyAsync(cancellationToken);
            return ApiResults.Ok(new PublishResult
            {
                Revision = outcome.Revision,
                PublishedAt = outcome.PublishedAt,
                Unchanged = outcome.Unchanged,
                Issues = outcome.Issues
            });
        }).RequireWriter();

        api.MapGet("/config/revisions", (int? limit, ConfigStore store) =>
            ApiResults.Ok(new RevisionList { Revisions = store.ListRevisions(limit ?? 20).ToList() }));

        api.MapPost("/config/rollback", async (RollbackRequest? body, ConfigStore store, GatewayReloadClient reload, CancellationToken cancellationToken) =>
        {
            var outcome = store.Rollback(body?.Revision ?? "");
            await reload.NotifyAsync(cancellationToken);
            return ApiResults.Ok(new RollbackResult
            {
                Revision = outcome.Revision,
                RolledBackAt = outcome.RolledBackAt
            });
        }).RequireWriter();

        api.MapPost("/devices/test", async (DeviceDocument? body, RuntimeQueries runtime, CancellationToken cancellationToken) =>
            ApiResults.Ok(await runtime.TestUnsavedAsync(body, cancellationToken))).RequireWriter();
        api.MapPost("/devices/{id}/test", async (string id, RuntimeQueries runtime, CancellationToken cancellationToken) =>
            ApiResults.Ok(await runtime.TestDeviceAsync(id, cancellationToken))).RequireWriter();
        api.MapPost("/devices/{id}/collection", async (string id, CollectionRequest? body, ConfigStore store, GatewayReloadClient reload, CancellationToken cancellationToken) =>
        {
            var result = store.SetCollectionEnabled(id, body?.Enabled ?? false);
            if (result.Reloaded)
            {
                await reload.NotifyAsync(cancellationToken);
            }

            return ApiResults.Ok(result);
        }).RequireWriter();

        api.MapGet("/runtime/status", async (RuntimeQueries runtime, CancellationToken cancellationToken) =>
            ApiResults.Ok(await runtime.StatusAsync(cancellationToken)));
        api.MapGet("/runtime/observations", async (string? deviceId, int? limit, RuntimeQueries runtime, CancellationToken cancellationToken) =>
            ApiResults.Ok(await runtime.ObservationsAsync(deviceId, limit ?? 50, cancellationToken)));
        api.MapGet("/runtime/logs/tail", async (int? lines, RuntimeQueries runtime, CancellationToken cancellationToken) =>
            ApiResults.Ok(await runtime.LogsAsync(lines ?? 200, cancellationToken)));

        api.MapGet("/license", () => ApiResults.Ok(License()));
        api.MapGet("/settings", (HttpContext http, ConfigStore store, AccountStore accounts) =>
            ApiResults.Ok(BuildSettings(http, store, accounts)));
        api.MapPut("/settings", (SettingsUpdate body, ConfigStore store) =>
        {
            var gateway = store.GetGateway();
            gateway.Metadata.SiteId = body.SiteId ?? "";
            gateway.Metadata.Name = body.Name ?? "";
            gateway.Spec.LogLevel = body.LogLevel ?? "";
            gateway.Spec.Features.ProgramWrite = body.ProgramWrite;
            gateway.Spec.Acquisition.DefaultIntervalMs = body.DefaultIntervalMs;
            gateway.Spec.Acquisition.ChangeOnly = body.ChangeOnly;
            store.UpsertGateway(gateway);
            return ApiResults.Ok(gateway);
        }).RequireWriter();
    }

    private static SettingsView BuildSettings(HttpContext http, ConfigStore store, AccountStore accounts)
    {
        var gateway = store.GetGateway();
        var user = CurrentUser(http, accounts);
        var posture = accounts.Posture();
        var users = user.Role == "admin" ? accounts.ListUsers().ToList() : [];
        return new SettingsView
        {
            SiteId = gateway.Metadata.SiteId,
            Name = gateway.Metadata.Name,
            LogLevel = gateway.Spec.LogLevel,
            ProgramWrite = gateway.Spec.Features.ProgramWrite,
            DefaultIntervalMs = gateway.Spec.Acquisition.DefaultIntervalMs,
            ChangeOnly = gateway.Spec.Acquisition.ChangeOnly,
            DataDirectory = store.DataDirectory,
            License = License(),
            CurrentUser = user,
            Users = users,
            AccountMode = posture.Mode,
            AccountMessage = posture.Message
        };
    }

    private static LicenseStatus License() => new()
    {
        Enforced = false,
        Status = "stub",
        Edition = "m1-dev",
        Message = "M1 许可证闸门为桩，当前不拦截管理 API。没有许可证时 Runtime 与 Studio 都可以运行。"
    };

    private static UserInfo CurrentUser(HttpContext http, AccountStore accounts)
    {
        var username = http.Items["studio.user"] as string ?? "";
        return new UserInfo
        {
            Username = username,
            Role = http.Items["studio.role"] as string ?? "",
            MustChangePassword = accounts.MustChangePassword(username)
        };
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

public sealed class GroupWrite
{
    public string? Name { get; set; }

    public string? Workshop { get; set; }

    public string? Line { get; set; }
}

public static class ApiResults
{
    public static IResult Ok(object value) => Results.Json(value, StudioJson.Options);

    public static IResult Error(int status, string code, string message, object? details = null) =>
        Results.Json(new ApiError { Code = code, Message = message, Details = details }, StudioJson.Options, statusCode: status);
}
