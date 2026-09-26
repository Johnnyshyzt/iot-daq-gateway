namespace IotDaq.Persistence.Visualization;

public sealed class SeriesPoint
{
    public string DeviceId { get; set; } = "";

    public string PointId { get; set; } = "";

    public long TimestampUnixMs { get; set; }

    public double Avg { get; set; }

    public double Min { get; set; }

    public double Max { get; set; }

    public int Count { get; set; }
}

public static class SeriesAggregation
{
    public const int MaxBuckets = 800;

    public static long ChooseBucket(long fromUnixMs, long toUnixMs, long requestedMs)
    {
        var span = Math.Max(1, toUnixMs - fromUnixMs);
        if (requestedMs > 0)
        {
            return Math.Clamp(requestedMs, 1000, span);
        }

        var bucket = span / MaxBuckets;
        if (bucket <= 5_000)
        {
            return 5_000;
        }

        if (bucket <= 60_000)
        {
            return 60_000;
        }

        if (bucket <= 300_000)
        {
            return 300_000;
        }

        return Math.Max(300_000, (bucket + 3_600_000 - 1) / 3_600_000 * 3_600_000);
    }

    public static List<SeriesPoint> Aggregate(IReadOnlyList<SeriesSample> samples, long bucketMs)
    {
        if (bucketMs <= 0)
        {
            return samples
                .Where(sample => sample.NumericValue is not null)
                .Select(sample => new SeriesPoint
                {
                    DeviceId = sample.DeviceId,
                    PointId = sample.PointId,
                    TimestampUnixMs = sample.TimestampUnixMs,
                    Avg = sample.NumericValue ?? 0,
                    Min = sample.NumericValue ?? 0,
                    Max = sample.NumericValue ?? 0,
                    Count = 1
                })
                .OrderBy(point => point.TimestampUnixMs)
                .ToList();
        }

        return samples
            .Where(sample => sample.NumericValue is not null)
            .GroupBy(sample => (sample.DeviceId, sample.PointId, Bucket: sample.TimestampUnixMs / bucketMs))
            .Select(group =>
            {
                var values = group.Select(sample => sample.NumericValue ?? 0).ToList();
                return new SeriesPoint
                {
                    DeviceId = group.Key.DeviceId,
                    PointId = group.Key.PointId,
                    TimestampUnixMs = group.Key.Bucket * bucketMs,
                    Avg = values.Average(),
                    Min = values.Min(),
                    Max = values.Max(),
                    Count = values.Count
                };
            })
            .OrderBy(point => point.TimestampUnixMs)
            .ThenBy(point => point.DeviceId, StringComparer.Ordinal)
            .ThenBy(point => point.PointId, StringComparer.Ordinal)
            .ToList();
    }
}

public readonly record struct SeriesSample(string DeviceId, string PointId, long TimestampUnixMs, double? NumericValue);
