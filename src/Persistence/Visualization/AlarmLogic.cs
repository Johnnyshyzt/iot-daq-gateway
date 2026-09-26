using System.Globalization;
using System.Text.RegularExpressions;

namespace IotDaq.Persistence.Visualization;

public static partial class AlarmLogic
{
    public static bool IsSpecificPoint(string? point)
    {
        var name = point ?? "";
        if (string.Equals(name, "state", StringComparison.OrdinalIgnoreCase)
            || string.Equals(name, "estop", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return name.Contains("alarm", StringComparison.OrdinalIgnoreCase)
            || string.Equals(name, "alarmNumber", StringComparison.OrdinalIgnoreCase)
            || name.EndsWith("_warningNumber", StringComparison.Ordinal)
            || name.EndsWith("Alarm", StringComparison.Ordinal);
    }

    public static bool IsActive(string? point, string? text)
    {
        var name = point ?? "";
        var value = (text ?? "").Trim();
        if (string.Equals(name, "state", StringComparison.OrdinalIgnoreCase))
        {
            return MachineState.Normalize(value) == MachineState.Alarm;
        }

        if (string.Equals(name, "estop", StringComparison.OrdinalIgnoreCase))
        {
            return value is "1" or "true" or "True" or "急停";
        }

        if (!IsSpecificPoint(name))
        {
            return false;
        }

        return value.Length > 0 && value is not ("0" or "none" or "正常" or "OK" or "false" or "False");
    }

    public static string CodeFrom(string? point, string? text)
    {
        var value = (text ?? "").Trim();
        var match = CodePattern().Match(value);
        if (match.Success)
        {
            return match.Value;
        }

        if (string.Equals(point, "state", StringComparison.OrdinalIgnoreCase))
        {
            return "STATE";
        }

        if (string.Equals(point, "estop", StringComparison.OrdinalIgnoreCase))
        {
            return "ESTOP";
        }

        return string.IsNullOrWhiteSpace(point) ? "ALARM" : point;
    }

    public static string SeverityFor(string? point) =>
        string.Equals(point, "estop", StringComparison.OrdinalIgnoreCase) ? "estop" : "alarm";

    public static long Duration(long raisedUnixMs, long? clearedUnixMs, long nowUnixMs, bool active)
    {
        var end = active || clearedUnixMs is null ? nowUnixMs : clearedUnixMs.Value;
        return Math.Max(0, end - raisedUnixMs);
    }

    public static string FormatNumber(double value) =>
        value.ToString("0.###", CultureInfo.InvariantCulture);

    [GeneratedRegex(@"[A-Za-z]*\d+[A-Za-z0-9-]*", RegexOptions.CultureInvariant)]
    private static partial Regex CodePattern();
}
