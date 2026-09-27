using System.Globalization;
using System.Text.Json;

namespace IotDaq.Persistence.Visualization;

public sealed class ShiftCalendar
{
    public string TimeZone { get; set; } = "Asia/Shanghai";

    public List<ShiftDefinition> Shifts { get; set; } = [];

    public List<PlannedBreak> Breaks { get; set; } = [];

    public List<string> Holidays { get; set; } = [];

    public static ShiftCalendar Default() => new()
    {
        TimeZone = "Asia/Shanghai",
        Shifts =
        [
            new ShiftDefinition { Name = "白班", Start = "08:00", End = "20:00", PlannedMinutes = 660 },
            new ShiftDefinition { Name = "夜班", Start = "20:00", End = "08:00", PlannedMinutes = 660 }
        ]
    };

    public static ShiftCalendar ParseOrDefault(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return Default();
        }

        try
        {
            var parsed = JsonSerializer.Deserialize<ShiftCalendar>(json, new JsonSerializerOptions(JsonSerializerDefaults.Web));
            if (parsed is null || parsed.Shifts.Count == 0)
            {
                return Default();
            }

            parsed.TimeZone = string.IsNullOrWhiteSpace(parsed.TimeZone) ? "Asia/Shanghai" : parsed.TimeZone.Trim();
            parsed.Breaks ??= [];
            parsed.Holidays ??= [];
            return parsed;
        }
        catch (JsonException)
        {
            return Default();
        }
    }

    public string ToJson() => JsonSerializer.Serialize(this, new JsonSerializerOptions(JsonSerializerDefaults.Web));

    public TimeZoneInfo ResolveZone()
    {
        var id = string.IsNullOrWhiteSpace(TimeZone) ? "Asia/Shanghai" : TimeZone.Trim();
        foreach (var candidate in Candidates(id))
        {
            try
            {
                return TimeZoneInfo.FindSystemTimeZoneById(candidate);
            }
            catch (TimeZoneNotFoundException)
            {
            }
            catch (InvalidTimeZoneException)
            {
            }
        }

        return TimeZoneInfo.Utc;
    }

    public IReadOnlyList<string> Validate()
    {
        var issues = new List<string>();
        if (Shifts.Count == 0)
        {
            issues.Add("至少配置一个班次");
            return issues;
        }

        var windows = new List<(string Name, int Start, int End)>();
        foreach (var shift in Shifts)
        {
            if (string.IsNullOrWhiteSpace(shift.Name))
            {
                issues.Add("班次名称不能为空");
                continue;
            }

            if (!TryClock(shift.Start, out var start) || !TryClock(shift.End, out var end))
            {
                issues.Add($"班次「{shift.Name}」的起止时间需为 HH:mm");
                continue;
            }

            if (start == end)
            {
                issues.Add($"班次「{shift.Name}」的开始和结束不能相同");
                continue;
            }

            var length = end > start ? end - start : end + 1440 - start;
            if (shift.PlannedMinutes < 0 || shift.PlannedMinutes > length)
            {
                issues.Add($"班次「{shift.Name}」的计划时间需在 0 到 {length} 分钟之间");
            }

            windows.Add((shift.Name.Trim(), start, end > start ? end : end + 1440));
        }

        foreach (var item in Breaks)
        {
            if (string.IsNullOrWhiteSpace(item.Name))
            {
                issues.Add("休息名称不能为空");
                continue;
            }

            if (!TryClock(item.Start, out var start) || !TryClock(item.End, out var end) || start == end)
            {
                issues.Add($"休息「{item.Name}」的起止时间需为 HH:mm，且不能相同");
            }
        }

        foreach (var holiday in Holidays)
        {
            if (!DateOnly.TryParseExact(holiday?.Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _))
            {
                issues.Add($"节假日「{holiday}」需为 yyyy-MM-dd");
            }
        }

        for (var i = 0; i < windows.Count; i++)
        {
            for (var j = i + 1; j < windows.Count; j++)
            {
                if (Overlaps(windows[i].Start, windows[i].End, windows[j].Start, windows[j].End)
                    || Overlaps(windows[i].Start, windows[i].End, windows[j].Start + 1440, windows[j].End + 1440)
                    || Overlaps(windows[i].Start + 1440, windows[i].End + 1440, windows[j].Start, windows[j].End))
                {
                    issues.Add($"班次「{windows[i].Name}」与「{windows[j].Name}」时间重叠");
                }
            }
        }

        return issues;
    }

    public IReadOnlyList<ShiftWindow> Windows(DateTimeOffset from, DateTimeOffset to)
    {
        if (to <= from || Shifts.Count == 0)
        {
            return [];
        }

        var zone = ResolveZone();
        var localFrom = TimeZoneInfo.ConvertTime(from, zone);
        var localTo = TimeZoneInfo.ConvertTime(to, zone);
        var day = DateOnly.FromDateTime(localFrom.DateTime).AddDays(-1);
        var last = DateOnly.FromDateTime(localTo.DateTime).AddDays(1);
        var windows = new List<ShiftWindow>();
        for (var cursor = day; cursor <= last; cursor = cursor.AddDays(1))
        {
            foreach (var shift in Shifts)
            {
                if (!TryClock(shift.Start, out var startMin) || !TryClock(shift.End, out var endMin))
                {
                    continue;
                }

                var startLocal = cursor.ToDateTime(TimeOnly.MinValue).AddMinutes(startMin);
                var endLocal = cursor.ToDateTime(TimeOnly.MinValue).AddMinutes(endMin);
                if (endMin <= startMin)
                {
                    endLocal = endLocal.AddDays(1);
                }

                var start = ToOffset(startLocal, zone);
                var end = ToOffset(endLocal, zone);
                if (end <= from || start >= to)
                {
                    continue;
                }

                var lengthMs = (long)(end - start).TotalMilliseconds;
                var planned = PlannedMilliseconds(cursor, start, end, lengthMs, shift);
                windows.Add(new ShiftWindow(shift.Name.Trim(), cursor, start, end, lengthMs, planned));
            }
        }

        return windows.OrderBy(window => window.Start).ToList();
    }

    private long PlannedMilliseconds(DateOnly day, DateTimeOffset start, DateTimeOffset end, long lengthMs, ShiftDefinition shift)
    {
        if (IsHoliday(day))
        {
            return 0;
        }

        if (Breaks.Count == 0)
        {
            return Math.Clamp(shift.PlannedMinutes, 0, (int)Math.Max(0, lengthMs / 60000)) * 60_000L;
        }

        long breakMs = 0;
        foreach (var item in Breaks)
        {
            if (!TryClock(item.Start, out var breakStart) || !TryClock(item.End, out var breakEnd))
            {
                continue;
            }

            breakMs += BreakOverlap(day, start, end, breakStart, breakEnd);
        }

        return Math.Max(0, lengthMs - breakMs);
    }

    public bool IsHoliday(DateOnly day) =>
        Holidays.Any(holiday => DateOnly.TryParseExact(holiday?.Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed) && parsed == day);

    private long BreakOverlap(DateOnly day, DateTimeOffset shiftStart, DateTimeOffset shiftEnd, int breakStartMin, int breakEndMin)
    {
        var zone = ResolveZone();
        long total = 0;
        foreach (var offsetDay in new[] { -1, 0, 1 })
        {
            var date = day.AddDays(offsetDay);
            var breakStart = ToOffset(date.ToDateTime(TimeOnly.MinValue).AddMinutes(breakStartMin), zone);
            var breakEnd = ToOffset(date.ToDateTime(TimeOnly.MinValue).AddMinutes(breakEndMin), zone);
            if (breakEndMin <= breakStartMin)
            {
                breakEnd = breakEnd.AddDays(1);
            }

            var from = breakStart > shiftStart ? breakStart : shiftStart;
            var to = breakEnd < shiftEnd ? breakEnd : shiftEnd;
            if (to > from)
            {
                total += (long)(to - from).TotalMilliseconds;
            }
        }

        return total;
    }

    private static bool Overlaps(int startA, int endA, int startB, int endB) => startA < endB && startB < endA;

    public static bool TryClock(string? text, out int minutes)
    {
        minutes = 0;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var parts = text.Trim().Split(':');
        if (parts.Length != 2
            || !int.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out var hour)
            || !int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var minute)
            || hour is < 0 or > 23
            || minute is < 0 or > 59)
        {
            return false;
        }

        minutes = hour * 60 + minute;
        return true;
    }

    private static DateTimeOffset ToOffset(DateTime local, TimeZoneInfo zone)
    {
        var unspecified = DateTime.SpecifyKind(local, DateTimeKind.Unspecified);
        var offset = zone.GetUtcOffset(unspecified);
        return new DateTimeOffset(unspecified, offset);
    }

    private static IEnumerable<string> Candidates(string id)
    {
        yield return id;
        if (id.Equals("Asia/Shanghai", StringComparison.OrdinalIgnoreCase))
        {
            yield return "China Standard Time";
        }

        if (id.Equals("China Standard Time", StringComparison.OrdinalIgnoreCase))
        {
            yield return "Asia/Shanghai";
        }
    }
}

public sealed class ShiftDefinition
{
    public string Name { get; set; } = "";

    public string Start { get; set; } = "08:00";

    public string End { get; set; } = "20:00";

    public int PlannedMinutes { get; set; } = 660;
}

public sealed class PlannedBreak
{
    public string Name { get; set; } = "";

    public string Start { get; set; } = "12:00";

    public string End { get; set; } = "13:00";
}

public readonly record struct ShiftWindow(
    string Name,
    DateOnly Day,
    DateTimeOffset Start,
    DateTimeOffset End,
    long LengthMs,
    long PlannedMs);
