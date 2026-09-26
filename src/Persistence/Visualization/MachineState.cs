namespace IotDaq.Persistence.Visualization;

/// <summary>
/// Five shop-floor states. Raw CNC words from the catalog (<c>RUNNING</c>, <c>IDLE</c>, …) map onto these.
/// </summary>
public static class MachineState
{
    public const string Running = "running";
    public const string Idle = "idle";
    public const string Alarm = "alarm";
    public const string Offline = "offline";
    public const string Stopped = "stopped";

    public static readonly string[] All = [Running, Idle, Alarm, Offline, Stopped];

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

    public static string Label(string? state) => Normalize(state) switch
    {
        Running => "运行",
        Idle => "待机",
        Alarm => "报警",
        Offline => "离线",
        Stopped => "停机",
        _ => "待机"
    };

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
