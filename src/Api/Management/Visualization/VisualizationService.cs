using System.Globalization;
using System.Text;
using IotDaq.Persistence;
using IotDaq.Persistence.Visualization;
using Studio.Contracts;
using Studio.Host.Config;

namespace Studio.Host.Visualization;

public sealed class VisualizationService(ConfigStore store, GatewayPersistence database)
{
    private static readonly string[] GaugePoints =
    [
        "state", "workMode", "program", "programLine", "alarm", "alarmNumber",
        "partCount", "partCountTotal", "spindleSpeed", "spindleLoad", "spindleOverride",
        "feedRate", "feedOverride", "rapidOverride",
        "machinePositionX", "machinePositionY", "machinePositionZ"
    ];

    public DashboardSnapshot Overview(DateTimeOffset now, IReadOnlyList<DeviceHealthView> health)
    {
        var calendar = Calendar();
        var zone = calendar.ResolveZone();
        var dayStart = LocalMidnight(now, zone);
        var devices = Devices();
        var ids = devices.Select(device => device.Id).ToList();
        var latest = database.Latest(ids, GaugePoints);
        var transitions = database.Transitions(ids, dayStart.ToUnixTimeMilliseconds(), now.ToUnixTimeMilliseconds());
        var parts = database.PartSamples(ids, dayStart.AddHours(-6).ToUnixTimeMilliseconds(), now.ToUnixTimeMilliseconds(), 60_000);
        var alarms = database.QueryAlarms(null, true, null, null, null, null, 2000);
        var report = UtilizationMath.Build(devices, transitions, parts, calendar, dayStart, now, now);
        var healthById = health.ToDictionary(item => item.Id, StringComparer.OrdinalIgnoreCase);
        var tiles = new List<DashboardDevice>();
        foreach (var device in devices)
        {
            var samples = latest.Where(row => row.DeviceId == device.Id).ToList();
            var open = transitions.LastOrDefault(row => row.DeviceId == device.Id && row.EndedUnixMs is null);
            healthById.TryGetValue(device.Id, out var live);
            var state = CurrentState(device, open, samples, live);
            var rows = report.Where(row => row.DeviceId == device.Id).ToList();
            tiles.Add(new DashboardDevice
            {
                Id = device.Id,
                DisplayName = device.DisplayName,
                Workshop = Workshop(device.Workshop),
                Line = Line(device.Line),
                Adapter = device.Adapter,
                State = state,
                StateLabel = MachineState.Label(state),
                Program = Text(samples, "program"),
                PartCount = rows.Sum(row => row.PartCount),
                Utilization = UtilizationMath.Ratio(rows.Sum(row => row.RunMs), rows.Sum(row => row.PlannedMs)),
                SpindleSpeed = Number(samples, "spindleSpeed"),
                SpindleLoad = Number(samples, "spindleLoad"),
                FeedRate = Number(samples, "feedRate"),
                ActiveAlarms = alarms.Count(alarm => alarm.DeviceId == device.Id),
                Connection = live?.Status ?? "",
                ConnectionMessage = live?.Message ?? ""
            });
        }

        var counts = MachineState.All.ToDictionary(state => state, state => tiles.Count(tile => tile.State == state));
        return new DashboardSnapshot
        {
            GeneratedUnixMs = now.ToUnixTimeMilliseconds(),
            Counts = counts,
            TodayUtilization = UtilizationMath.Ratio(report.Sum(row => row.RunMs), report.Sum(row => row.PlannedMs)),
            TodayPartCount = tiles.Sum(tile => tile.PartCount),
            ActiveAlarmCount = alarms.Count,
            Groups = tiles
                .GroupBy(tile => tile.Workshop)
                .OrderBy(group => group.Key, StringComparer.Ordinal)
                .Select(workshop => new DashboardGroup
                {
                    Workshop = workshop.Key,
                    Lines = workshop
                        .GroupBy(tile => tile.Line)
                        .OrderBy(group => group.Key, StringComparer.Ordinal)
                        .Select(line => new DashboardLine
                        {
                            Line = line.Key,
                            Devices = line.OrderBy(tile => tile.DisplayName, StringComparer.Ordinal).ToList()
                        })
                        .ToList()
                })
                .ToList()
        };
    }

    public DeviceDetail? Detail(string deviceId, DateTimeOffset now, IReadOnlyList<DeviceHealthView> health)
    {
        var device = Devices().FirstOrDefault(item => string.Equals(item.Id, deviceId, StringComparison.OrdinalIgnoreCase));
        if (device is null)
        {
            return null;
        }

        var calendar = Calendar();
        var zone = calendar.ResolveZone();
        var dayStart = LocalMidnight(now, zone);
        var latest = database.Latest(device.Id);
        var transitions = database.Transitions([device.Id], dayStart.ToUnixTimeMilliseconds(), now.ToUnixTimeMilliseconds());
        var parts = database.PartSamples([device.Id], dayStart.AddHours(-6).ToUnixTimeMilliseconds(), now.ToUnixTimeMilliseconds(), 60_000);
        var report = UtilizationMath.Build([device], transitions, parts, calendar, dayStart, now, now);
        var alarms = database.QueryAlarms(device.Id, null, null, null, dayStart.AddDays(-1).ToUnixTimeMilliseconds(), now.ToUnixTimeMilliseconds(), 40);
        var live = health.FirstOrDefault(item => string.Equals(item.Id, device.Id, StringComparison.OrdinalIgnoreCase));
        var open = transitions.LastOrDefault(row => row.EndedUnixMs is null);
        var state = CurrentState(device, open, latest, live);
        var timeline = UtilizationMath.Timeline(transitions, dayStart.ToUnixTimeMilliseconds(), now.ToUnixTimeMilliseconds());
        var events = new List<DeviceEvent>();
        foreach (var segment in transitions.TakeLast(12))
        {
            events.Add(new DeviceEvent
            {
                TimestampUnixMs = segment.StartedUnixMs,
                Kind = "state",
                Message = MachineState.Label(segment.State) + (string.IsNullOrWhiteSpace(segment.State) ? "" : " · " + segment.State)
            });
        }

        foreach (var alarm in alarms.Take(12))
        {
            events.Add(new DeviceEvent
            {
                TimestampUnixMs = alarm.RaisedUnixMs,
                Kind = "alarm",
                Message = string.IsNullOrWhiteSpace(alarm.Code) ? alarm.Message : alarm.Code + " " + alarm.Message
            });
        }

        return new DeviceDetail
        {
            Id = device.Id,
            DisplayName = device.DisplayName,
            Workshop = Workshop(device.Workshop),
            Line = Line(device.Line),
            Adapter = device.Adapter,
            Enabled = device.Enabled,
            Host = device.Host,
            Port = device.Port,
            State = state,
            StateLabel = MachineState.Label(state),
            Connection = live?.Status ?? "",
            ConnectionMessage = live?.Message ?? "",
            LastSeenUnixMs = live?.LastSeen?.ToUnixTimeMilliseconds(),
            Program = Text(latest, "program"),
            ProgramLine = Number(latest, "programLine"),
            WorkMode = Text(latest, "workMode"),
            PartCount = Number(latest, "partCount"),
            PartCountTotal = Number(latest, "partCountTotal"),
            SpindleSpeed = Number(latest, "spindleSpeed"),
            SpindleLoad = Number(latest, "spindleLoad"),
            SpindleOverride = Number(latest, "spindleOverride"),
            FeedRate = Number(latest, "feedRate"),
            FeedOverride = Number(latest, "feedOverride"),
            RapidOverride = Number(latest, "rapidOverride"),
            AxisX = Number(latest, "machinePositionX"),
            AxisY = Number(latest, "machinePositionY"),
            AxisZ = Number(latest, "machinePositionZ"),
            AlarmText = Text(latest, "alarm"),
            TodayUtilization = UtilizationMath.Ratio(report.Sum(row => row.RunMs), report.Sum(row => row.PlannedMs)),
            TodayPartCount = report.Sum(row => row.PartCount),
            TimelineFromUnixMs = dayStart.ToUnixTimeMilliseconds(),
            TimelineToUnixMs = now.ToUnixTimeMilliseconds(),
            Timeline = timeline.Select(piece => new TimelinePiece
            {
                State = piece.State,
                Label = MachineState.Label(piece.State),
                StartedUnixMs = piece.Start,
                EndedUnixMs = piece.End
            }).ToList(),
            ActiveAlarms = alarms.Where(alarm => alarm.Active).ToList(),
            RecentEvents = events.OrderByDescending(item => item.TimestampUnixMs).Take(16).ToList(),
            Latest = latest
        };
    }

    public SeriesResponse Series(string? devices, string? points, long fromUnixMs, long toUnixMs, long bucketMs)
    {
        var deviceIds = Split(devices).Take(8).ToList();
        var pointIds = Split(points).Take(8).ToList();
        if (toUnixMs < fromUnixMs)
        {
            (fromUnixMs, toUnixMs) = (toUnixMs, fromUnixMs);
        }

        var bucket = SeriesAggregation.ChooseBucket(fromUnixMs, toUnixMs, bucketMs);
        var series = database.AggregateSeries(deviceIds, pointIds, fromUnixMs, toUnixMs, bucket);
        return new SeriesResponse
        {
            BucketMs = bucket,
            FromUnixMs = fromUnixMs,
            ToUnixMs = toUnixMs,
            Series = series.ToList()
        };
    }

    public UtilizationResponse Utilization(long fromUnixMs, long toUnixMs, string? deviceId)
    {
        var now = DateTimeOffset.UtcNow;
        var from = DateTimeOffset.FromUnixTimeMilliseconds(fromUnixMs);
        var to = DateTimeOffset.FromUnixTimeMilliseconds(toUnixMs);
        if (to < from)
        {
            (from, to) = (to, from);
        }

        var devices = Devices();
        if (!string.IsNullOrWhiteSpace(deviceId))
        {
            devices = devices.Where(device => string.Equals(device.Id, deviceId, StringComparison.OrdinalIgnoreCase)).ToList();
        }

        var ids = devices.Select(device => device.Id).ToList();
        var calendar = Calendar();
        var segments = database.Transitions(ids, from.ToUnixTimeMilliseconds(), to.ToUnixTimeMilliseconds());
        var parts = database.PartSamples(ids, from.AddHours(-6).ToUnixTimeMilliseconds(), to.ToUnixTimeMilliseconds(), 60_000);
        var shifts = UtilizationMath.Build(devices, segments, parts, calendar, from, to, now);
        return new UtilizationResponse
        {
            FromUnixMs = from.ToUnixTimeMilliseconds(),
            ToUnixMs = to.ToUnixTimeMilliseconds(),
            Calendar = calendar,
            Shifts = shifts,
            Days = UtilizationMath.RollupDays(shifts),
            Lines = UtilizationMath.RollupLines(shifts)
        };
    }

    public VizSettings Settings()
    {
        var calendar = Calendar();
        return new VizSettings
        {
            HistoryRetentionDays = database.HistoryRetentionDays,
            TimeZone = calendar.TimeZone,
            Shifts = calendar.Shifts
        };
    }

    public IReadOnlyList<string> SaveSettings(VizSettings input)
    {
        var days = Math.Clamp(input.HistoryRetentionDays <= 0 ? 14 : input.HistoryRetentionDays, 1, 3650);
        var calendar = new ShiftCalendar
        {
            TimeZone = string.IsNullOrWhiteSpace(input.TimeZone) ? "Asia/Shanghai" : input.TimeZone.Trim(),
            Shifts = input.Shifts ?? []
        };
        var issues = calendar.Validate();
        if (issues.Count > 0)
        {
            return issues;
        }

        database.SetSetting("historyRetentionDays", days.ToString(CultureInfo.InvariantCulture));
        database.SetSetting("shiftCalendar", calendar.ToJson());
        return [];
    }

    public string SeriesCsv(SeriesResponse series)
    {
        var builder = new StringBuilder();
        builder.AppendLine("timestamp,deviceId,pointId,avg,min,max,count");
        foreach (var point in series.Series)
        {
            builder.Append(DateTimeOffset.FromUnixTimeMilliseconds(point.TimestampUnixMs).ToString("O", CultureInfo.InvariantCulture));
            builder.Append(',').Append(Csv(point.DeviceId));
            builder.Append(',').Append(Csv(point.PointId));
            builder.Append(',').Append(point.Avg.ToString("0.###", CultureInfo.InvariantCulture));
            builder.Append(',').Append(point.Min.ToString("0.###", CultureInfo.InvariantCulture));
            builder.Append(',').Append(point.Max.ToString("0.###", CultureInfo.InvariantCulture));
            builder.Append(',').Append(point.Count.ToString(CultureInfo.InvariantCulture));
            builder.AppendLine();
        }

        return builder.ToString();
    }

    public string UtilizationCsv(IReadOnlyList<UtilizationRow> rows)
    {
        var builder = new StringBuilder();
        builder.AppendLine("day,shift,workshop,line,deviceId,displayName,runMinutes,idleMinutes,alarmMinutes,offlineMinutes,stoppedMinutes,plannedMinutes,utilization,partCount");
        foreach (var row in rows)
        {
            builder.Append(Csv(row.Day)).Append(',');
            builder.Append(Csv(row.Shift)).Append(',');
            builder.Append(Csv(row.Workshop)).Append(',');
            builder.Append(Csv(row.Line)).Append(',');
            builder.Append(Csv(row.DeviceId)).Append(',');
            builder.Append(Csv(row.DisplayName)).Append(',');
            builder.Append(Minutes(row.RunMs)).Append(',');
            builder.Append(Minutes(row.IdleMs)).Append(',');
            builder.Append(Minutes(row.AlarmMs)).Append(',');
            builder.Append(Minutes(row.OfflineMs)).Append(',');
            builder.Append(Minutes(row.StoppedMs)).Append(',');
            builder.Append(Minutes(row.PlannedMs)).Append(',');
            builder.Append(row.Utilization.ToString("0.####", CultureInfo.InvariantCulture)).Append(',');
            builder.Append(row.PartCount.ToString("0.###", CultureInfo.InvariantCulture));
            builder.AppendLine();
        }

        return builder.ToString();
    }

    public byte[] UtilizationExcel(IReadOnlyList<UtilizationRow> rows)
    {
        var table = rows.Select(row => (IReadOnlyList<string>)
        [
            row.Day,
            row.Shift,
            row.Workshop,
            row.Line,
            row.DeviceId,
            row.DisplayName,
            Minutes(row.RunMs),
            Minutes(row.PlannedMs),
            row.Utilization.ToString("0.####", CultureInfo.InvariantCulture),
            row.PartCount.ToString("0.###", CultureInfo.InvariantCulture)
        ]).ToList();
        return SpreadsheetXml.Build(
            "稼动率",
            ["日期", "班次", "车间", "产线", "设备", "名称", "运行分钟", "计划分钟", "稼动率", "产量"],
            table);
    }

    public string AlarmsCsv(IReadOnlyList<AlarmView> alarms)
    {
        var builder = new StringBuilder();
        builder.AppendLine("raised,cleared,deviceId,code,message,active,acknowledged,durationMinutes");
        foreach (var alarm in alarms)
        {
            builder.Append(DateTimeOffset.FromUnixTimeMilliseconds(alarm.RaisedUnixMs).ToString("O", CultureInfo.InvariantCulture)).Append(',');
            builder.Append(alarm.ClearedUnixMs is null ? "" : DateTimeOffset.FromUnixTimeMilliseconds(alarm.ClearedUnixMs.Value).ToString("O", CultureInfo.InvariantCulture)).Append(',');
            builder.Append(Csv(alarm.DeviceId)).Append(',');
            builder.Append(Csv(alarm.Code)).Append(',');
            builder.Append(Csv(alarm.Message)).Append(',');
            builder.Append(alarm.Active ? "1" : "0").Append(',');
            builder.Append(alarm.Acknowledged ? "1" : "0").Append(',');
            builder.Append(Minutes(alarm.DurationMs ?? 0));
            builder.AppendLine();
        }

        return builder.ToString();
    }

    public (List<AlarmStatBucket> ByDevice, List<AlarmStatBucket> ByCode) AlarmStatistics(long? fromUnixMs, long? toUnixMs, string? deviceId, int top)
    {
        var alarms = database.QueryAlarms(deviceId, null, null, null, fromUnixMs, toUnixMs, 2000);
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var inputs = alarms.Select(alarm => new AlarmStatInput
        {
            DeviceId = alarm.DeviceId,
            Code = string.IsNullOrWhiteSpace(alarm.Code) ? alarm.PointId : alarm.Code,
            Message = alarm.Message,
            DurationMs = alarm.DurationMs ?? AlarmLogic.Duration(alarm.RaisedUnixMs, alarm.ClearedUnixMs, now, alarm.Active)
        }).ToList();
        return AlarmStats.Top(inputs, top);
    }

    private ShiftCalendar Calendar() => ShiftCalendar.ParseOrDefault(database.GetSetting("shiftCalendar"));

    private List<DeviceRef> Devices()
    {
        return store.ReadPublished().Devices
            .OrderBy(device => device.Metadata.Id, StringComparer.Ordinal)
            .Select(device => new DeviceRef
            {
                Id = device.Metadata.Id,
                DisplayName = string.IsNullOrWhiteSpace(device.Metadata.DisplayName) ? device.Metadata.Id : device.Metadata.DisplayName,
                Workshop = device.Spec.Workshop ?? "",
                Line = device.Spec.Line ?? "",
                Adapter = device.Spec.Adapter,
                Enabled = device.Spec.Enabled,
                Host = device.Spec.Connection?.Host ?? "",
                Port = device.Spec.Connection?.Port ?? 0
            })
            .ToList();
    }

    private static string CurrentState(DeviceRef device, StateSegment open, IReadOnlyList<SampleView> samples, DeviceHealthView? health)
    {
        if (!device.Enabled || string.Equals(health?.Status, "disabled", StringComparison.OrdinalIgnoreCase))
        {
            return MachineState.Stopped;
        }

        if (!string.IsNullOrEmpty(open.DeviceId))
        {
            return MachineState.Normalize(open.State);
        }

        var state = samples.FirstOrDefault(row => string.Equals(row.PointId, "state", StringComparison.OrdinalIgnoreCase));
        if (state is not null)
        {
            return MachineState.Normalize(state.Value);
        }

        if (string.Equals(health?.Status, "offline", StringComparison.OrdinalIgnoreCase))
        {
            return MachineState.Offline;
        }

        return MachineState.Idle;
    }

    private static string? Text(IReadOnlyList<SampleView> samples, string point) =>
        samples.FirstOrDefault(row => string.Equals(row.PointId, point, StringComparison.OrdinalIgnoreCase))?.Value;

    private static double? Number(IReadOnlyList<SampleView> samples, string point) =>
        samples.FirstOrDefault(row => string.Equals(row.PointId, point, StringComparison.OrdinalIgnoreCase))?.NumericValue;

    private static DateTimeOffset LocalMidnight(DateTimeOffset now, TimeZoneInfo zone)
    {
        var local = TimeZoneInfo.ConvertTime(now, zone);
        var midnight = DateTime.SpecifyKind(local.Date, DateTimeKind.Unspecified);
        return new DateTimeOffset(midnight, zone.GetUtcOffset(midnight));
    }

    private static string Workshop(string value) => string.IsNullOrWhiteSpace(value) ? "未分组" : value.Trim();

    private static string Line(string value) => string.IsNullOrWhiteSpace(value) ? "未分线" : value.Trim();

    private static List<string> Split(string? text) =>
        (text ?? "")
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

    private static string Csv(string? value)
    {
        var text = value ?? "";
        if (text.Contains('"') || text.Contains(',') || text.Contains('\n'))
        {
            return "\"" + text.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";
        }

        return text;
    }

    private static string Minutes(long milliseconds) =>
        (milliseconds / 60000d).ToString("0.##", CultureInfo.InvariantCulture);
}
