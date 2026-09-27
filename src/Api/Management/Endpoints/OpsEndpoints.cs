using System.Globalization;
using IotDaq.Persistence;
using Microsoft.AspNetCore.Routing;
using Studio.Host.Auth;
using Studio.Host.Config;
using Studio.Host.Runtime;

namespace Studio.Host.Endpoints;

public static class OpsEndpoints
{
    public const string DismissedKey = "onboarding.dismissed";
    public const string PasswordAckKey = "onboarding.passwordAck";
    public const string ConnectionTestedKey = "onboarding.connectionTested";

    public static void Map(RouteGroupBuilder api)
    {
        api.MapGet("/onboarding", OnboardingAsync);
        api.MapPost("/onboarding/dismiss", (HttpContext http, ConfigStore store) =>
        {
            store.Database.SetSetting(DismissedKey, "1");
            ConfigAudit.Write(http, store.Database, "onboarding.dismiss", "setup", "关闭上手引导");
            return ApiResults.Ok(new { dismissed = true });
        });
        api.MapPost("/onboarding/password-ack", (HttpContext http, AccountStore accounts, ConfigStore store) =>
        {
            var username = http.Items["studio.user"] as string ?? "";
            if (accounts.MustChangePassword(username))
            {
                return ApiResults.Error(StatusCodes.Status400BadRequest, "password_change_required", "必须先修改密码，不能跳过。");
            }

            store.Database.SetSetting(PasswordAckKey, "1");
            return ApiResults.Ok(new { acknowledged = true });
        });
        api.MapGet("/audit", (int? limit, GatewayPersistence database) =>
            ApiResults.Ok(new AuditList { Events = database.ListAudit(limit ?? 100).ToList() }));
        api.MapGet("/ops/status", (ConfigStore store) => ApiResults.Ok(new OpsStatus
        {
            Database = store.Database.Provider,
            FileBackup = store.Database.SupportsFileBackup,
            SchemaVersion = store.Database.CurrentSchemaVersion
        }));
        api.MapGet("/ops/backup", (HttpContext http, ConfigStore store) =>
        {
            try
            {
                var memory = new MemoryStream();
                store.Database.WriteSqliteBackup(memory);
                ConfigAudit.Write(http, store.Database, "ops.backup", "gateway.db", "下载 SQLite 备份");
                memory.Position = 0;
                var name = "iot-daq-gateway-" + DateTimeOffset.UtcNow.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture) + ".db";
                return Results.File(memory, "application/octet-stream", name);
            }
            catch (InvalidOperationException ex)
            {
                return ApiResults.Error(StatusCodes.Status400BadRequest, "backup_unavailable", ex.Message);
            }
        }).RequireAdmin();
        api.MapPost("/ops/restore", async (HttpContext http, ConfigStore store, GatewayReloadClient reload, CancellationToken cancellationToken) =>
        {
            try
            {
                var buffer = new MemoryStream();
                await http.Request.Body.CopyToAsync(buffer, cancellationToken);
                buffer.Position = 0;
                store.Database.RestoreSqlite(buffer);
                store.Database.SyncUsers(Path.Combine(store.DataDirectory, "auth", "accounts.json"));
                ConfigAudit.Write(http, store.Database, "ops.restore", "gateway.db", "已从上传的 SQLite 备份恢复");
                await reload.NotifyAsync(cancellationToken);
                return ApiResults.Ok(new { restored = true });
            }
            catch (InvalidOperationException ex)
            {
                return ApiResults.Error(StatusCodes.Status400BadRequest, "restore_rejected", ex.Message);
            }
        }).RequireAdmin();
    }

    private static async Task<IResult> OnboardingAsync(
        HttpContext http,
        ConfigStore store,
        AccountStore accounts,
        RuntimeQueries runtime,
        CancellationToken cancellationToken)
    {
        var username = http.Items["studio.user"] as string ?? "";
        var mustChange = accounts.MustChangePassword(username);
        var demoPassword = accounts.UsesDemoPassword(username);
        var passwordAck = store.Database.GetSetting(PasswordAckKey) == "1";
        var passwordDone = !mustChange && (!demoPassword || passwordAck);
        var hasDevice = store.ListDevices().Count > 0;
        var tested = store.Database.GetSetting(ConnectionTestedKey) == "1";
        var view = store.GetView();
        var published = !string.IsNullOrWhiteSpace(view.ActiveRevision) && !view.Dirty;
        var status = await runtime.StatusAsync(cancellationToken);
        var live = store.Database.HasSamples()
            || status.Devices.Any(device => device.Status is "online" or "degraded");
        var steps = new List<OnboardingStepView>
        {
            new()
            {
                Id = "password",
                Title = "确认管理员密码",
                Done = passwordDone,
                Href = "/account/password",
                Detail = mustChange
                    ? "现场包必须先把一次性口令改成至少 8 位的新密码。"
                    : demoPassword
                        ? "当前仍是演示口令。可以现在修改，或在本机演示里稍后再改。"
                        : "管理员口令已不是演示口令。"
            },
            new()
            {
                Id = "device",
                Title = "添加第一台设备",
                Done = hasDevice,
                Href = "/devices",
                Detail = hasDevice
                    ? "草稿里已经有设备。可以按品牌再加一台模拟器或真实机床。"
                    : "从品牌目录选择适配器，保存到草稿。"
            },
            new()
            {
                Id = "connect",
                Title = "测试连接",
                Done = tested,
                Href = "/devices",
                Detail = "在设备页对草稿或已保存的设备做一次连接测试。模拟器会直接成功。"
            },
            new()
            {
                Id = "publish",
                Title = "发布配置",
                Done = published,
                Href = "/publish",
                Detail = published
                    ? "草稿和已发布配置一致，采集读的是这一版。"
                    : "校验通过后发布。采集只在发布后加载新设备。"
            },
            new()
            {
                Id = "live",
                Title = "查看实时数据",
                Done = live,
                Href = "/",
                Detail = "回到总览。运行、待机、报警和离线会铺在车间和产线上。"
            }
        };
        return ApiResults.Ok(new OnboardingView
        {
            Dismissed = store.Database.GetSetting(DismissedKey) == "1",
            Complete = steps.All(step => step.Done),
            MustChangePassword = mustChange,
            Steps = steps
        });
    }

    private static RouteHandlerBuilder RequireAdmin(this RouteHandlerBuilder builder) =>
        builder.AddEndpointFilter(async (context, next) =>
        {
            var role = context.HttpContext.Items["studio.role"] as string;
            if (role is not "admin")
            {
                return ApiResults.Error(StatusCodes.Status403Forbidden, "forbidden", "只有管理员可以备份或恢复数据库");
            }

            return await next(context);
        });
}

public sealed class OnboardingView
{
    public bool Dismissed { get; set; }

    public bool Complete { get; set; }

    public bool MustChangePassword { get; set; }

    public List<OnboardingStepView> Steps { get; set; } = [];
}

public sealed class OnboardingStepView
{
    public string Id { get; set; } = "";

    public string Title { get; set; } = "";

    public string Detail { get; set; } = "";

    public bool Done { get; set; }

    public string Href { get; set; } = "";
}

public sealed class AuditList
{
    public List<AuditEventRow> Events { get; set; } = [];
}

public sealed class OpsStatus
{
    public string Database { get; set; } = "";

    public bool FileBackup { get; set; }

    public int SchemaVersion { get; set; }
}
