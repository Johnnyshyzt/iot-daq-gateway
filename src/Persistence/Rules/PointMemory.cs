namespace IotDaq.Persistence.Rules;

/// <summary>
/// Bounded per-device history for time functions. Each point keeps at most 240 samples
/// and drops anything older than two hours.
/// </summary>
public sealed class PointMemory
{
    private const int MaxSamples = 240;
    private const long MaxAgeMs = 2 * 60 * 60 * 1000;

    private readonly Dictionary<string, List<Sample>> _series = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, (bool Active, long Since)> _duration = new(StringComparer.Ordinal);

    public void Observe(string point, long unixMs, double value)
    {
        if (!_series.TryGetValue(point, out var series))
        {
            series = [];
            _series[point] = series;
        }

        if (series.Count > 0 && series[^1].UnixMs == unixMs)
        {
            series[^1] = new Sample(unixMs, value);
        }
        else
        {
            series.Add(new Sample(unixMs, value));
        }

        var cutoff = unixMs - MaxAgeMs;
        while (series.Count > MaxSamples || (series.Count > 1 && series[0].UnixMs < cutoff))
        {
            series.RemoveAt(0);
        }
    }

    public double? Delta(string point)
    {
        var series = Series(point);
        if (series.Count < 2)
        {
            return series.Count == 0 ? null : 0;
        }

        return series[^1].Value - series[^2].Value;
    }

    public double? Rate(string point)
    {
        var series = Series(point);
        if (series.Count < 2)
        {
            return series.Count == 0 ? null : 0;
        }

        var dt = (series[^1].UnixMs - series[^2].UnixMs) / 1000d;
        if (dt <= 0)
        {
            return null;
        }

        return (series[^1].Value - series[^2].Value) / dt;
    }

    public double? Average(string point, double windowSeconds, long nowMs)
    {
        var series = Series(point);
        if (series.Count == 0)
        {
            return null;
        }

        var from = nowMs - (long)(windowSeconds * 1000);
        double sum = 0;
        var count = 0;
        foreach (var sample in series)
        {
            if (sample.UnixMs < from)
            {
                continue;
            }

            sum += sample.Value;
            count++;
        }

        return count == 0 ? null : sum / count;
    }

    public double DurationTrue(string key, bool current, long nowMs)
    {
        if (!_duration.TryGetValue(key, out var state) || state.Active != current)
        {
            state = (current, nowMs);
            _duration[key] = state;
        }

        if (!current)
        {
            return 0;
        }

        return Math.Max(0, (nowMs - state.Since) / 1000d);
    }

    public double? CounterIncrement(string point, double? modulus)
    {
        var series = Series(point);
        if (series.Count < 2)
        {
            return series.Count == 0 ? null : 0;
        }

        var previous = series[^2].Value;
        var current = series[^1].Value;
        if (current >= previous)
        {
            return current - previous;
        }

        if (modulus is > 0)
        {
            return Math.Max(0, modulus.Value - previous) + Math.Max(0, current);
        }

        return Math.Max(0, current);
    }

    public double? Since(string point, long nowMs)
    {
        var series = Series(point);
        if (series.Count == 0)
        {
            return null;
        }

        var changedAt = series[0].UnixMs;
        for (var i = series.Count - 1; i > 0; i--)
        {
            if (series[i].Value != series[i - 1].Value)
            {
                changedAt = series[i].UnixMs;
                break;
            }
        }

        return Math.Max(0, (nowMs - changedAt) / 1000d);
    }

    private List<Sample> Series(string point) =>
        _series.TryGetValue(point, out var series) ? series : [];

    private readonly record struct Sample(long UnixMs, double Value);
}
