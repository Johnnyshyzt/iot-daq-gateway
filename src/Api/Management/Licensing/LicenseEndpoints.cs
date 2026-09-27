using IotDaq.Licensing;
using Microsoft.Extensions.Hosting;
using Studio.Host.Config;
using Studio.Host.Endpoints;
using Studio.Host.Runtime;

namespace Studio.Host.Licensing;

public static class LicenseEndpoints
{
    public static void Map(RouteGroupBuilder api)
    {
        api.MapGet("/license", (LicenseService licensing, ConfigStore store) =>
            ApiResults.Ok(licensing.Describe(store)));

        api.MapPost("/license", async (HttpContext http, LicenseService licensing, ConfigStore store, CancellationToken cancellationToken) =>
        {
            string document;
            if (http.Request.HasFormContentType)
            {
                var form = await http.Request.ReadFormAsync(cancellationToken);
                var file = form.Files.FirstOrDefault();
                if (file is null)
                {
                    return ApiResults.Error(StatusCodes.Status400BadRequest, "license_missing", "请上传许可证文件。");
                }

                using var reader = new StreamReader(file.OpenReadStream());
                document = await reader.ReadToEndAsync(cancellationToken);
            }
            else
            {
                using var reader = new StreamReader(http.Request.Body);
                document = await reader.ReadToEndAsync(cancellationToken);
            }

            if (string.IsNullOrWhiteSpace(document))
            {
                return ApiResults.Error(StatusCodes.Status400BadRequest, "license_missing", "许可证内容为空。");
            }

            var user = http.Items["studio.user"] as string ?? "";
            var (ok, code, message) = licensing.Import(document, user);
            if (!ok)
            {
                ConfigAudit.Write(http, store.Database, "license.reject", "license", message);
                return ApiResults.Error(StatusCodes.Status400BadRequest, code, message);
            }

            ConfigAudit.Write(http, store.Database, "license.import", "license", message);
            return ApiResults.Ok(licensing.Describe(store));
        }).RequireAdmin();

        api.MapDelete("/license", (HttpContext http, LicenseService licensing, ConfigStore store) =>
        {
            licensing.Remove();
            ConfigAudit.Write(http, store.Database, "license.remove", "license", "已移除许可证，恢复社区版。采集不停止。");
            return ApiResults.Ok(licensing.Describe(store));
        }).RequireAdmin();

        api.MapPost("/license/security/ack", (HttpContext http, LicenseService licensing, ConfigStore store) =>
        {
            var result = licensing.AcknowledgeTamper();
            ConfigAudit.Write(http, store.Database, "security.ack", "license", result.Code.Length == 0 ? "已清除授权状态异常标记" : result.Message);
            return ApiResults.Ok(licensing.Describe(store));
        }).RequireAdmin();
    }

    private static RouteHandlerBuilder RequireAdmin(this RouteHandlerBuilder builder) =>
        builder.AddEndpointFilter(async (context, next) =>
        {
            var role = context.HttpContext.Items["studio.role"] as string;
            if (role is not "admin")
            {
                return ApiResults.Error(StatusCodes.Status403Forbidden, "forbidden", "只有管理员可以导入或移除许可证。");
            }

            return await next(context);
        });
}

public static class DeliveryEndpoints
{
    public static readonly string[] Limitations =
    [
        "正在运行的进程不能替换自己的程序文件。暂存之后要重启 Windows 服务或 systemd，由升级脚本在进程退出后覆盖安装目录（不含 data）。",
        "Docker 里不要指望页面改掉镜像。请更换镜像标签并重建容器；数据库放在卷里。页面仍可校验包并备份数据库。",
        "签名使用内嵌公钥。仓库里没有私钥。GitHub Release 只有在配置了 RELEASE_SIGNING_KEY 时才会带上 SHA256SUMS.sig，否则页面会拒绝安装。",
        "失败时脚本从 data/upgrade/previous 恢复程序，并从升级前的数据库备份恢复数据。磁盘满时脚本会停在删除旧文件之前。",
        "本包不包含厂商 SDK。升级不会替你放上 Fwlib64.dll。"
    ];

    public static void Map(RouteGroupBuilder api)
    {
        api.MapGet("/ops/upgrade", (ConfigStore store) =>
        {
            var state = UpgradeCoordinator.Read(store.DataDirectory);
            return ApiResults.Ok(new
            {
                state,
                limitations = Limitations,
                packagePresent = File.Exists(UpgradePaths.PackagePath(store.DataDirectory))
            });
        });

        api.MapPost("/ops/upgrade", async (HttpContext http, ConfigStore store, LicenseService licensing, CancellationToken cancellationToken) =>
        {
            var buffer = new MemoryStream();
            await http.Request.Body.CopyToAsync(buffer, cancellationToken);
            if (buffer.Length == 0)
            {
                return ApiResults.Error(StatusCodes.Status400BadRequest, "upgrade_missing", "请上传 zip 升级包。");
            }

            if (buffer.Length > UpgradePackage.MaxBytes)
            {
                return ApiResults.Error(StatusCodes.Status400BadRequest, "upgrade_too_large", "升级包超过 400 MB。");
            }

            buffer.Position = 0;
            try
            {
                var inspection = UpgradeCoordinator.Stage(buffer, store.DataDirectory, store.Database, licensing.Options.PublicKeySpki);
                if (!inspection.Ok)
                {
                    ConfigAudit.Write(http, store.Database, "upgrade.reject", inspection.Version, inspection.Message);
                    return ApiResults.Error(StatusCodes.Status400BadRequest, inspection.Code, inspection.Message);
                }

                ConfigAudit.Write(http, store.Database, "upgrade.stage", inspection.Version, "已校验签名和校验和并暂存，数据库已备份");
                return ApiResults.Ok(new
                {
                    staged = true,
                    version = inspection.Version,
                    message = "签名和校验和已通过，包已暂存，数据库已备份。重启后才会替换程序。采集现在不会停。",
                    limitations = Limitations
                });
            }
            catch (InvalidOperationException ex)
            {
                return ApiResults.Error(StatusCodes.Status400BadRequest, "upgrade_backup", ex.Message);
            }
        }).RequireAdmin();

        api.MapPost("/ops/upgrade/apply", (HttpContext http, ConfigStore store, IHostApplicationLifetime lifetime, IConfiguration configuration, IHostEnvironment environment) =>
        {
            var state = UpgradeCoordinator.Read(store.DataDirectory);
            if (state is null || !File.Exists(UpgradePaths.PackagePath(store.DataDirectory)))
            {
                return ApiResults.Error(StatusCodes.Status400BadRequest, "upgrade_none", "还没有暂存的升级包。");
            }

            state.Phase = "apply-requested";
            state.Message = "已请求在下次启动时应用。请重启服务。Docker 需要更换镜像。";
            UpgradeCoordinator.Write(store.DataDirectory, state);
            var spawned = UpgradeLauncher.TrySpawn(store.DataDirectory, environment);
            var restart = string.Equals(configuration["Upgrade:AutoRestart"], "true", StringComparison.OrdinalIgnoreCase);
            ConfigAudit.Write(http, store.Database, "upgrade.apply", state.Version, spawned ? "已请求升级并拉起脚本" : "已请求升级，等待重启");
            if (restart && spawned)
            {
                lifetime.StopApplication();
            }

            return ApiResults.Ok(new
            {
                phase = state.Phase,
                spawned,
                restarting = restart && spawned,
                message = spawned
                    ? "升级脚本已在后台等待本进程退出，然后替换程序并尝试拉起服务。"
                    : "已记下「下次启动时应用」。请重启 Windows 服务或 systemd。Docker 请更换镜像后重建容器，页面不会改容器里的程序文件。",
                limitations = Limitations
            });
        }).RequireAdmin();

        api.MapPost("/ops/upgrade/rollback", (HttpContext http, ConfigStore store, GatewayReloadClient reload) =>
        {
            try
            {
                if (!UpgradeCoordinator.RestoreBackup(store.DataDirectory, store.Database))
                {
                    return ApiResults.Error(StatusCodes.Status400BadRequest, "upgrade_no_backup", "没有找到升级前的数据库备份。");
                }

                store.Database.SyncUsers(Path.Combine(store.DataDirectory, "auth", "accounts.json"));
                ConfigAudit.Write(http, store.Database, "upgrade.rollback", "database", "已从升级前备份恢复数据库");
                return ApiResults.Ok(new
                {
                    restored = true,
                    message = "数据库已恢复到升级前的备份。若程序文件已经换过，请用 data/upgrade/previous 里的旧文件覆盖安装目录后再启动。"
                });
            }
            catch (InvalidOperationException ex)
            {
                return ApiResults.Error(StatusCodes.Status400BadRequest, "upgrade_rollback", ex.Message);
            }
        }).RequireAdmin();

        api.MapPost("/ops/demo", async (HttpContext http, ConfigStore store, GatewayReloadClient reload, CancellationToken cancellationToken) =>
        {
            try
            {
                var result = DemoMode.Seed(store, publish: true);
                Studio.Host.Shop.ShopDemo.Seed(store.Database);
                ConfigAudit.Write(http, store.Database, "demo.seed", "devices", result.Message);
                if (result.Published)
                {
                    await reload.NotifyAsync(cancellationToken);
                }

                return ApiResults.Ok(result);
            }
            catch (ConfigStoreException ex)
            {
                return ApiResults.Error(ex.StatusCode, ex.Code, ex.Message);
            }
        }).RequireAdmin();
    }

    private static RouteHandlerBuilder RequireAdmin(this RouteHandlerBuilder builder) =>
        builder.AddEndpointFilter(async (context, next) =>
        {
            var role = context.HttpContext.Items["studio.role"] as string;
            if (role is not "admin")
            {
                return ApiResults.Error(StatusCodes.Status403Forbidden, "forbidden", "只有管理员可以执行此操作。");
            }

            return await next(context);
        });
}

public static class UpgradeLauncher
{
    public static bool TrySpawn(string dataDirectory, IHostEnvironment environment)
    {
        if (environment.IsDevelopment())
        {
            return false;
        }

        var install = AppContext.BaseDirectory;
        string? script = OperatingSystem.IsWindows()
            ? FirstExisting(Path.Combine(install, "apply-upgrade.ps1"))
            : FirstExisting(Path.Combine(install, "apply-upgrade.sh"), Path.Combine(environment.ContentRootPath, "packaging", "linux", "apply-upgrade.sh"));
        if (script is null)
        {
            return false;
        }

        var start = new System.Diagnostics.ProcessStartInfo
        {
            UseShellExecute = false,
            CreateNoWindow = true
        };
        if (OperatingSystem.IsWindows())
        {
            start.FileName = "powershell";
            start.ArgumentList.Add("-NoProfile");
            start.ArgumentList.Add("-ExecutionPolicy");
            start.ArgumentList.Add("Bypass");
            start.ArgumentList.Add("-File");
            start.ArgumentList.Add(script);
        }
        else
        {
            start.FileName = "/bin/bash";
            start.ArgumentList.Add(script);
        }

        start.ArgumentList.Add("--wait-pid");
        start.ArgumentList.Add(Environment.ProcessId.ToString(System.Globalization.CultureInfo.InvariantCulture));
        start.ArgumentList.Add("--install");
        start.ArgumentList.Add(install);
        start.ArgumentList.Add("--data");
        start.ArgumentList.Add(dataDirectory);
        try
        {
            return System.Diagnostics.Process.Start(start) is not null;
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            return false;
        }
    }

    private static string? FirstExisting(params string[] paths) =>
        paths.FirstOrDefault(File.Exists);
}
