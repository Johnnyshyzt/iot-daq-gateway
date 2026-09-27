namespace IotDaq.Persistence.Rules;

public sealed class RuleEpisode
{
    public long? TrueSince { get; set; }

    public long SuppressUntil { get; set; }

    public bool Holding { get; set; }

    /// <summary>
    /// Duration: the condition must stay true for <paramref name="durationMs"/> before a fire.
    /// A false sample clears the timer. Debounce: after a fire, further fires wait
    /// <paramref name="debounceMs"/> even if the condition drops and returns.
    /// </summary>
    public bool Step(bool condition, long nowMs, long durationMs, long debounceMs)
    {
        if (!condition)
        {
            TrueSince = null;
            Holding = false;
            return false;
        }

        TrueSince ??= nowMs;
        if (nowMs - TrueSince.Value < Math.Max(0, durationMs))
        {
            return false;
        }

        if (Holding || nowMs < SuppressUntil)
        {
            return false;
        }

        Holding = true;
        SuppressUntil = nowMs + Math.Max(0, debounceMs);
        return true;
    }
}

public sealed class HistoryFrame
{
    public long UnixMs { get; init; }

    public Dictionary<string, double?> Numbers { get; init; } = new(StringComparer.OrdinalIgnoreCase);

    public Dictionary<string, string?> Texts { get; init; } = new(StringComparer.OrdinalIgnoreCase);
}

public sealed class BacktestHit
{
    public long UnixMs { get; init; }

    public bool Fired { get; init; }

    public bool Condition { get; init; }

    public double? Value { get; init; }

    public string? Error { get; init; }
}

public static class RuleRuntime
{
    public static IReadOnlyList<BacktestHit> Backtest(
        ExpressionProgram program,
        long durationMs,
        long debounceMs,
        IReadOnlyList<HistoryFrame> frames)
    {
        var memory = new PointMemory();
        var episode = new RuleEpisode();
        var hits = new List<BacktestHit>();
        foreach (var frame in frames.OrderBy(item => item.UnixMs))
        {
            foreach (var pair in frame.Numbers)
            {
                if (pair.Value is double number)
                {
                    memory.Observe(pair.Key, frame.UnixMs, number);
                }
            }

            var value = ExpressionEngine.Evaluate(program, new EvalContext
            {
                Memory = memory,
                Numbers = frame.Numbers,
                Texts = frame.Texts,
                NowMs = frame.UnixMs,
                DurationScope = "backtest"
            });
            var condition = value.Ok && value.Truthy;
            var fired = value.Ok && episode.Step(condition, frame.UnixMs, durationMs, debounceMs);
            if (fired || !value.Ok)
            {
                hits.Add(new BacktestHit
                {
                    UnixMs = frame.UnixMs,
                    Fired = fired,
                    Condition = condition,
                    Value = value.Number,
                    Error = value.Error
                });
            }
        }

        return hits;
    }
}
