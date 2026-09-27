using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using IotDaq.Persistence;
using IotDaq.Persistence.Visualization;

namespace Studio.Host.Notifications;

public sealed class AlarmNotice
{
    public string AlarmId { get; init; } = "";

    public string DeviceId { get; init; } = "";

    public string Workshop { get; init; } = "";

    public string Line { get; init; } = "";

    public string Severity { get; init; } = "";

    public string Code { get; init; } = "";

    public string Message { get; init; } = "";

    public string Kind { get; init; } = "raise";

    public bool Active { get; init; }

    public bool Acknowledged { get; init; }

    public DateTimeOffset Raised { get; init; }
}

public static class NotificationRules
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public static bool Matches(NotificationRuleRow rule, AlarmNotice notice)
    {
        if (!rule.Enabled)
        {
            return false;
        }

        if (notice.Kind == "raise" && !rule.OnRaise)
        {
            return false;
        }

        if (notice.Kind == "clear" && !rule.OnClear)
        {
            return false;
        }

        var devices = ReadList(rule.DeviceIdsJson);
        if (devices.Count > 0 && !devices.Contains(notice.DeviceId, StringComparer.OrdinalIgnoreCase))
        {
            return false;
        }

        var groups = ReadList(rule.GroupsJson);
        if (groups.Count > 0
            && !groups.Contains(notice.Workshop, StringComparer.OrdinalIgnoreCase)
            && !groups.Contains(notice.Line, StringComparer.OrdinalIgnoreCase))
        {
            return false;
        }

        var severities = ReadList(rule.SeveritiesJson);
        if (severities.Count > 0 && !severities.Contains(notice.Severity, StringComparer.OrdinalIgnoreCase))
        {
            return false;
        }

        return CodeMatches(rule.CodeFilter, notice.Code);
    }

    public static bool InQuietHours(string? start, string? end, DateTimeOffset utc, TimeZoneInfo zone)
    {
        if (!ShiftCalendar.TryClock(start, out var startMin) || !ShiftCalendar.TryClock(end, out var endMin) || startMin == endMin)
        {
            return false;
        }

        var local = TimeZoneInfo.ConvertTime(utc, zone);
        var minutes = local.Hour * 60 + local.Minute;
        if (startMin < endMin)
        {
            return minutes >= startMin && minutes < endMin;
        }

        return minutes >= startMin || minutes < endMin;
    }

    public static bool AllowSend(int recentSame, int dedupSeconds, int sentThisHour, int ratePerHour)
    {
        if (dedupSeconds > 0 && recentSame > 0)
        {
            return false;
        }

        if (ratePerHour > 0 && sentThisHour >= ratePerHour)
        {
            return false;
        }

        return true;
    }

    public static bool ShouldEscalate(
        NotificationRuleRow rule,
        AlarmNotice notice,
        DateTimeOffset now,
        bool alreadyEscalated,
        bool quiet)
    {
        if (!rule.Enabled || rule.EscalationMinutes <= 0 || string.IsNullOrWhiteSpace(rule.EscalationChannelId))
        {
            return false;
        }

        if (!notice.Active || notice.Acknowledged || alreadyEscalated || quiet)
        {
            return false;
        }

        if (!string.Equals(notice.Kind, "raise", StringComparison.Ordinal))
        {
            return false;
        }

        return now >= notice.Raised.AddMinutes(rule.EscalationMinutes);
    }

    public static bool CodeMatches(string? filter, string? code)
    {
        if (string.IsNullOrWhiteSpace(filter))
        {
            return true;
        }

        var value = code ?? "";
        foreach (var part in filter.Split([',', ';', '\n', ' '], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (part.Contains('*', StringComparison.Ordinal))
            {
                var pattern = "^" + Regex.Escape(part).Replace("\\*", ".*", StringComparison.Ordinal) + "$";
                if (Regex.IsMatch(value, pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
                {
                    return true;
                }
            }
            else if (value.Contains(part, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    public static List<string> ReadList(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return [];
        }

        try
        {
            return JsonSerializer.Deserialize<List<string>>(json, Json)?
                .Where(item => !string.IsNullOrWhiteSpace(item))
                .Select(item => item.Trim())
                .ToList() ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    public static string WriteList(IEnumerable<string>? values)
    {
        var list = (values ?? [])
            .Where(item => !string.IsNullOrWhiteSpace(item))
            .Select(item => item.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        return JsonSerializer.Serialize(list, Json);
    }
}

public static class ReportScheduleMath
{
    public static string? DueDaily(ReportScheduleRow schedule, DateTimeOffset utc, TimeZoneInfo zone)
    {
        if (!schedule.Enabled || !schedule.DailyEnabled)
        {
            return null;
        }

        if (!ShiftCalendar.TryClock(schedule.DailyTime, out var minutes))
        {
            return null;
        }

        var local = TimeZoneInfo.ConvertTime(utc, zone);
        var nowMin = local.Hour * 60 + local.Minute;
        if (nowMin < minutes)
        {
            return null;
        }

        var day = DateOnly.FromDateTime(local.DateTime).AddDays(-1);
        var key = day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        return string.Equals(key, schedule.LastDailyKey, StringComparison.Ordinal) ? null : key;
    }

    public static (string Key, DateTimeOffset From, DateTimeOffset To)? DueShift(
        ReportScheduleRow schedule,
        ShiftCalendar calendar,
        DateTimeOffset utc)
    {
        if (!schedule.Enabled || !schedule.ShiftEnabled)
        {
            return null;
        }

        ShiftWindow? due = null;
        foreach (var window in calendar.Windows(utc.AddHours(-18), utc.AddMinutes(1)))
        {
            if (window.End <= utc && window.End > utc.AddHours(-18))
            {
                due = window;
            }
        }

        if (due is null)
        {
            return null;
        }

        var key = due.Value.Day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) + "|" + due.Value.Name;
        if (string.Equals(key, schedule.LastShiftKey, StringComparison.Ordinal))
        {
            return null;
        }

        return (key, due.Value.Start, due.Value.End);
    }

    public static (DateTimeOffset From, DateTimeOffset To) PreviousLocalDay(DateTimeOffset utc, TimeZoneInfo zone)
    {
        var local = TimeZoneInfo.ConvertTime(utc, zone);
        var startLocal = DateOnly.FromDateTime(local.DateTime).AddDays(-1).ToDateTime(TimeOnly.MinValue);
        var endLocal = DateOnly.FromDateTime(local.DateTime).ToDateTime(TimeOnly.MinValue);
        return (ToOffset(startLocal, zone), ToOffset(endLocal, zone));
    }

    private static DateTimeOffset ToOffset(DateTime local, TimeZoneInfo zone)
    {
        var unspecified = DateTime.SpecifyKind(local, DateTimeKind.Unspecified);
        return new DateTimeOffset(unspecified, zone.GetUtcOffset(unspecified));
    }
}
