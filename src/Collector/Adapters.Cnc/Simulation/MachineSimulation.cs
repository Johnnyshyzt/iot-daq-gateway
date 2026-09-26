namespace Adapters.Cnc;

/// <summary>
/// One coherent machine cycle shared by every brand simulator.
/// Each device id picks a personality so a shop floor shows 运行 / 待机 / 报警 / 离线 / 停机 together.
/// Part count steps up when a cycle ends and wraps so a shift can see a counter reset.
/// Spindle and feed are zero unless the machine is running.
/// </summary>
public static class MachineSimulation
{
    public const int CycleSeconds = 90;

    public const int PartCountModulo = 240;

    public sealed record Snapshot(
        string State,
        string WorkMode,
        bool Estop,
        bool Alarm,
        string Program,
        string ProgramMain,
        string ProgramComment,
        string AlarmText,
        int ProgramLine,
        double SpindleSpeed,
        double SpindleSpeedCmd,
        double SpindleOverride,
        double FeedRate,
        double FeedRateCmd,
        double FeedOverride,
        double RapidOverride,
        double PartCount,
        double PartCountTotal,
        double CycleSecondsValue,
        double RunSeconds,
        double CutSeconds,
        double PowerOnSeconds,
        double AxisX,
        double AxisY,
        double AxisZ,
        double Load,
        double SpindleLoad,
        double TempC);

    public static Snapshot At(DateTimeOffset now, string deviceId, string brandId)
    {
        var skew = Math.Abs(StableHash(deviceId)) % 30;
        var elapsed = now.ToUnixTimeSeconds() + skew;
        if (elapsed < 0)
        {
            elapsed = 0;
        }

        var personality = Math.Abs(StableHash(deviceId)) % 6;
        var phase = (int)(elapsed % CycleSeconds);
        var completed = elapsed / CycleSeconds;
        var state = StateAt(personality, phase);
        var running = state == "RUNNING";
        var alarm = state == "ALARM";
        var estop = alarm && phase % 5 == 0;
        var spindle = running ? 900 + (phase * 47 % 2100) : 0;
        var feed = running ? 120 + (phase * 13 % 800) : 0;
        var axis = running ? phase * 0.37 : 12.5;
        var parts = completed % PartCountModulo;
        return new Snapshot(
            State: state,
            WorkMode: running ? "AUTO" : alarm ? "ALARM" : state == "STOPPED" ? "MDI" : "JOG",
            Estop: estop,
            Alarm: alarm,
            Program: ProgramName(brandId, (int)(completed % 8)),
            ProgramMain: ProgramName(brandId, 0),
            ProgramComment: "PART-" + (completed % 8).ToString(System.Globalization.CultureInfo.InvariantCulture),
            AlarmText: alarm ? AlarmText(personality) : "0",
            ProgramLine: running ? 10 + (phase % 40) : 0,
            SpindleSpeed: spindle,
            SpindleSpeedCmd: running ? 1800 : 0,
            SpindleOverride: running ? 100 : 0,
            FeedRate: feed,
            FeedRateCmd: running ? 500 : 0,
            FeedOverride: 100,
            RapidOverride: running ? 100 : 50,
            PartCount: parts,
            PartCountTotal: completed + 120,
            CycleSecondsValue: running ? phase - 20 : phase < 20 ? 0 : 50,
            RunSeconds: elapsed,
            CutSeconds: completed * 50,
            PowerOnSeconds: elapsed + 86_400,
            AxisX: Math.Round(axis, 3),
            AxisY: Math.Round(running ? phase * 0.11 : 0, 3),
            AxisZ: Math.Round(running ? -2.5 - (phase * 0.05) : -2.5, 3),
            Load: running ? 35 + (phase % 25) : alarm ? 5 : 2,
            SpindleLoad: running ? 40 + (phase % 30) : 0,
            TempC: running ? 42 + (phase % 8) : 28);
    }

    public static string StateAt(int personality, int phase)
    {
        var band = ((personality % 6) + 6) % 6;
        return band switch
        {
            0 => phase switch
            {
                < 20 => "IDLE",
                < 70 => "RUNNING",
                < 82 => "ALARM",
                _ => "IDLE"
            },
            1 => phase switch
            {
                < 12 => "IDLE",
                < 55 => "RUNNING",
                < 68 => "STOPPED",
                < 80 => "ALARM",
                _ => "IDLE"
            },
            2 => phase switch
            {
                < 35 => "IDLE",
                < 68 => "RUNNING",
                < 80 => "ALARM",
                _ => "IDLE"
            },
            3 => phase switch
            {
                < 15 => "IDLE",
                < 45 => "RUNNING",
                < 60 => "OFFLINE",
                < 75 => "RUNNING",
                < 85 => "ALARM",
                _ => "IDLE"
            },
            4 => phase switch
            {
                < 18 => "IDLE",
                < 40 => "RUNNING",
                < 55 => "ALARM",
                < 75 => "STOPPED",
                _ => "RUNNING"
            },
            _ => phase switch
            {
                < 22 => "RUNNING",
                < 36 => "ALARM",
                < 50 => "IDLE",
                < 80 => "RUNNING",
                _ => "IDLE"
            }
        };
    }

    public static string AlarmText(int personality) => (personality % 6) switch
    {
        0 => "EX100 伺服过载",
        1 => "EX231 主轴过热",
        2 => "PS101 程序错误",
        3 => "OH000 超程",
        4 => "SV040 伺服报警",
        _ => "EX441 冷却异常"
    };

    public static string ProgramName(string brandId, int index)
    {
        var number = 1 + index;
        return brandId switch
        {
            "siemens" => $"PART_{number:00}.MPF",
            "heidenhain" => $"TNC:\\PART{number:00}.H",
            "haas" => $"O{number:0000}.nc",
            "mazak-smart" or "mazak-matrix" => $"P{number:0000}",
            _ => $"O{number:0000}"
        };
    }

    private static int StableHash(string text)
    {
        var hash = 17;
        foreach (var ch in text)
        {
            hash = unchecked(hash * 31 + ch);
        }

        return hash;
    }
}
