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
                ExpiresAt = expires
            });
        });

        api.MapGet("/auth/me", (HttpContext http) => ApiResults.Ok(CurrentUser(http)));

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
        api.MapPost("/config/publish", (PublishRequest? body, ConfigStore store) =>
        {
            var outcome = store.Publish(body?.Note);
            if (!outcome.Published)
            {
                return ApiResults.Error(StatusCodes.Status400BadRequest, "validation_failed", "草稿未通过校验，未发布", new { outcome.Issues });
            }

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

        api.MapPost("/config/rollback", (RollbackRequest? body, ConfigStore store) =>
        {
            var outcome = store.Rollback(body?.Revision ?? "");
            return ApiResults.Ok(new RollbackResult
            {
                Revision = outcome.Revision,
                RolledBackAt = outcome.RolledBackAt
            });
        }).RequireWriter();

        api.MapPost("/devices/{id}/test", async (string id, RuntimeQueries runtime, CancellationToken cancellationToken) =>
            ApiResults.Ok(await runtime.TestDeviceAsync(id, cancellationToken))).RequireWriter();

        api.MapGet("/runtime/status", (RuntimeQueries runtime) => ApiResults.Ok(runtime.Status()));
        api.MapGet("/runtime/observations", (string? deviceId, int? limit, RuntimeQueries runtime) =>
            ApiResults.Ok(runtime.Observations(deviceId, limit ?? 50)));
        api.MapGet("/runtime/logs/tail", (int? lines, RuntimeQueries runtime) =>
            ApiResults.Ok(runtime.Logs(lines ?? 200)));

        api.MapGet("/license", () => ApiResults.Ok(License()));
        api.MapGet("/settings", (HttpContext http, ConfigStore store, TokenService tokens) =>
            ApiResults.Ok(BuildSettings(http, store, tokens)));
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

    private static SettingsView BuildSettings(HttpContext http, ConfigStore store, TokenService tokens)
    {
        var gateway = store.GetGateway();
        var user = CurrentUser(http);
        var users = user.Role == "admin"
            ? tokens.Users.Select(item => new UserInfo { Username = item.Username, Role = item.Role }).ToList()
            : [];
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
            Users = users
        };
    }

    private static LicenseStatus License() => new()
    {
        Enforced = false,
        Status = "stub",
        Edition = "m1-dev",
        Message = "M1 许可证闸门为桩：没有许可证时开源 Runtime 仍可读取 YAML；Studio 商业校验尚未启用。"
    };

    private static UserInfo CurrentUser(HttpContext http) => new()
    {
        Username = http.Items["studio.user"] as string ?? "",
        Role = http.Items["studio.role"] as string ?? ""
    };

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

public static class ApiResults
{
    public static IResult Ok(object value) => Results.Json(value, StudioJson.Options);

    public static IResult Error(int status, string code, string message, object? details = null) =>
        Results.Json(new ApiError { Code = code, Message = message, Details = details }, StudioJson.Options, statusCode: status);
}
