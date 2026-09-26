using System.Globalization;
using System.Xml.Linq;

namespace Adapters.Cnc.Drivers;

/// <summary>
/// MTConnect Streams / Devices XML. Namespace-agnostic so 1.x and 2.x agents both parse.
/// </summary>
public static class MtConnectXml
{
    public sealed record Sample(
        string DataItemId,
        string Name,
        string Type,
        string SubType,
        string Component,
        string Value,
        string Condition);

    public static IReadOnlyList<Sample> Parse(string xml)
    {
        if (string.IsNullOrWhiteSpace(xml))
        {
            return [];
        }

        var document = XDocument.Parse(xml, LoadOptions.None);
        var rows = new List<Sample>();
        foreach (var element in document.Descendants())
        {
            var local = element.Name.LocalName;
            if (local is "Header" or "Streams" or "Devices" or "DeviceStream" or "ComponentStream"
                or "Events" or "Samples" or "Condition" or "MTConnectStreams" or "MTConnectDevices"
                or "MTConnectError" or "Errors" or "Error")
            {
                if (local == "Error")
                {
                    rows.Add(new Sample("", "", "Error", "", "", element.Value.Trim(), "error"));
                }

                continue;
            }

            if (element.Parent is null)
            {
                continue;
            }

            var parent = element.Parent.Name.LocalName;
            if (parent is not ("Events" or "Samples" or "Condition"))
            {
                continue;
            }

            rows.Add(new Sample(
                Attribute(element, "dataItemId"),
                Attribute(element, "name"),
                local,
                Attribute(element, "subType"),
                ComponentName(element),
                element.Value.Trim(),
                parent == "Condition" ? local : ""));
        }

        return rows;
    }

    public static IReadOnlyDictionary<string, object?> ToCatalog(IReadOnlyList<Sample> samples)
    {
        var values = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        var positions = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        string? alarm = null;

        foreach (var sample in samples)
        {
            if (sample.Type.Equals("Error", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var type = sample.Type;
            var sub = sample.SubType;
            var name = sample.Name;
            if (type.Equals("Execution", StringComparison.OrdinalIgnoreCase))
            {
                values["state"] = MapExecution(sample.Value);
            }
            else if (type.Equals("ControllerMode", StringComparison.OrdinalIgnoreCase) || type.Equals("Mode", StringComparison.OrdinalIgnoreCase))
            {
                values["workMode"] = sample.Value;
            }
            else if (type.Equals("EmergencyStop", StringComparison.OrdinalIgnoreCase))
            {
                values["estop"] = sample.Value.Equals("TRIGGERED", StringComparison.OrdinalIgnoreCase);
                if (sample.Value.Equals("TRIGGERED", StringComparison.OrdinalIgnoreCase))
                {
                    values["state"] = "ALARM";
                }
            }
            else if (type.Equals("Program", StringComparison.OrdinalIgnoreCase))
            {
                values["program"] = sample.Value;
            }
            else if (type.Equals("ProgramComment", StringComparison.OrdinalIgnoreCase))
            {
                values["programComment"] = sample.Value;
            }
            else if (type.Equals("ProgramHeader", StringComparison.OrdinalIgnoreCase) || type.Equals("Block", StringComparison.OrdinalIgnoreCase))
            {
                values["programBlock"] = sample.Value;
            }
            else if (type.Equals("Line", StringComparison.OrdinalIgnoreCase))
            {
                values["programLine"] = Number(sample.Value);
            }
            else if (type.Equals("PartCount", StringComparison.OrdinalIgnoreCase))
            {
                var key = sub.Equals("TARGET", StringComparison.OrdinalIgnoreCase) ? "partCountTarget"
                    : name.Contains("total", StringComparison.OrdinalIgnoreCase) ? "partCountTotal"
                    : "partCount";
                values[key] = Number(sample.Value);
            }
            else if (type.Equals("PathFeedrate", StringComparison.OrdinalIgnoreCase) || type.Equals("Feedrate", StringComparison.OrdinalIgnoreCase))
            {
                values[sub.Equals("COMMANDED", StringComparison.OrdinalIgnoreCase) || sub.Equals("PROGRAMMED", StringComparison.OrdinalIgnoreCase)
                    ? "feedRateCmd"
                    : "feedRate"] = Number(sample.Value);
            }
            else if (type.Equals("PathFeedrateOverride", StringComparison.OrdinalIgnoreCase) || type.Equals("FeedrateOverride", StringComparison.OrdinalIgnoreCase))
            {
                values["feedOverride"] = Number(sample.Value);
            }
            else if (type.Equals("SpindleSpeed", StringComparison.OrdinalIgnoreCase) || type.Equals("RotaryVelocity", StringComparison.OrdinalIgnoreCase))
            {
                values[sub.Equals("COMMANDED", StringComparison.OrdinalIgnoreCase) || sub.Equals("PROGRAMMED", StringComparison.OrdinalIgnoreCase)
                    ? "spindleSpeedCmd"
                    : "spindleSpeed"] = Number(sample.Value);
            }
            else if (type.Equals("RotaryVelocityOverride", StringComparison.OrdinalIgnoreCase) || type.Equals("SpindleSpeedOverride", StringComparison.OrdinalIgnoreCase))
            {
                values["spindleOverride"] = Number(sample.Value);
            }
            else if (type.Equals("ToolNumber", StringComparison.OrdinalIgnoreCase) || type.Equals("ToolId", StringComparison.OrdinalIgnoreCase))
            {
                values["toolNumber"] = sample.Value;
            }
            else if (type.Equals("Position", StringComparison.OrdinalIgnoreCase) || type.Equals("PathPosition", StringComparison.OrdinalIgnoreCase))
            {
                var axis = AxisLetter(name, sample.DataItemId);
                if (axis is not null && (string.IsNullOrEmpty(sub) || sub.Equals("ACTUAL", StringComparison.OrdinalIgnoreCase)))
                {
                    positions[axis] = sample.Value;
                }
            }
            else if (!string.IsNullOrEmpty(sample.Condition) && sample.Condition is not ("Normal" or "Unavailable"))
            {
                alarm = string.IsNullOrEmpty(sample.Value) ? sample.Condition : sample.Value;
            }
            else if (!string.IsNullOrEmpty(name) && !values.ContainsKey(name))
            {
                values[name] = NumberOrText(sample.Value);
            }
        }

        if (alarm is not null)
        {
            values["alarm"] = alarm;
            if (values.TryGetValue("state", out var state) && state is string text && text != "ALARM")
            {
                values["state"] = "ALARM";
            }
        }
        else if (!values.ContainsKey("alarm"))
        {
            values["alarm"] = "0";
        }

        if (positions.Count > 0)
        {
            var ordered = string.Join(' ', new[] { "X", "Y", "Z", "A", "B", "C" }
                .Where(positions.ContainsKey)
                .Select(axis => axis + FormatNumber(positions[axis])));
            if (!string.IsNullOrEmpty(ordered))
            {
                values["machinePosition"] = ordered;
                values["absolutePosition"] = ordered;
            }

            foreach (var pair in positions)
            {
                values["machinePosition" + pair.Key] = Number(pair.Value);
                values["absolutePosition" + pair.Key] = Number(pair.Value);
            }
        }

        return values;
    }

    public static string MapExecution(string value) => value.ToUpperInvariant() switch
    {
        "ACTIVE" => "RUNNING",
        "INTERRUPTED" or "FEED_HOLD" or "STOPPED" or "PROGRAM_STOPPED" or "PROGRAM_COMPLETED" or "READY" or "OPTIONAL_STOP" => "IDLE",
        "UNAVAILABLE" => "OFFLINE",
        _ => value
    };

    private static string ComponentName(XElement element)
    {
        var parent = element.Parent;
        while (parent is not null && parent.Name.LocalName != "ComponentStream")
        {
            parent = parent.Parent;
        }

        return parent is null ? "" : Attribute(parent, "name");
    }

    private static string Attribute(XElement element, string name) =>
        element.Attribute(name)?.Value ?? "";

    private static string? AxisLetter(string name, string id)
    {
        foreach (var token in new[] { name, id })
        {
            if (string.IsNullOrWhiteSpace(token))
            {
                continue;
            }

            var trimmed = token.Trim();
            if (trimmed.Length == 1 && "XYZABC".Contains(char.ToUpperInvariant(trimmed[0]), StringComparison.Ordinal))
            {
                return char.ToUpperInvariant(trimmed[0]).ToString();
            }

            var last = trimmed[^1];
            if ("XYZABC".Contains(char.ToUpperInvariant(last), StringComparison.Ordinal)
                && (trimmed.Contains("pos", StringComparison.OrdinalIgnoreCase) || trimmed.Contains('_')))
            {
                return char.ToUpperInvariant(last).ToString();
            }
        }

        return null;
    }

    private static object NumberOrText(string value) =>
        double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out _) ? Number(value) : value;

    private static object Number(string value)
    {
        if (long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var integer))
        {
            return integer;
        }

        if (double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var number))
        {
            return number;
        }

        return value;
    }

    private static string FormatNumber(string value)
    {
        if (double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var number))
        {
            return number.ToString("0.###", CultureInfo.InvariantCulture);
        }

        return value;
    }
}
