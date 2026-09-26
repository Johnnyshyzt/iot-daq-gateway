namespace IotDaq.Persistence.Visualization;

public sealed class AlarmStatInput
{
    public string DeviceId { get; set; } = "";

    public string Code { get; set; } = "";

    public string Message { get; set; } = "";

    public long DurationMs { get; set; }
}

public sealed class AlarmStatBucket
{
    public string Key { get; set; } = "";

    public string Label { get; set; } = "";

    public int Count { get; set; }

    public long DurationMs { get; set; }
}

public static class AlarmStats
{
    public static (List<AlarmStatBucket> ByDevice, List<AlarmStatBucket> ByCode) Top(
        IReadOnlyList<AlarmStatInput> alarms,
        int top)
    {
        top = Math.Clamp(top, 1, 50);
        return (Rank(alarms, alarm => alarm.DeviceId, alarm => alarm.DeviceId, top),
            Rank(alarms, alarm => alarm.Code, alarm => string.IsNullOrWhiteSpace(alarm.Message) ? alarm.Code : alarm.Message, top));
    }

    private static List<AlarmStatBucket> Rank(
        IReadOnlyList<AlarmStatInput> alarms,
        Func<AlarmStatInput, string> key,
        Func<AlarmStatInput, string> label,
        int top)
    {
        return alarms
            .GroupBy(key)
            .Select(group => new AlarmStatBucket
            {
                Key = group.Key,
                Label = group.Select(label).FirstOrDefault(text => !string.IsNullOrWhiteSpace(text)) ?? group.Key,
                Count = group.Count(),
                DurationMs = group.Sum(alarm => alarm.DurationMs)
            })
            .OrderByDescending(bucket => bucket.Count)
            .ThenByDescending(bucket => bucket.DurationMs)
            .ThenBy(bucket => bucket.Key, StringComparer.Ordinal)
            .Take(top)
            .ToList();
    }
}
