namespace Gateway.Abstractions.Topics;

public static class DaqTopics
{
    public const string Prefix = "daq";

    public static string Point(string site, string deviceId, string point)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(site);
        ArgumentException.ThrowIfNullOrWhiteSpace(deviceId);
        ArgumentException.ThrowIfNullOrWhiteSpace(point);
        return $"{Prefix}/{site}/{deviceId}/{point}";
    }

    public static string Status(string site, string deviceId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(site);
        ArgumentException.ThrowIfNullOrWhiteSpace(deviceId);
        return $"{Prefix}/{site}/{deviceId}/$status";
    }

    public const string DefaultPointTemplate = "daq/{site}/{deviceId}/{point}";

    public const string DefaultStatusTemplate = "daq/{site}/{deviceId}/$status";

    public static string PointTopic(string? template, string site, string deviceId, string point)
    {
        if (string.IsNullOrWhiteSpace(template) || template == DefaultPointTemplate)
        {
            return Point(site, deviceId, point);
        }

        return Apply(template, site, deviceId, point);
    }

    public static string StatusTopic(string? template, string site, string deviceId)
    {
        if (string.IsNullOrWhiteSpace(template) || template == DefaultStatusTemplate)
        {
            return Status(site, deviceId);
        }

        return Apply(template, site, deviceId, point: null);
    }

    private static string Apply(string template, string site, string deviceId, string? point)
    {
        var value = template
            .Replace("{site}", site, StringComparison.Ordinal)
            .Replace("{deviceId}", deviceId, StringComparison.Ordinal);
        if (point is not null)
        {
            value = value.Replace("{point}", point, StringComparison.Ordinal);
        }

        return value;
    }
}
