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
}
