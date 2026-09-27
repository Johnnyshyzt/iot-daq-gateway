using System.Net.NetworkInformation;
using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;

namespace IotDaq.Licensing;

/// <summary>
/// Machine binding material. v2 (<c>fp2.</c>) keeps the OS id, hostname, board id, and NIC hashes
/// separate so one NIC change still matches. Older licenses store the 32-hex hash of machine-id and hostname.
/// This is not a TPM quote.
/// </summary>
public static class MachineFingerprint
{
    public static string Current() => Format(Capture());

    public static FingerprintParts Capture()
    {
        var primaryMaterial = PrimaryMaterial();
        var host = Environment.MachineName ?? "";
        var board = BoardMaterial();
        return new FingerprintParts
        {
            Primary = Hash(primaryMaterial.Length == 0 ? "none|" + host : primaryMaterial, 32),
            Host = Hash(host, 16),
            Board = board.Length == 0 ? "" : Hash(board, 16),
            Nics = ReadNicHashes(),
            LegacyMaterial = primaryMaterial + "|" + host
        };
    }

    public static string Format(FingerprintParts parts) =>
        "fp2." + parts.Primary + "." + parts.Host + "." + parts.Board + "." + string.Join(',', parts.Nics);

    public static bool IsWellFormed(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var value = text.Trim().ToLowerInvariant();
        if (!value.StartsWith("fp2.", StringComparison.Ordinal))
        {
            return value.Length is >= 8 and <= 128 && value.All(Uri.IsHexDigit);
        }

        return TryParse(value, out _);
    }

    /// <summary>
    /// A bound fingerprint matches when the primary id matches and at most one NIC was added or removed
    /// (a replacement counts as one) and at most one of hostname or board id differs.
    /// Empty NIC lists are ignored so a host that cannot see adapters does not invalidate the license.
    /// Legacy 32-hex values match only the previous machine-id|hostname hash.
    /// </summary>
    public static bool Matches(string? bound, FingerprintParts? current = null)
    {
        if (string.IsNullOrWhiteSpace(bound))
        {
            return true;
        }

        current ??= Capture();
        var text = bound.Trim().ToLowerInvariant();
        if (!text.StartsWith("fp2.", StringComparison.Ordinal))
        {
            return string.Equals(text, LegacyHash(current.LegacyMaterial), StringComparison.Ordinal);
        }

        if (!TryParse(text, out var parts))
        {
            return false;
        }

        return MatchesParts(parts, current);
    }

    public static bool MatchesParts(FingerprintParts bound, FingerprintParts current)
    {
        if (bound.Primary.Length == 0 || !string.Equals(bound.Primary, current.Primary, StringComparison.Ordinal))
        {
            return false;
        }

        if (bound.Nics.Count > 0 && current.Nics.Count > 0)
        {
            var boundSet = new HashSet<string>(bound.Nics, StringComparer.Ordinal);
            var currentSet = new HashSet<string>(current.Nics, StringComparer.Ordinal);
            var removed = boundSet.Count(item => !currentSet.Contains(item));
            var added = currentSet.Count(item => !boundSet.Contains(item));
            if (Math.Max(removed, added) > 1)
            {
                return false;
            }
        }

        var hostChanged = !string.Equals(bound.Host, current.Host, StringComparison.Ordinal);
        var boardChanged = !string.Equals(bound.Board, current.Board, StringComparison.Ordinal);
        return (hostChanged ? 1 : 0) + (boardChanged ? 1 : 0) <= 1;
    }

    public static string LegacyHash(string material)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(material ?? ""));
        return Convert.ToHexString(hash).ToLowerInvariant()[..32];
    }

    public static bool TryParse(string text, out FingerprintParts parts)
    {
        parts = new FingerprintParts();
        var value = text.Trim().ToLowerInvariant();
        var pieces = value.Split('.', 5);
        if (pieces.Length != 5 || pieces[0] != "fp2")
        {
            return false;
        }

        if (!IsHex(pieces[1], 32) || !IsHex(pieces[2], 16) || !(pieces[3].Length == 0 || IsHex(pieces[3], 16)))
        {
            return false;
        }

        var nics = new List<string>();
        if (pieces[4].Length > 0)
        {
            foreach (var nic in pieces[4].Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                if (!IsHex(nic, 16))
                {
                    return false;
                }

                nics.Add(nic);
            }
        }

        parts = new FingerprintParts
        {
            Primary = pieces[1],
            Host = pieces[2],
            Board = pieces[3],
            Nics = nics
        };
        return true;
    }

    private static string PrimaryMaterial()
    {
        if (OperatingSystem.IsLinux() && File.Exists("/etc/machine-id"))
        {
            return File.ReadAllText("/etc/machine-id").Trim();
        }

        if (OperatingSystem.IsWindows())
        {
            return WindowsGuid() ?? "";
        }

        return "";
    }

    private static string BoardMaterial()
    {
        if (OperatingSystem.IsLinux())
        {
            const string path = "/sys/class/dmi/id/product_uuid";
            if (File.Exists(path))
            {
                try
                {
                    return File.ReadAllText(path).Trim();
                }
                catch (IOException)
                {
                    return "";
                }
                catch (UnauthorizedAccessException)
                {
                    return "";
                }
            }
        }

        if (OperatingSystem.IsWindows())
        {
            return WindowsBoard() ?? "";
        }

        return "";
    }

    private static List<string> ReadNicHashes()
    {
        var list = new List<string>();
        try
        {
            foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (nic.NetworkInterfaceType == NetworkInterfaceType.Loopback)
                {
                    continue;
                }

                byte[]? mac;
                try
                {
                    mac = nic.GetPhysicalAddress()?.GetAddressBytes();
                }
                catch (NetworkInformationException)
                {
                    continue;
                }

                if (mac is not { Length: 6 } || mac.All(item => item == 0))
                {
                    continue;
                }

                list.Add(Hash(Convert.ToHexString(mac).ToLowerInvariant(), 16));
            }
        }
        catch (NetworkInformationException)
        {
            return [];
        }

        return list.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).Take(8).ToList();
    }

    private static string Hash(string material, int hexChars)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(material));
        return Convert.ToHexString(hash).ToLowerInvariant()[..hexChars];
    }

    private static bool IsHex(string text, int length) =>
        text.Length == length && text.All(Uri.IsHexDigit);

    [SupportedOSPlatform("windows")]
    private static string? WindowsGuid()
    {
        try
        {
            return Microsoft.Win32.Registry.GetValue(
                @"HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Cryptography",
                "MachineGuid",
                null) as string;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            return null;
        }
    }

    [SupportedOSPlatform("windows")]
    private static string? WindowsBoard()
    {
        try
        {
            var product = Microsoft.Win32.Registry.GetValue(
                @"HKEY_LOCAL_MACHINE\HARDWARE\DESCRIPTION\System\BIOS",
                "SystemProductName",
                null) as string ?? "";
            var vendor = Microsoft.Win32.Registry.GetValue(
                @"HKEY_LOCAL_MACHINE\HARDWARE\DESCRIPTION\System\BIOS",
                "SystemManufacturer",
                null) as string ?? "";
            var joined = (vendor + "|" + product).Trim('|');
            return joined.Length == 0 ? null : joined;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            return null;
        }
    }
}

public sealed class FingerprintParts
{
    public string Primary { get; init; } = "";

    public string Host { get; init; } = "";

    public string Board { get; init; } = "";

    public List<string> Nics { get; init; } = [];

    public string LegacyMaterial { get; init; } = "";
}
