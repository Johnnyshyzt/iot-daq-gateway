using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using IotDaq.Licensing;
using IotDaq.Persistence;

namespace Studio.Host.Licensing;

public sealed class UpgradeState
{
    public string Phase { get; set; } = "staged";

    public string Version { get; set; } = "";

    public long StagedUnixMs { get; set; }

    public string BackupPath { get; set; } = "";

    public string Message { get; set; } = "";
}

public static class UpgradePaths
{
    public static string Root(string dataDirectory) => Path.Combine(dataDirectory, "upgrade");

    public static string StatePath(string dataDirectory) => Path.Combine(Root(dataDirectory), "state.json");

    public static string PackagePath(string dataDirectory) => Path.Combine(Root(dataDirectory), "package.zip");
}

public static class UpgradeCoordinator
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    public static UpgradeState? Read(string dataDirectory)
    {
        var path = UpgradePaths.StatePath(dataDirectory);
        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<UpgradeState>(File.ReadAllText(path), Json);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public static void Write(string dataDirectory, UpgradeState state)
    {
        var root = UpgradePaths.Root(dataDirectory);
        Directory.CreateDirectory(root);
        File.WriteAllText(UpgradePaths.StatePath(dataDirectory), JsonSerializer.Serialize(state, Json));
    }

    public static UpgradeInspection Stage(Stream zip, string dataDirectory, GatewayPersistence database, string? publicKeySpki)
    {
        using var key = LicenseCrypto.CreatePublic(publicKeySpki);
        var inspection = UpgradePackage.Inspect(zip, key);
        if (!inspection.Ok)
        {
            return inspection;
        }

        if (zip.CanSeek)
        {
            zip.Position = 0;
        }

        var backup = BackupDatabase(dataDirectory, database);
        var root = UpgradePaths.Root(dataDirectory);
        Directory.CreateDirectory(root);
        var package = UpgradePaths.PackagePath(dataDirectory);
        using (var file = File.Create(package))
        {
            zip.CopyTo(file);
        }

        Write(dataDirectory, new UpgradeState
        {
            Phase = "staged",
            Version = inspection.Version,
            StagedUnixMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            BackupPath = backup,
            Message = "已校验并暂存。重启服务后由升级脚本替换程序。采集在重启前不会中断。"
        });
        return inspection;
    }

    public static string BackupDatabase(string dataDirectory, GatewayPersistence database)
    {
        var directory = Path.Combine(UpgradePaths.Root(dataDirectory), "backup");
        Directory.CreateDirectory(directory);
        var stamp = DateTimeOffset.UtcNow.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
        if (database.SupportsFileBackup)
        {
            var path = Path.Combine(directory, "pre-upgrade-" + stamp + ".db");
            using var stream = File.Create(path);
            database.WriteSqliteBackup(stream);
            return path;
        }

        if (!string.Equals(database.Provider, "Postgres", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("当前数据库不能在升级前自动备份。");
        }

        var dump = PostgresTools.Dump(database.PostgresConnectionString, directory, "pre-upgrade-" + stamp + ".dump");
        return dump;
    }

    public static void NoteBoot(string dataDirectory, string version, ILogger logger)
    {
        var state = Read(dataDirectory);
        if (state is null)
        {
            return;
        }

        if (state.Phase is "apply-requested" or "applying"
            && version.StartsWith(state.Version, StringComparison.Ordinal)
            && state.Version.Length > 0)
        {
            state.Phase = "applied";
            state.Message = "当前进程版本与暂存包一致，记为已应用。";
            Write(dataDirectory, state);
            logger.LogInformation("Upgrade package {Version} marked applied", state.Version);
        }
    }

    public static bool RestoreBackup(string dataDirectory, GatewayPersistence database)
    {
        var state = Read(dataDirectory);
        if (state is null || string.IsNullOrWhiteSpace(state.BackupPath) || !File.Exists(state.BackupPath))
        {
            return false;
        }

        if (database.SupportsFileBackup)
        {
            using var stream = File.OpenRead(state.BackupPath);
            database.RestoreSqlite(stream);
        }
        else
        {
            PostgresTools.Restore(database.PostgresConnectionString, state.BackupPath);
        }

        state.Phase = "rolled-back";
        state.Message = "已从升级前的数据库备份恢复。程序文件要由升级脚本从 upgrade/previous 拷回。";
        Write(dataDirectory, state);
        return true;
    }
}

public static class PostgresTools
{
    public static string? Find(string name)
    {
        var path = Environment.GetEnvironmentVariable("PATH") ?? "";
        foreach (var directory in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            var candidate = Path.Combine(directory, name);
            if (File.Exists(candidate))
            {
                return candidate;
            }

            if (OperatingSystem.IsWindows() && File.Exists(candidate + ".exe"))
            {
                return candidate + ".exe";
            }
        }

        return null;
    }

    public static string Dump(string? connectionString, string directory, string fileName)
    {
        var tool = Find("pg_dump");
        if (tool is null)
        {
            throw new InvalidOperationException("未在 PATH 中找到 pg_dump。请在运行 Host 的机器上安装 PostgreSQL 客户端工具后再备份。SQLite 备份不需要这个工具。");
        }

        var builder = Parse(connectionString);
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, fileName);
        var args = new List<string>
        {
            "--format=custom",
            "--file=" + path,
            "--host=" + builder.Host,
            "--port=" + builder.Port.ToString(CultureInfo.InvariantCulture),
            "--username=" + builder.Username,
            "--dbname=" + builder.Database
        };
        Run(tool, args, builder.Password, "pg_dump");
        return path;
    }

    public static void Restore(string? connectionString, string path)
    {
        var tool = Find("pg_restore");
        if (tool is null)
        {
            throw new InvalidOperationException("未在 PATH 中找到 pg_restore。请安装 PostgreSQL 客户端工具后再恢复。");
        }

        if (!File.Exists(path))
        {
            throw new InvalidOperationException("找不到要恢复的备份文件。");
        }

        var builder = Parse(connectionString);
        var args = new List<string>
        {
            "--clean",
            "--if-exists",
            "--no-owner",
            "--host=" + builder.Host,
            "--port=" + builder.Port.ToString(CultureInfo.InvariantCulture),
            "--username=" + builder.Username,
            "--dbname=" + builder.Database,
            path
        };
        Run(tool, args, builder.Password, "pg_restore");
    }

    private static Npgsql.NpgsqlConnectionStringBuilder Parse(string? connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException("PostgreSQL 连接字符串为空。");
        }

        var builder = new Npgsql.NpgsqlConnectionStringBuilder(connectionString);
        if (string.IsNullOrWhiteSpace(builder.Host) || string.IsNullOrWhiteSpace(builder.Database))
        {
            throw new InvalidOperationException("PostgreSQL 连接字符串需要 Host 和 Database。");
        }

        return builder;
    }

    private static void Run(string fileName, List<string> arguments, string? password, string tool)
    {
        var start = new ProcessStartInfo
        {
            FileName = fileName,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            UseShellExecute = false
        };
        if (!string.IsNullOrEmpty(password))
        {
            start.Environment["PGPASSWORD"] = password;
        }

        foreach (var argument in arguments)
        {
            start.ArgumentList.Add(argument);
        }

        using var process = Process.Start(start) ?? throw new InvalidOperationException("无法启动 " + tool + "。");
        var stderr = process.StandardError.ReadToEnd();
        process.WaitForExit();
        if (process.ExitCode != 0)
        {
            var detail = string.IsNullOrWhiteSpace(stderr) ? "退出码 " + process.ExitCode.ToString(CultureInfo.InvariantCulture) : stderr.Trim();
            throw new InvalidOperationException(tool + " 失败：" + detail);
        }
    }
}
