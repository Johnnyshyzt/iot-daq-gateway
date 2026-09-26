using System.Globalization;
using Gateway.Abstractions.Models;

namespace Adapters.Cnc.Drivers;

public static class PointValues
{
    public static Observation Make(string deviceId, string point, object? value, string? unit = null, string quality = "good")
    {
        return new Observation
        {
            DeviceId = deviceId,
            Point = point,
            Value = value,
            Unit = string.IsNullOrWhiteSpace(unit) ? null : unit,
            Quality = quality,
            Timestamp = DateTimeOffset.UtcNow
        };
    }

    public static string Format(object? value)
    {
        return value switch
        {
            null => "",
            bool flag => flag ? "true" : "false",
            IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture) ?? "",
            _ => Convert.ToString(value, CultureInfo.InvariantCulture) ?? ""
        };
    }
}
