namespace IotDaq.Persistence.Visualization;

/// <summary>
/// Shop-floor states. Raw CNC words from the catalog map onto these.
/// Newer categories (setup, waiting, planned) sit beside the original five.
/// </summary>
public static class MachineState
{
    public const string Running = "running";
    public const string Idle = "idle";
    public const string Alarm = "alarm";
    public const string Offline = "offline";
    public const string Stopped = "stopped";
    public const string Setup = "setup";
    public const string Waiting = "waiting";
    public const string Planned = "planned";

    public static readonly string[] All = [Running, Idle, Alarm, Offline, Stopped, Setup, Waiting, Planned];

    public static string Normalize(string? raw)
    {
        var text = (raw ?? "").Trim();
        if (text.Length == 0)
        {
            return Offline;
        }

        var upper = text.ToUpperInvariant();
        if (upper is "RUNNING" or "RUN" or "CYCLE" or "IN_CYCLE" or "IN-CYCLE" or "CUTTING" or "START" or "AUTO")
        {
            return Running;
        }

        if (upper is "ALARM" or "ALM" or "ERROR" or "FAULT")
        {
            return Alarm;
        }

        if (upper is "OFFLINE" or "DISCONNECT" or "DISCONNECTED" or "DOWN")
        {
            return Offline;
        }

        if (upper is "STOP" or "STOPPED" or "ESTOP" or "E-STOP" or "EMERGENCY" or "POWER_OFF" or "POWEROFF")
        {
            return Stopped;
        }

        if (upper is "SETUP" or "CHANGEOVER" or "CHANGE_OVER" or "CHANGE-OVER")
        {
            return Setup;
        }

        if (upper is "WAITING" or "WAIT_MATERIAL" or "WAITING_MATERIAL" or "NO_MATERIAL")
        {
            return Waiting;
        }

        if (upper is "PLANNED" or "PLANNED_STOP" or "PLAN_STOP" or "PLANNED_DOWNTIME")
        {
            return Planned;
        }

        if (upper is "IDLE" or "READY" or "WAIT" or "STANDBY" or "HOLD" or "FEEDHOLD" or "FEED_HOLD")
        {
            return Idle;
        }

        if (text.Contains("报警", StringComparison.Ordinal) || text.Contains("告警", StringComparison.Ordinal))
        {
            return Alarm;
        }

        if (text.Contains("离线", StringComparison.Ordinal))
        {
            return Offline;
        }

        if (text.Contains("计划停机", StringComparison.Ordinal) || text.Contains("计划停止", StringComparison.Ordinal))
        {
            return Planned;
        }

        if (text.Contains("换型", StringComparison.Ordinal) || text.Contains("调机", StringComparison.Ordinal))
        {
            return Setup;
        }

        if (text.Contains("待料", StringComparison.Ordinal) || text.Contains("等料", StringComparison.Ordinal))
        {
            return Waiting;
        }

        if (text.Contains("停机", StringComparison.Ordinal) || text.Contains("急停", StringComparison.Ordinal))
        {
            return Stopped;
        }

        if (text.Contains("运行", StringComparison.Ordinal) || text.Contains("加工", StringComparison.Ordinal))
        {
            return Running;
        }

        if (text.Contains("待机", StringComparison.Ordinal) || text.Contains("空闲", StringComparison.Ordinal))
        {
            return Idle;
        }

        return Idle;
    }

    public static string Label(string? state) => Canonical(state) switch
    {
        Running => "运行",
        Idle => "待机",
        Alarm => "报警",
        Offline => "离线",
        Stopped => "停机",
        Setup => "换型",
        Waiting => "待料",
        Planned => "计划停机",
        _ => "待机"
    };

    public static string Canonical(string? state)
    {
        var text = (state ?? "").Trim().ToLowerInvariant();
        return text switch
        {
            Running => Running,
            Idle => Idle,
            Alarm or "fault" => Alarm,
            Offline => Offline,
            Stopped => Stopped,
            Setup or "changeover" => Setup,
            Waiting or "wait_material" => Waiting,
            Planned or "planned_stop" => Planned,
            _ => Normalize(text)
        };
    }

    public static bool IsStop(string? state)
    {
        var text = Canonical(state);
        return text is Idle or Alarm or Offline or Stopped or Setup or Waiting or Planned;
    }

    public static string FromConnection(string? status)
    {
        var text = (status ?? "").Trim();
        if (text.Equals("offline", StringComparison.OrdinalIgnoreCase))
        {
            return Offline;
        }

        if (text.Equals("disabled", StringComparison.OrdinalIgnoreCase))
        {
            return Stopped;
        }

        return "";
    }
}
