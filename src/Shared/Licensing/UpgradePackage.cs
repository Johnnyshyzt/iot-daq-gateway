using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;

namespace IotDaq.Licensing;

public sealed class UpgradeInspection
{
    public bool Ok { get; init; }

    public string Code { get; init; } = "";

    public string Message { get; init; } = "";

    public string Version { get; init; } = "";

    public bool SignatureVerified { get; init; }

    public bool ChecksumVerified { get; init; }

    public IReadOnlyList<string> Files { get; init; } = [];
}

/// <summary>
/// A release zip carries SHA256SUMS (payload files only) and SHA256SUMS.sig
/// (ECDSA P-256 over the exact SHA256SUMS bytes).
/// </summary>
public static class UpgradePackage
{
    public const string SumsName = "SHA256SUMS";
    public const string SignatureName = "SHA256SUMS.sig";
    public const long MaxBytes = 400L * 1024 * 1024;

    public static byte[] Create(IReadOnlyList<(string Path, byte[] Content)> files, ECDsa privateKey, string version)
    {
        var payload = new List<(string Path, byte[] Content)>(files)
        {
            ("VERSION.txt", Encoding.UTF8.GetBytes(version.Trim() + "\n"))
        };
        var sums = new StringBuilder();
        foreach (var file in payload.OrderBy(item => item.Path, StringComparer.Ordinal))
        {
            var hash = Convert.ToHexString(SHA256.HashData(file.Content)).ToLowerInvariant();
            sums.Append(hash).Append("  ").Append(file.Path.Replace('\\', '/')).Append('\n');
        }

        var sumBytes = Encoding.UTF8.GetBytes(sums.ToString());
        var signature = Convert.ToBase64String(privateKey.SignData(sumBytes, HashAlgorithmName.SHA256));
        using var memory = new MemoryStream();
        using (var archive = new ZipArchive(memory, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var file in payload)
            {
                Write(archive, file.Path, file.Content);
            }

            Write(archive, SumsName, sumBytes);
            Write(archive, SignatureName, Encoding.UTF8.GetBytes(signature));
        }

        return memory.ToArray();
    }

    public static UpgradeInspection Inspect(Stream zip, ECDsa publicKey)
    {
        if (zip.CanSeek && zip.Length > MaxBytes)
        {
            return Fail("upgrade_too_large", "升级包超过 400 MB，已拒绝。");
        }

        ZipArchive archive;
        try
        {
            archive = new ZipArchive(zip, ZipArchiveMode.Read, leaveOpen: true);
        }
        catch (InvalidDataException)
        {
            return Fail("upgrade_malformed", "上传的文件不是 zip。");
        }

        try
        {
        using (archive)
        {
            var names = new List<string>();
            foreach (var entry in archive.Entries)
            {
                var name = entry.FullName.Replace('\\', '/');
                if (name.Length == 0 || name.EndsWith('/'))
                {
                    continue;
                }

                if (name.StartsWith('/') || name.Contains("..", StringComparison.Ordinal) || Path.IsPathRooted(name))
                {
                    return Fail("upgrade_path", "升级包里有不安全的路径，已拒绝。");
                }

                names.Add(name);
            }

            var sumsEntry = archive.GetEntry(SumsName);
            var sigEntry = archive.GetEntry(SignatureName);
            if (sumsEntry is null || sigEntry is null)
            {
                return Fail("upgrade_unsigned", "升级包缺少 SHA256SUMS 或 SHA256SUMS.sig。未签名的包不能安装。");
            }

            var sumBytes = ReadAll(sumsEntry);
            var signatureText = Encoding.UTF8.GetString(ReadAll(sigEntry)).Trim();
            byte[] signature;
            try
            {
                signature = Convert.FromBase64String(signatureText);
            }
            catch (FormatException)
            {
                return Fail("upgrade_signature", "SHA256SUMS.sig 不是 base64 签名。");
            }

            if (!publicKey.VerifyData(sumBytes, signature, HashAlgorithmName.SHA256))
            {
                return Fail("upgrade_signature", "升级包签名无效。");
            }

            var listed = new List<(string Hash, string Path)>();
            foreach (var line in Encoding.UTF8.GetString(sumBytes).Split('\n', StringSplitOptions.RemoveEmptyEntries))
            {
                var parts = line.Split("  ", 2, StringSplitOptions.None);
                if (parts.Length != 2 || parts[0].Length != 64)
                {
                    return Fail("upgrade_checksum", "SHA256SUMS 格式不正确。");
                }

                listed.Add((parts[0].ToLowerInvariant(), parts[1].Trim().Replace('\\', '/')));
            }

            if (listed.Count == 0)
            {
                return Fail("upgrade_checksum", "SHA256SUMS 没有列出文件。");
            }

            foreach (var item in listed)
            {
                var entry = archive.GetEntry(item.Path);
                if (entry is null)
                {
                    return Fail("upgrade_checksum", "校验清单里的文件不在包中：" + item.Path);
                }

                var hash = Convert.ToHexString(SHA256.HashData(ReadAll(entry))).ToLowerInvariant();
                if (!string.Equals(hash, item.Hash, StringComparison.Ordinal))
                {
                    return Fail("upgrade_checksum", "文件校验失败：" + item.Path);
                }
            }

            var hasHost = names.Any(name => name.Equals("Host.exe", StringComparison.OrdinalIgnoreCase)
                || name.Equals("Host.dll", StringComparison.OrdinalIgnoreCase)
                || name.EndsWith("/Host.exe", StringComparison.OrdinalIgnoreCase)
                || name.EndsWith("/Host.dll", StringComparison.OrdinalIgnoreCase));
            var hasWeb = names.Any(name => name.Equals("wwwroot/index.html", StringComparison.OrdinalIgnoreCase)
                || name.EndsWith("/wwwroot/index.html", StringComparison.OrdinalIgnoreCase));
            if (!hasHost || !hasWeb)
            {
                return Fail("upgrade_layout", "升级包需要包含 Host 程序和 wwwroot/index.html。");
            }

            var version = "";
            var versionEntry = archive.Entries.FirstOrDefault(entry =>
                entry.FullName.Replace('\\', '/').Equals("VERSION.txt", StringComparison.OrdinalIgnoreCase)
                || entry.FullName.Replace('\\', '/').EndsWith("/VERSION.txt", StringComparison.OrdinalIgnoreCase));
            if (versionEntry is not null)
            {
                version = Encoding.UTF8.GetString(ReadAll(versionEntry)).Trim();
            }

            return new UpgradeInspection
            {
                Ok = true,
                Version = version,
                SignatureVerified = true,
                ChecksumVerified = true,
                Files = names,
                Message = "签名和校验和都已通过。"
            };
        }
        }
        catch (InvalidDataException)
        {
            return Fail("upgrade_malformed", "升级包已损坏，无法读取。");
        }
    }

    private static void Write(ZipArchive archive, string path, byte[] content)
    {
        var entry = archive.CreateEntry(path.Replace('\\', '/'), CompressionLevel.Optimal);
        using var stream = entry.Open();
        stream.Write(content);
    }

    private static byte[] ReadAll(ZipArchiveEntry entry)
    {
        using var stream = entry.Open();
        using var memory = new MemoryStream();
        stream.CopyTo(memory);
        if (memory.Length > MaxBytes)
        {
            throw new InvalidDataException("entry too large");
        }

        return memory.ToArray();
    }

    private static UpgradeInspection Fail(string code, string message) =>
        new() { Ok = false, Code = code, Message = message };
}
