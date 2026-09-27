namespace Gateway.Host.Reliability;

/// <summary>
/// Exponential reconnect delay with a hard cap and symmetric jitter.
/// <paramref name="jitterUnit"/> is in [0, 1]. 0.5 leaves the delay unchanged.
/// The result never exceeds <paramref name="cap"/>.
/// </summary>
public static class ReconnectBackoff
{
    public static TimeSpan Delay(
        int attempt,
        TimeSpan initial,
        double multiplier,
        TimeSpan cap,
        double jitterRatio,
        double jitterUnit)
    {
        if (attempt < 1)
        {
            attempt = 1;
        }

        if (initial < TimeSpan.Zero)
        {
            initial = TimeSpan.Zero;
        }

        if (cap < TimeSpan.Zero)
        {
            cap = TimeSpan.Zero;
        }

        multiplier = Math.Clamp(multiplier, 1, 10);
        jitterRatio = Math.Clamp(jitterRatio, 0, 1);
        jitterUnit = Math.Clamp(jitterUnit, 0, 1);

        var steps = Math.Min(attempt - 1, 16);
        var raw = initial.TotalMilliseconds * Math.Pow(multiplier, steps);
        if (double.IsNaN(raw) || double.IsInfinity(raw))
        {
            raw = cap.TotalMilliseconds;
        }

        var capped = Math.Min(Math.Max(0, raw), cap.TotalMilliseconds);
        var jitter = (jitterUnit * 2 - 1) * jitterRatio;
        var withJitter = capped * (1 + jitter);
        var ms = Math.Clamp(withJitter, 0, cap.TotalMilliseconds);
        return TimeSpan.FromMilliseconds(ms);
    }
}
