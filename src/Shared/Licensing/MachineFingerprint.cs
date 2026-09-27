using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;

namespace IotDaq.Licensing;

/// <summary>
/// Stable fingerprint the operator copies to the vendor. It is a hash, not a TPM quote.
/// Linux prefers /etc/machine-id. Windows prefers the cryptography MachineGuid.
/// Hostname is always mixed in. Containers that share an id will share a fingerprint.
/// </summary>
public static class MachineFingerprint
{
    public static string Current()
    {
        var material = new StringBuilder();
        if (OperatingSystem.IsLinux() && File.Exists("/etc/machine-id"))
        {
            material.Append(File.ReadAllText("/etc/machine-id").Trim());
        }
        else if (OperatingSystem.IsWindows())
        {
            material.Append(WindowsGuid() ?? "");
        }

        material.Append('|').Append(Environment.MachineName);
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(material.ToString()));
        return Convert.ToHexString(hash).ToLowerInvariant()[..32];
    }

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
}
