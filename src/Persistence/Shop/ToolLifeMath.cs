namespace IotDaq.Persistence.Shop;

public sealed class ToolLimits
{
    public double? Count { get; init; }

    public double? CuttingMinutes { get; init; }

    public double WarningPercent { get; init; } = 80;
}

public sealed class ToolCursor
{
    public string ToolNumber { get; set; } = "";

    public double? LastPartCount { get; set; }

    public double? LastCycleSeconds { get; set; }

    public long LastUnixMs { get; set; }

    public bool WasCutting { get; set; }
}

public sealed class ToolLifeTotals
{
    public double UsedCount { get; set; }

    public double UsedCuttingMs { get; set; }

    public string Source { get; set; } = "";

    public string Level { get; set; } = "ok";
}

public readonly record struct ToolSignal(
    string? ToolNumber,
    double? PartCount,
    bool Cutting,
    double? CycleSeconds,
    long UnixMs,
    string Source);

public sealed class ToolAdvance
{
    public string ToolNumber { get; init; } = "";

    public double CountDelta { get; init; }

    public double CuttingMs { get; init; }

    public double UsedCount { get; init; }

    public double UsedCuttingMs { get; init; }

    public string Level { get; init; } = "ok";

    public string Source { get; init; } = "";

    public bool Switched { get; init; }

    public bool Updated { get; init; }
}

/// <summary>
/// Counts tool life from points the drivers already expose: tool number, part count,
/// running state, and an optional cycle-time signal. The first sample after a tool
/// change only sets the baseline, so a cumulative part counter is not assigned to the new tool.
/// </summary>
public static class ToolLifeMath
{
    public const long MaxGapMs = 120_000;

    public static string NormalizeTool(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return "";
        }

        var text = raw.Trim();
        if (text is "0" or "T0" or "T00" or "无" or "-")
        {
            return "";
        }

        return text;
    }

    public static bool IsCutting(string? state, string? running)
    {
        if (!string.IsNullOrWhiteSpace(running))
        {
            if (running.Equals("1", StringComparison.Ordinal) || running.Equals("true", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (running.Equals("0", StringComparison.Ordinal) || running.Equals("false", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
        }

        if (string.IsNullOrWhiteSpace(state))
        {
            return false;
        }

        return state.Equals("RUNNING", StringComparison.OrdinalIgnoreCase)
            || state.Equals("CYCLE", StringComparison.OrdinalIgnoreCase)
            || state.Equals("运行", StringComparison.Ordinal)
            || state.Equals("加工", StringComparison.Ordinal);
    }

    public static ToolAdvance Advance(ToolCursor cursor, ToolLifeTotals life, ToolLimits limits, ToolSignal signal)
    {
        var tool = NormalizeTool(signal.ToolNumber);
        var switched = !string.Equals(tool, cursor.ToolNumber ?? "", StringComparison.OrdinalIgnoreCase);
        double countDelta = 0;
        double cutting = 0;
        if (!switched && tool.Length > 0)
        {
            if (signal.PartCount is double part && cursor.LastPartCount is double previous && part >= previous)
            {
                countDelta = part - previous;
            }

            if (signal.Cutting && cursor.WasCutting && cursor.LastUnixMs > 0)
            {
                var gap = signal.UnixMs - cursor.LastUnixMs;
                if (gap > 0 && gap <= MaxGapMs)
                {
                    cutting += gap;
                }
            }

            if (signal.CycleSeconds is double cycle && cursor.LastCycleSeconds is double lastCycle && cycle > lastCycle)
            {
                var extra = (cycle - lastCycle) * 1000d;
                if (extra > 0 && extra <= MaxGapMs)
                {
                    cutting += extra;
                }
            }
        }

        cursor.ToolNumber = tool;
        if (signal.PartCount is double seenPart)
        {
            cursor.LastPartCount = seenPart;
        }

        if (signal.CycleSeconds is double seenCycle)
        {
            cursor.LastCycleSeconds = seenCycle;
        }

        cursor.LastUnixMs = signal.UnixMs;
        cursor.WasCutting = signal.Cutting && tool.Length > 0;

        if (tool.Length == 0)
        {
            return new ToolAdvance { Switched = switched };
        }

        life.UsedCount += countDelta;
        life.UsedCuttingMs += cutting;
        life.Source = MergeSource(life.Source, signal.Source, countDelta > 0 || cutting > 0);
        life.Level = Level(limits, life.UsedCount, life.UsedCuttingMs);
        return new ToolAdvance
        {
            ToolNumber = tool,
            CountDelta = countDelta,
            CuttingMs = cutting,
            UsedCount = life.UsedCount,
            UsedCuttingMs = life.UsedCuttingMs,
            Level = life.Level,
            Source = life.Source,
            Switched = switched,
            Updated = true
        };
    }

    public static string Level(ToolLimits limits, double usedCount, double usedMs)
    {
        var ratios = new List<double>();
        if (limits.Count is > 0)
        {
            ratios.Add(usedCount / limits.Count.Value);
        }

        if (limits.CuttingMinutes is > 0)
        {
            ratios.Add(usedMs / 60000d / limits.CuttingMinutes.Value);
        }

        if (ratios.Count == 0)
        {
            return "ok";
        }

        var ratio = ratios.Max();
        if (ratio >= 1d)
        {
            return "eol";
        }

        var warn = limits.WarningPercent <= 0 ? 80d : limits.WarningPercent;
        return ratio >= warn / 100d ? "warning" : "ok";
    }

    public static void Reset(ToolLifeTotals life)
    {
        life.UsedCount = 0;
        life.UsedCuttingMs = 0;
        life.Level = "ok";
    }

    private static string MergeSource(string current, string incoming, bool moved)
    {
        if (!moved)
        {
            return current;
        }

        if (current is "manual" or "mixed" || incoming == "manual")
        {
            return incoming == "manual" && current.Length == 0 ? "manual" : "mixed";
        }

        if (incoming == "computed")
        {
            return "computed";
        }

        return string.IsNullOrEmpty(current) ? "points" : current;
    }
}
