namespace IotDaq.Licensing;

public sealed class ClockSnapshot
{
    public bool Present { get; init; }

    public bool MacValid { get; init; }

    public long? LastSeenUnixMs { get; init; }
}

public sealed class ClockDecision
{
    public bool Rollback { get; init; }

    public bool Tampered { get; init; }

    public long LastSeenUnixMs { get; init; }

    /// <summary>Empty, <c>clock_rollback</c>, or <c>state_tamper</c>.</summary>
    public string Code { get; init; } = "";
}

/// <summary>
/// Monotonic last-seen clock. A reading earlier than the stored time by more than the tolerance is a rollback.
/// An invalid MAC on a present store is tampering. The stored time never moves backwards.
/// </summary>
public static class ClockGuard
{
    public static ClockDecision Observe(long nowUnixMs, ClockSnapshot database, ClockSnapshot sidecar, int toleranceMs)
    {
        toleranceMs = Math.Max(0, toleranceMs);
        if ((database.Present && !database.MacValid) || (sidecar.Present && !sidecar.MacValid))
        {
            var kept = Math.Max(database.LastSeenUnixMs ?? nowUnixMs, Math.Max(sidecar.LastSeenUnixMs ?? nowUnixMs, nowUnixMs));
            return new ClockDecision
            {
                Tampered = true,
                Code = "state_tamper",
                LastSeenUnixMs = kept
            };
        }

        long? seen = null;
        if (database.Present && database.LastSeenUnixMs is long db)
        {
            seen = db;
        }

        if (sidecar.Present && sidecar.LastSeenUnixMs is long side)
        {
            seen = seen is long current ? Math.Max(current, side) : side;
        }

        if (seen is null)
        {
            return new ClockDecision { LastSeenUnixMs = nowUnixMs };
        }

        if (nowUnixMs + (long)toleranceMs < seen.Value)
        {
            return new ClockDecision
            {
                Rollback = true,
                Code = "clock_rollback",
                LastSeenUnixMs = seen.Value
            };
        }

        return new ClockDecision { LastSeenUnixMs = Math.Max(seen.Value, nowUnixMs) };
    }
}
