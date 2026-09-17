using System.Reflection;

namespace Gateway.Host;

internal static class HostInfo
{
    public const string WindowsServiceName = "IotDaqGateway";

    public const string WindowsServiceDisplayName = "IoT DAQ Gateway";

    public static string Version { get; } = ReadVersion();

    private static string ReadVersion()
    {
        var assembly = typeof(HostInfo).Assembly;
        var informational = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        if (!string.IsNullOrWhiteSpace(informational))
        {
            return informational;
        }

        return assembly.GetName().Version?.ToString() ?? "unknown";
    }
}
