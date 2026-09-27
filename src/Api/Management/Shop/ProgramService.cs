using System.Text;
using Adapters.Cnc.Drivers;
using IotDaq.Persistence;
using IotDaq.Persistence.Shop;
using Studio.Host.Config;

namespace Studio.Host.Shop;

public sealed class ProgramSendResult
{
    public bool Ok { get; init; }

    public string Code { get; init; } = "";

    public string Message { get; init; } = "";

    public string Checksum { get; init; } = "";

    public string? Path { get; init; }
}

public static class ProgramTransfer
{
    public const int MaxChars = 256 * 1024;

    public static ProgramSendResult Send(GatewayPersistence database, ConfigStore store, NcProgramVersionRow version, string deviceId, string channel, string? folder, string actor)
    {
        if (!string.Equals(version.Status, "approved", StringComparison.Ordinal))
        {
            return Fail("program_not_approved", "只能下发已批准的程序。");
        }

        if (!TransferCatalog.IsGenericChannel(channel))
        {
            return Fail("transfer_unavailable", "该通道不能传程序。海德汉 LSV2 和厂商 SDK 在本版本只采集，不写文件。请用 DNC 目录或 FTP。");
        }

        var checksum = version.Checksum;
        try
        {
            if (channel == "ftp")
            {
                var device = store.GetDevice(deviceId);
                var connection = device.Spec.Connection;
                var path = string.IsNullOrWhiteSpace(connection.Path) ? "/" + SafeName(version.ProgramId) + ".nc" : connection.Path;
                FtpStatus.UploadAsync(
                    connection.Host,
                    connection.Port,
                    path,
                    connection.Username ?? "",
                    connection.Password ?? "",
                    connection.TimeoutMs ?? 4000,
                    Encoding.UTF8.GetBytes(version.Content),
                    CancellationToken.None).GetAwaiter().GetResult();
                Record(database, version, deviceId, "to-machine", channel, actor, "ok", path);
                return new ProgramSendResult { Ok = true, Message = "已通过 FTP 写入 " + path, Checksum = checksum, Path = path };
            }

            var target = ResolveFolder(database, store, deviceId, folder);
            if (string.IsNullOrWhiteSpace(target))
            {
                return Fail("folder_required", "请在设备参数 dncFolder、请求或 nc.dncRoot 中指定目录。");
            }

            var file = WriteFolder(target, version.ProgramId + "-v" + version.Version.ToString(System.Globalization.CultureInfo.InvariantCulture) + ".nc", version.Content);
            Record(database, version, deviceId, "to-machine", channel, actor, "ok", file);
            return new ProgramSendResult { Ok = true, Message = "已写入 " + file + "。机床侧请从该目录读取。", Checksum = checksum, Path = file };
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or ConfigStoreException)
        {
            Record(database, version, deviceId, "to-machine", channel, actor, "failed", ex.Message);
            return Fail("transfer_failed", ex.Message);
        }
    }

    public static ProgramSendResult Receive(GatewayPersistence database, ConfigStore store, NcProgramRow program, string deviceId, string channel, string? folder, string actor)
    {
        if (!TransferCatalog.IsGenericChannel(channel))
        {
            return Fail("transfer_unavailable", "该通道不能读取程序文件。");
        }

        try
        {
            string content;
            string note;
            if (channel == "ftp")
            {
                var device = store.GetDevice(deviceId);
                var connection = device.Spec.Connection;
                content = FtpStatus.DownloadAsync(
                    connection.Host,
                    connection.Port,
                    string.IsNullOrWhiteSpace(connection.Path) ? "/program.nc" : connection.Path,
                    connection.Username ?? "",
                    connection.Password ?? "",
                    connection.TimeoutMs ?? 4000,
                    CancellationToken.None).GetAwaiter().GetResult();
                note = connection.Path ?? "/program.nc";
            }
            else
            {
                var target = ResolveFolder(database, store, deviceId, folder);
                if (string.IsNullOrWhiteSpace(target))
                {
                    return Fail("folder_required", "请指定要读取的目录。");
                }

                var path = Directory.Exists(target)
                    ? Directory.GetFiles(target, "*.nc").Order(StringComparer.Ordinal).FirstOrDefault()
                    : null;
                if (path is null)
                {
                    return Fail("program_missing", "目录里没有 .nc 文件。");
                }

                content = File.ReadAllText(path);
                note = path;
            }

            if (content.Length > MaxChars)
            {
                return Fail("program_too_large", "程序超过 256 KB。");
            }

            var checksum = ContentHash.Sha256(content);
            var version = database.AddProgramVersion(new NcProgramVersionRow
            {
                Id = Guid.NewGuid().ToString("N"),
                ProgramId = program.Id,
                Content = content.Replace("\r\n", "\n", StringComparison.Ordinal),
                Checksum = checksum,
                Comment = "从设备读取",
                Status = "draft",
                UploadedBy = actor,
                UploadedUnixMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
            });
            Record(database, version, deviceId, "from-machine", channel, actor, "ok", note);
            return new ProgramSendResult { Ok = true, Message = "已存为草稿版本 " + version.Version.ToString(System.Globalization.CultureInfo.InvariantCulture) + "，需要批准后才能再下发。", Checksum = checksum, Path = note };
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or ConfigStoreException)
        {
            return Fail("transfer_failed", ex.Message);
        }
    }

    public static string WriteFolder(string folder, string fileName, string content)
    {
        var root = Path.GetFullPath(folder);
        Directory.CreateDirectory(root);
        var name = SafeName(fileName);
        var path = Path.GetFullPath(Path.Combine(root, name));
        if (!path.StartsWith(root, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("程序文件名不能跳出目录。");
        }

        File.WriteAllText(path, content);
        return path;
    }

    private static string? ResolveFolder(GatewayPersistence database, ConfigStore store, string deviceId, string? folder)
    {
        if (!string.IsNullOrWhiteSpace(folder))
        {
            return folder;
        }

        try
        {
            var device = store.GetDevice(deviceId);
            if (device.Spec.Connection.Parameters is not null
                && device.Spec.Connection.Parameters.TryGetValue("dncFolder", out var configured)
                && !string.IsNullOrWhiteSpace(configured))
            {
                return configured;
            }
        }
        catch (ConfigStoreException)
        {
        }

        var root = database.GetSetting("nc.dncRoot");
        return string.IsNullOrWhiteSpace(root) ? null : Path.Combine(root, deviceId);
    }

    private static void Record(GatewayPersistence database, NcProgramVersionRow version, string deviceId, string direction, string channel, string actor, string result, string message)
    {
        database.AddTransfer(new NcTransferRow
        {
            Id = Guid.NewGuid().ToString("N"),
            ProgramId = version.ProgramId,
            VersionId = version.Id,
            DeviceId = deviceId,
            Direction = direction,
            Channel = channel,
            Checksum = version.Checksum,
            Actor = actor,
            UnixMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            Result = result,
            Message = message.Length > 300 ? message[..300] : message
        });
    }

    private static ProgramSendResult Fail(string code, string message) =>
        new() { Ok = false, Code = code, Message = message };

    private static string SafeName(string name)
    {
        var cleaned = new string(name.Select(ch => Path.GetInvalidFileNameChars().Contains(ch) ? '_' : ch).ToArray());
        return string.IsNullOrWhiteSpace(cleaned) ? "program.nc" : cleaned;
    }
}
