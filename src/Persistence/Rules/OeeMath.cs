namespace IotDaq.Persistence.Rules;

/// <summary>
/// OEE = Availability × Performance × Quality.
/// Availability = run time / planned production time.
/// Planned production time is the shift plan after breaks and holidays, minus planned stops.
/// Performance = (ideal cycle seconds × total parts) / run seconds.
/// Quality = good parts / total parts. Good parts = total − scrap.
/// A zero plan yields OEE 0 (noPlannedTime). A missing ideal cycle leaves performance and OEE empty (missingIdealCycle).
/// </summary>
public static class OeeMath
{
    public static OeeFactors Compute(OeeInput input)
    {
        var quality = QualityOf(input.TotalParts, input.ScrapParts);
        var good = GoodOf(input.TotalParts, input.ScrapParts);
        if (input.PlannedMs <= 0)
        {
            return new OeeFactors
            {
                Availability = 0,
                Performance = null,
                Quality = quality,
                Oee = 0,
                Flag = "noPlannedTime",
                GoodParts = good
            };
        }

        var availability = Math.Round(input.RunMs / (double)input.PlannedMs, 4);
        if (input.IdealCycleSeconds is null || input.IdealCycleSeconds <= 0)
        {
            return new OeeFactors
            {
                Availability = availability,
                Performance = null,
                Quality = quality,
                Oee = null,
                Flag = "missingIdealCycle",
                GoodParts = good
            };
        }

        double? performance;
        var flag = "";
        if (input.RunMs <= 0)
        {
            performance = input.TotalParts <= 0 ? 0 : null;
            if (performance is null)
            {
                flag = "noRunTime";
            }
        }
        else
        {
            var runSeconds = input.RunMs / 1000d;
            performance = Math.Round(input.IdealCycleSeconds.Value * Math.Max(0, input.TotalParts) / runSeconds, 4);
        }

        double? oee = performance is null
            ? null
            : Math.Round(Clamp(availability) * Clamp(performance.Value) * Clamp(quality), 4);
        return new OeeFactors
        {
            Availability = availability,
            Performance = performance,
            Quality = quality,
            Oee = oee,
            Flag = flag,
            GoodParts = good
        };
    }

    public static long PlannedProduction(long calendarPlannedMs, long deductionMs) =>
        Math.Max(0, calendarPlannedMs - Math.Max(0, deductionMs));

    public static long MergedOverlap(long start, long end, IReadOnlyList<(long Start, long End)> intervals)
    {
        if (end <= start || intervals.Count == 0)
        {
            return 0;
        }

        var clipped = intervals
            .Select(item => (Start: Math.Max(item.Start, start), End: Math.Min(item.End, end)))
            .Where(item => item.End > item.Start)
            .OrderBy(item => item.Start)
            .ToList();
        if (clipped.Count == 0)
        {
            return 0;
        }

        long total = 0;
        var cursor = clipped[0].Start;
        var until = clipped[0].End;
        for (var i = 1; i < clipped.Count; i++)
        {
            if (clipped[i].Start <= until)
            {
                until = Math.Max(until, clipped[i].End);
                continue;
            }

            total += until - cursor;
            cursor = clipped[i].Start;
            until = clipped[i].End;
        }

        return total + (until - cursor);
    }

    public static List<WaterfallStep> Waterfall(WaterfallInput input)
    {
        var steps = new List<WaterfallStep>();
        var cursor = input.CalendarMs;
        Add("日历时间", input.CalendarMs, "total");
        Add("班次外", input.OutsideShiftMs, "loss");
        Add("休息、节假日和计划停机", input.PlannedDeductionMs, "loss");
        Add("故障", input.AlarmMs, "loss");
        Add("换型", input.SetupMs, "loss");
        Add("待料", input.WaitingMs, "loss");
        Add("空闲", input.IdleMs, "loss");
        Add("离线", input.OfflineMs, "loss");
        Add("其他停机", input.OtherStopMs, "loss");
        var performanceLoss = 0L;
        var qualityLoss = 0L;
        if (input.IdealCycleSeconds is > 0 && input.RunMs > 0)
        {
            var earned = (long)Math.Round(input.IdealCycleSeconds.Value * 1000d * Math.Max(0, input.TotalParts));
            performanceLoss = Math.Max(0, input.RunMs - earned);
            qualityLoss = (long)Math.Round(input.IdealCycleSeconds.Value * 1000d * Math.Max(0, input.ScrapParts));
            qualityLoss = Math.Min(qualityLoss, Math.Max(0, earned));
        }

        Add("性能损失", performanceLoss, "loss");
        Add("质量损失", qualityLoss, "loss");
        var effective = Math.Max(0, input.RunMs - performanceLoss - qualityLoss);
        steps.Add(new WaterfallStep { Name = "有效时间", OffsetMs = 0, ValueMs = effective, Kind = "result" });
        return steps;

        void Add(string name, long value, string kind)
        {
            var amount = Math.Max(0, value);
            if (kind == "loss")
            {
                cursor = Math.Max(0, cursor - amount);
                steps.Add(new WaterfallStep { Name = name, OffsetMs = cursor, ValueMs = amount, Kind = kind });
                return;
            }

            steps.Add(new WaterfallStep { Name = name, OffsetMs = 0, ValueMs = amount, Kind = kind });
        }
    }

    public static List<ParetoBar> Pareto(IEnumerable<(string Reason, long Ms)> items)
    {
        var ordered = items
            .GroupBy(item => string.IsNullOrWhiteSpace(item.Reason) ? "未说明" : item.Reason.Trim(), StringComparer.Ordinal)
            .Select(group => (Reason: group.Key, Ms: group.Sum(item => Math.Max(0, item.Ms))))
            .Where(item => item.Ms > 0)
            .OrderByDescending(item => item.Ms)
            .ToList();
        var total = ordered.Sum(item => item.Ms);
        long cumulative = 0;
        var bars = new List<ParetoBar>();
        foreach (var item in ordered)
        {
            cumulative += item.Ms;
            bars.Add(new ParetoBar
            {
                Reason = item.Reason,
                DurationMs = item.Ms,
                Share = total <= 0 ? 0 : Math.Round(item.Ms / (double)total, 4),
                Cumulative = total <= 0 ? 0 : Math.Round(cumulative / (double)total, 4)
            });
        }

        return bars;
    }

    public static double? ResolveIdeal(IEnumerable<CycleSpec> cycles, string deviceId, string? program)
    {
        var rows = cycles.Where(row =>
            string.Equals(row.Scope, "device", StringComparison.OrdinalIgnoreCase)
            && string.Equals(row.OwnerId, deviceId, StringComparison.OrdinalIgnoreCase)
            && row.IdealSeconds > 0).ToList();
        if (!string.IsNullOrWhiteSpace(program))
        {
            var match = rows.FirstOrDefault(row => string.Equals(row.Program, program, StringComparison.OrdinalIgnoreCase));
            if (match is not null)
            {
                return match.IdealSeconds;
            }
        }

        return rows.FirstOrDefault(row => string.IsNullOrWhiteSpace(row.Program))?.IdealSeconds;
    }

    private static double GoodOf(double total, double scrap) =>
        total <= 0 ? 0 : Math.Max(0, total - Math.Max(0, scrap));

    private static double QualityOf(double total, double scrap)
    {
        if (total <= 0)
        {
            return scrap > 0 ? 0 : 1;
        }

        return Math.Round(Math.Max(0, total - Math.Max(0, scrap)) / total, 4);
    }

    private static double Clamp(double value) => Math.Clamp(value, 0, 1);
}

public sealed class OeeInput
{
    public long PlannedMs { get; init; }

    public long RunMs { get; init; }

    public double TotalParts { get; init; }

    public double ScrapParts { get; init; }

    public double? IdealCycleSeconds { get; init; }
}

public sealed class OeeFactors
{
    public double Availability { get; init; }

    public double? Performance { get; init; }

    public double Quality { get; init; }

    public double? Oee { get; init; }

    public string Flag { get; init; } = "";

    public double GoodParts { get; init; }
}

public sealed class WaterfallInput
{
    public long CalendarMs { get; init; }

    public long OutsideShiftMs { get; init; }

    public long PlannedDeductionMs { get; init; }

    public long AlarmMs { get; init; }

    public long SetupMs { get; init; }

    public long WaitingMs { get; init; }

    public long IdleMs { get; init; }

    public long OfflineMs { get; init; }

    public long OtherStopMs { get; init; }

    public long RunMs { get; init; }

    public double TotalParts { get; init; }

    public double ScrapParts { get; init; }

    public double? IdealCycleSeconds { get; init; }
}

public sealed class WaterfallStep
{
    public string Name { get; set; } = "";

    public long OffsetMs { get; set; }

    public long ValueMs { get; set; }

    public string Kind { get; set; } = "";
}

public sealed class ParetoBar
{
    public string Reason { get; set; } = "";

    public long DurationMs { get; set; }

    public double Share { get; set; }

    public double Cumulative { get; set; }
}

public sealed class CycleSpec
{
    public string Scope { get; set; } = "device";

    public string OwnerId { get; set; } = "";

    public string Program { get; set; } = "";

    public double IdealSeconds { get; set; }
}
