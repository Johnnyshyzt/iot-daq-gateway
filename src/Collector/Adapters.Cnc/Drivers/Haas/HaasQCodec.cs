using System.Globalization;

namespace Adapters.Cnc.Drivers;

/// <summary>
/// Haas NGC Machine Data Collection replies on TCP 5051.
/// Queries are <c>?Qnnn</c>. The control ends each reply with <c>&gt;</c>.
/// </summary>
public static class HaasQCodec
{
    public static readonly string[] DefaultQueries = ["Q100", "Q101", "Q102", "Q104", "Q201", "Q300", "Q301", "Q303", "Q402", "Q403", "Q500"];

    public static string Query(string code) => "?" + code.Trim().TrimStart('?') + "\n";

    public static string TakeReply(string buffer, out string rest)
    {
        var mark = buffer.IndexOf('>');
        if (mark < 0)
        {
            rest = buffer;
            return "";
        }

        var reply = buffer[..mark];
        rest = buffer[(mark + 1)..];
        return reply;
    }

    public static void Apply(string code, string reply, IDictionary<string, object?> values)
    {
        var text = reply.Replace("\r", "", StringComparison.Ordinal).Trim();
        if (text.Length == 0)
        {
            return;
        }

        var normalized = code.Trim().TrimStart('?').ToUpperInvariant();
        if (normalized == "Q500")
        {
            ApplyCombined(text, values);
            return;
        }

        var value = LastField(text);
        switch (normalized)
        {
            case "Q100":
                values["serialNumber"] = value;
                break;
            case "Q101":
                values["softwareVersion"] = value;
                break;
            case "Q102":
                values["systemType"] = value;
                break;
            case "Q104":
                values["workMode"] = value;
                break;
            case "Q201":
                values["toolNumber"] = value;
                break;
            case "Q300":
                values["powerOnTime"] = Seconds(value);
                break;
            case "Q301":
                values["runTime"] = Seconds(value);
                break;
            case "Q303":
                values["cycleTime"] = Seconds(value);
                break;
            case "Q402":
                values["partCount"] = Integer(value);
                break;
            case "Q403":
                values["partCountTotal"] = Integer(value);
                break;
            case "Q600":
                values["haas_macro"] = value;
                break;
        }
    }

    public static void ApplyCombined(string text, IDictionary<string, object?> values)
    {
        var tokens = text.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        for (var i = 0; i < tokens.Length; i++)
        {
            var token = tokens[i];
            var next = i + 1 < tokens.Length ? tokens[i + 1] : "";
            if (token.Contains("PROGRAM", StringComparison.OrdinalIgnoreCase) && next.Length > 0 && !IsStatus(next))
            {
                values["program"] = next;
                i++;
                continue;
            }

            if (token.Contains("PART", StringComparison.OrdinalIgnoreCase) && next.Length > 0 && !IsStatus(next))
            {
                values["partCount"] = Integer(next);
                i++;
                continue;
            }

            if (IsStatus(token))
            {
                values["state"] = MapStatus(token);
            }
            else if (IsStatus(next) && token.Contains("STATUS", StringComparison.OrdinalIgnoreCase))
            {
                values["state"] = MapStatus(next);
                i++;
            }
        }

        if (!values.ContainsKey("state"))
        {
            values["state"] = MapStatus(text);
        }
    }

    public static string MapStatus(string text)
    {
        var upper = text.ToUpperInvariant();
        if (upper.Contains("ALARM", StringComparison.Ordinal) || upper.Contains("ESTOP", StringComparison.Ordinal))
        {
            return "ALARM";
        }

        if (upper.Contains("FEED", StringComparison.Ordinal) || upper.Contains("RUN", StringComparison.Ordinal) || upper.Contains("BUSY", StringComparison.Ordinal))
        {
            return "RUNNING";
        }

        return "IDLE";
    }

    private static bool IsStatus(string token)
    {
        var upper = token.ToUpperInvariant();
        return upper is "IDLE" or "RUNNING" or "FEED HOLD" or "ALARM" or "BUSY" or "STOPPED"
            || upper.Contains("ALARM", StringComparison.Ordinal)
            || upper.Contains("FEED", StringComparison.Ordinal);
    }

    private static string LastField(string text)
    {
        var tokens = text.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        return tokens.Length == 0 ? text.Trim() : tokens[^1];
    }

    private static long Seconds(string value)
    {
        var parts = value.Split(':', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 3
            && long.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var hours)
            && long.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var minutes)
            && long.TryParse(parts[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out var seconds))
        {
            return hours * 3600 + minutes * 60 + seconds;
        }

        return long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var raw) ? raw : 0;
    }

    private static long Integer(string value) =>
        long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var number) ? number : 0;
}
