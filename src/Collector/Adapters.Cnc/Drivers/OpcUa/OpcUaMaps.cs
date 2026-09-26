using System.Globalization;

namespace Adapters.Cnc.Drivers;

public static class OpcUaMaps
{
    public const string SiemensNamespaceHint = "sinumerik";

    public static readonly IReadOnlyDictionary<string, string> Siemens = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["state"] = "/Channel/State/progStatus",
        ["workMode"] = "/Channel/State/opMode",
        ["program"] = "/Channel/ProgramInfo/progName",
        ["programBlock"] = "/Channel/ProgramInfo/actBlock",
        ["partCount"] = "/Channel/State/actParts",
        ["feedRate"] = "/Channel/MachineAxis/actFeedRateIpo[u1]",
        ["feedRateCmd"] = "/Channel/MachineAxis/cmdFeedRateIpo[u1]",
        ["feedOverride"] = "/Channel/MachineAxis/feedRateIpoOvr[u1]",
        ["spindleSpeed"] = "/Channel/Spindle/actSpeed[u1,1]",
        ["spindleSpeedCmd"] = "/Channel/Spindle/cmdSpeed[u1,1]",
        ["spindleOverride"] = "/Channel/Spindle/speedOvr[u1,1]",
        ["toolNumber"] = "/Channel/State/actTNumber",
        ["alarm"] = "/Channel/State/actAlarm"
    };

    public static readonly (string ItemId, string Path)[] SiemensAxes =
    [
        ("machinePositionX", "/Channel/GeometricAxis/actToolBasePos[u1,1]"),
        ("machinePositionY", "/Channel/GeometricAxis/actToolBasePos[u1,2]"),
        ("machinePositionZ", "/Channel/GeometricAxis/actToolBasePos[u1,3]")
    ];

    public static bool UsesSiemensMap(string? adapterId, string? brandId) =>
        string.Equals(brandId, "siemens", StringComparison.OrdinalIgnoreCase)
        || (adapterId?.Contains("siemens", StringComparison.OrdinalIgnoreCase) ?? false);

    public static bool IsExplicitNode(string? address) =>
        !string.IsNullOrWhiteSpace(address)
        && (address.StartsWith("ns=", StringComparison.OrdinalIgnoreCase)
            || address.StartsWith("opcua:", StringComparison.OrdinalIgnoreCase));

    public static string ExplicitNode(string address) =>
        address.StartsWith("opcua:", StringComparison.OrdinalIgnoreCase) ? address["opcua:".Length..] : address;

    public static string Node(int namespaceIndex, string path) =>
        "ns=" + namespaceIndex.ToString(CultureInfo.InvariantCulture) + ";s=" + path;

    public static string MapState(object? status, object? alarm)
    {
        if (ToLong(alarm) is > 0)
        {
            return "ALARM";
        }

        return ToLong(status) switch
        {
            3 => "RUNNING",
            2 => "HOLD",
            _ => "IDLE"
        };
    }

    public static long? ToLong(object? value)
    {
        return value switch
        {
            null => null,
            bool flag => flag ? 1 : 0,
            byte or sbyte or short or ushort or int or uint or long => Convert.ToInt64(value, CultureInfo.InvariantCulture),
            float or double or decimal => (long)Convert.ToDouble(value, CultureInfo.InvariantCulture),
            string text when long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var number) => number,
            _ => null
        };
    }
}

public sealed record OpcReading(object? Value, string? Error);

public sealed record OpcConnectInfo(string Url, int TimeoutMs, string Username, string Password);

public interface IOpcUaSession : IAsyncDisposable
{
    Task ConnectAsync(OpcConnectInfo info, CancellationToken cancellationToken);

    int ResolveNamespace(string? uriHint, string fallbackContains);

    Task<IReadOnlyDictionary<string, OpcReading>> ReadAsync(IReadOnlyList<string> nodeIds, CancellationToken cancellationToken);

    Task<IReadOnlyDictionary<string, string>> BrowseNamesAsync(int limit, CancellationToken cancellationToken);
}
