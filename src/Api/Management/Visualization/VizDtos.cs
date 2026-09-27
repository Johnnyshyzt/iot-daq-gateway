using IotDaq.Persistence;
using IotDaq.Persistence.Visualization;

namespace Studio.Host.Visualization;

public sealed class DashboardSnapshot
{
    public long GeneratedUnixMs { get; set; }

    public Dictionary<string, int> Counts { get; set; } = [];

    public double TodayUtilization { get; set; }

    public double TodayPartCount { get; set; }

    public int ActiveAlarmCount { get; set; }

    public List<DashboardGroup> Groups { get; set; } = [];
}

public sealed class DashboardGroup
{
    public string Workshop { get; set; } = "";

    public List<DashboardLine> Lines { get; set; } = [];
}

public sealed class DashboardLine
{
    public string Line { get; set; } = "";

    public List<DashboardDevice> Devices { get; set; } = [];
}

public sealed class DashboardDevice
{
    public string Id { get; set; } = "";

    public string DisplayName { get; set; } = "";

    public string Workshop { get; set; } = "";

    public string Line { get; set; } = "";

    public string Adapter { get; set; } = "";

    public string State { get; set; } = "";

    public string StateLabel { get; set; } = "";

    public string? Program { get; set; }

    public double PartCount { get; set; }

    public double Utilization { get; set; }

    public double? SpindleSpeed { get; set; }

    public double? SpindleLoad { get; set; }

    public double? FeedRate { get; set; }

    public int ActiveAlarms { get; set; }

    public string Connection { get; set; } = "";

    public string ConnectionMessage { get; set; } = "";
}

public sealed class DeviceDetail
{
    public string Id { get; set; } = "";

    public string DisplayName { get; set; } = "";

    public string Workshop { get; set; } = "";

    public string Line { get; set; } = "";

    public string Adapter { get; set; } = "";

    public bool Enabled { get; set; }

    public string Host { get; set; } = "";

    public int Port { get; set; }

    public string State { get; set; } = "";

    public string StateLabel { get; set; } = "";

    public string Connection { get; set; } = "";

    public string ConnectionMessage { get; set; } = "";

    public string LinkPhase { get; set; } = "";

    public long? NextRetryUnixMs { get; set; }

    public string LastError { get; set; } = "";

    public int LinkAttempt { get; set; }

    public long? LastSeenUnixMs { get; set; }

    public string? Program { get; set; }

    public double? ProgramLine { get; set; }

    public string? WorkMode { get; set; }

    public double? PartCount { get; set; }

    public double? PartCountTotal { get; set; }

    public double? SpindleSpeed { get; set; }

    public double? SpindleLoad { get; set; }

    public double? SpindleOverride { get; set; }

    public double? FeedRate { get; set; }

    public double? FeedOverride { get; set; }

    public double? RapidOverride { get; set; }

    public double? AxisX { get; set; }

    public double? AxisY { get; set; }

    public double? AxisZ { get; set; }

    public string? AlarmText { get; set; }

    public double TodayUtilization { get; set; }

    public double TodayPartCount { get; set; }

    public long TimelineFromUnixMs { get; set; }

    public long TimelineToUnixMs { get; set; }

    public List<TimelinePiece> Timeline { get; set; } = [];

    public List<AlarmView> ActiveAlarms { get; set; } = [];

    public List<DeviceEvent> RecentEvents { get; set; } = [];

    public IReadOnlyList<SampleView> Latest { get; set; } = [];
}

public sealed class TimelinePiece
{
    public string State { get; set; } = "";

    public string Label { get; set; } = "";

    public long StartedUnixMs { get; set; }

    public long EndedUnixMs { get; set; }
}

public sealed class DeviceEvent
{
    public long TimestampUnixMs { get; set; }

    public string Kind { get; set; } = "";

    public string Message { get; set; } = "";
}

public sealed class SeriesResponse
{
    public long BucketMs { get; set; }

    public long FromUnixMs { get; set; }

    public long ToUnixMs { get; set; }

    public List<SeriesPoint> Series { get; set; } = [];
}

public sealed class UtilizationResponse
{
    public long FromUnixMs { get; set; }

    public long ToUnixMs { get; set; }

    public ShiftCalendar Calendar { get; set; } = new();

    public List<UtilizationRow> Shifts { get; set; } = [];

    public List<UtilizationRow> Days { get; set; } = [];

    public List<UtilizationRow> Lines { get; set; } = [];
}

public sealed class VizSettings
{
    public int HistoryRetentionDays { get; set; } = 14;

    public string TimeZone { get; set; } = "Asia/Shanghai";

    public List<ShiftDefinition> Shifts { get; set; } = [];
}
