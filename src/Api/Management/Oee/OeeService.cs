using System.Globalization;
using System.Text;
using IotDaq.Persistence;
using IotDaq.Persistence.Rules;
using IotDaq.Persistence.Visualization;
using Studio.Host.Config;

namespace Studio.Host.Oee;

public sealed class OeeService(GatewayPersistence database, ConfigStore store)
{
    public OeeReport Build(long fromUnixMs, long toUnixMs, string? deviceId, string? line)
    {
        var from = DateTimeOffset.FromUnixTimeMilliseconds(fromUnixMs);
        var to = DateTimeOffset.FromUnixTimeMilliseconds(Math.Max(fromUnixMs, toUnixMs));
        var now = DateTimeOffset.UtcNow;
        var end = to > now ? now : to;
        var calendar = ShiftCalendar.ParseOrDefault(database.GetSetting("shiftCalendar"));
        var devices = Devices(deviceId, line);
        var ids = devices.Select(device => device.Id).ToList();
        var segments = ids.Count == 0 ? [] : database.Transitions(ids, from.ToUnixTimeMilliseconds(), end.ToUnixTimeMilliseconds());
        var parts = ids.Count == 0 ? [] : database.PartSamples(ids, from.AddHours(-6).ToUnixTimeMilliseconds(), end.ToUnixTimeMilliseconds(), 60_000);
        var scrapPoint = database.GetSetting("oee.scrapPoint");
        if (string.IsNullOrWhiteSpace(scrapPoint))
        {
            scrapPoint = "scrapCount";
        }

        var scrapSamples = ids.Count == 0 ? [] : ScrapSamples(ids, scrapPoint, from.AddHours(-6), end);
        var manual = database.ListScrap(deviceId, from.ToUnixTimeMilliseconds(), end.ToUnixTimeMilliseconds());
        var cycles = database.ListCycleTimes().Select(row => new CycleSpec
        {
            Scope = row.Scope,
            OwnerId = row.OwnerId,
            Program = row.Program,
            IdealSeconds = row.IdealSeconds
        }).ToList();
        var programs = ProgramNames(ids, from, end);
        var stops = database.ListPlannedStops();
        var reasons = database.ListReasons().ToDictionary(row => row.Id, StringComparer.Ordinal);
        var events = database.ListDowntime(deviceId, from.ToUnixTimeMilliseconds(), end.ToUnixTimeMilliseconds(), false, 500);
        var windows = calendar.Windows(from, end);
        var rows = new List<OeeRow>();
        foreach (var device in devices)
        {
            foreach (var window in windows)
            {
                var overlapStart = window.Start > from ? window.Start : from;
                var overlapEnd = window.End < end ? window.End : end;
                if (overlapEnd <= overlapStart)
                {
                    continue;
                }

                var startMs = overlapStart.ToUnixTimeMilliseconds();
                var endMs = overlapEnd.ToUnixTimeMilliseconds();
                var deviceSegments = segments.Where(segment => segment.DeviceId == device.Id).ToList();
                var durations = UtilizationMath.Durations(deviceSegments, startMs, endMs);
                var holes = HoleIntervals(device, stops, deviceSegments, startMs, endMs);
                var deduction = OeeMath.MergedOverlap(startMs, endMs, holes);
                var planned = OeeMath.PlannedProduction(window.PlannedMs, deduction);
                var run = durations[MachineState.Running];
                var total = Parts(parts, device.Id, startMs, endMs);
                var scrap = Parts(scrapSamples, device.Id, startMs, endMs)
                    + manual.Where(item => string.Equals(item.DeviceId, device.Id, StringComparison.OrdinalIgnoreCase) && item.UnixMs >= startMs && item.UnixMs < endMs).Sum(item => item.Quantity);
                programs.TryGetValue(device.Id, out var program);
                var ideal = OeeMath.ResolveIdeal(cycles, device.Id, program);
                var factors = OeeMath.Compute(new OeeInput
                {
                    PlannedMs = planned,
                    RunMs = run,
                    TotalParts = total,
                    ScrapParts = scrap,
                    IdealCycleSeconds = ideal
                });
                rows.Add(new OeeRow
                {
                    DeviceId = device.Id,
                    DisplayName = device.DisplayName,
                    Workshop = device.Workshop,
                    Line = device.Line,
                    Day = window.Day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                    Shift = window.Name,
                    PlannedMs = planned,
                    RunMs = run,
                    IdleMs = durations[MachineState.Idle],
                    AlarmMs = durations[MachineState.Alarm],
                    SetupMs = durations[MachineState.Setup],
                    WaitingMs = durations[MachineState.Waiting],
                    PlannedStopMs = durations[MachineState.Planned] + deduction,
                    OfflineMs = durations[MachineState.Offline],
                    StoppedMs = durations[MachineState.Stopped],
                    TotalParts = total,
                    ScrapParts = scrap,
                    GoodParts = factors.GoodParts,
                    IdealCycleSeconds = ideal,
                    Availability = factors.Availability,
                    Performance = factors.Performance,
                    Quality = factors.Quality,
                    Oee = factors.Oee,
                    Flag = factors.Flag
                });
            }
        }

        var pareto = OeeMath.Pareto(events.Select(item =>
        {
            var name = item.ReasonId is not null && reasons.TryGetValue(item.ReasonId, out var reason) ? reason.Name : "未说明";
            var until = item.EndedUnixMs ?? end.ToUnixTimeMilliseconds();
            var start = Math.Max(item.StartedUnixMs, from.ToUnixTimeMilliseconds());
            until = Math.Min(until, end.ToUnixTimeMilliseconds());
            return (name, Math.Max(0, until - start));
        }));
        var span = Math.Max(0, (long)(end - from).TotalMilliseconds);
        var waterfall = OeeMath.Waterfall(new WaterfallInput
        {
            CalendarMs = span,
            OutsideShiftMs = Math.Max(0, span - rows.Sum(row => row.PlannedMs) - rows.Sum(row => Math.Max(0, WindowLength(row) - row.PlannedMs))),
            PlannedDeductionMs = rows.Sum(row => row.PlannedStopMs),
            AlarmMs = rows.Sum(row => row.AlarmMs),
            SetupMs = rows.Sum(row => row.SetupMs),
            WaitingMs = rows.Sum(row => row.WaitingMs),
            IdleMs = rows.Sum(row => row.IdleMs),
            OfflineMs = rows.Sum(row => row.OfflineMs),
            OtherStopMs = rows.Sum(row => row.StoppedMs),
            RunMs = rows.Sum(row => row.RunMs),
            TotalParts = rows.Sum(row => row.TotalParts),
            ScrapParts = rows.Sum(row => row.ScrapParts),
            IdealCycleSeconds = rows.Select(row => row.IdealCycleSeconds).FirstOrDefault(value => value is > 0)
        });
        return new OeeReport
        {
            FromUnixMs = from.ToUnixTimeMilliseconds(),
            ToUnixMs = end.ToUnixTimeMilliseconds(),
            Rows = rows,
            Days = Rollup(rows, day: true),
            Lines = Rollup(rows, day: false),
            Pareto = pareto,
            Waterfall = waterfall,
            Formula = Formula
        };
    }

    public string Csv(IReadOnlyList<OeeRow> rows)
    {
        var builder = new StringBuilder();
        builder.AppendLine("day,shift,workshop,line,deviceId,displayName,plannedMinutes,runMinutes,availability,performance,quality,oee,totalParts,goodParts,scrapParts,idealSeconds,flag");
        foreach (var row in rows)
        {
            builder.Append(row.Day).Append(',').Append(CsvCell(row.Shift)).Append(',')
                .Append(CsvCell(row.Workshop)).Append(',').Append(CsvCell(row.Line)).Append(',')
                .Append(CsvCell(row.DeviceId)).Append(',').Append(CsvCell(row.DisplayName)).Append(',')
                .Append(Minutes(row.PlannedMs)).Append(',').Append(Minutes(row.RunMs)).Append(',')
                .Append(Num(row.Availability)).Append(',').Append(Num(row.Performance)).Append(',')
                .Append(Num(row.Quality)).Append(',').Append(Num(row.Oee)).Append(',')
                .Append(Num(row.TotalParts)).Append(',').Append(Num(row.GoodParts)).Append(',')
                .Append(Num(row.ScrapParts)).Append(',').Append(Num(row.IdealCycleSeconds)).Append(',')
                .Append(row.Flag).AppendLine();
        }

        return builder.ToString();
    }

    public byte[] Excel(IReadOnlyList<OeeRow> rows) =>
        Visualization.SpreadsheetXml.Build(
            "OEE",
            ["日期", "班次", "车间", "产线", "设备", "名称", "计划分钟", "运行分钟", "可用率", "性能率", "质量率", "OEE", "总产量", "良品", "报废", "理想节拍秒", "标记"],
            rows.Select(row => (IReadOnlyList<string>)
            [
                row.Day, row.Shift, row.Workshop, row.Line, row.DeviceId, row.DisplayName,
                Minutes(row.PlannedMs), Minutes(row.RunMs),
                Num(row.Availability), Num(row.Performance), Num(row.Quality), Num(row.Oee),
                Num(row.TotalParts), Num(row.GoodParts), Num(row.ScrapParts), Num(row.IdealCycleSeconds), row.Flag
            ]).ToList());

    private static List<OeeRow> Rollup(IReadOnlyList<OeeRow> rows, bool day)
    {
        var groups = day
            ? rows.GroupBy(row => (row.DeviceId, row.Day, Shift: ""))
            : rows.GroupBy(row => (DeviceId: "", row.Day, row.Shift));
        if (!day)
        {
            groups = rows.GroupBy(row => (DeviceId: row.Line, row.Day, row.Shift));
        }

        return groups.Select(group =>
        {
            var first = group.First();
            var planned = group.Sum(row => row.PlannedMs);
            var run = group.Sum(row => row.RunMs);
            var total = group.Sum(row => row.TotalParts);
            var scrap = group.Sum(row => row.ScrapParts);
            var ideals = group.Select(row => row.IdealCycleSeconds).Where(value => value is > 0).Distinct().ToList();
            double? ideal = ideals.Count == 1 ? ideals[0] : null;
            if (ideals.Count > 1 && run > 0)
            {
                var earned = group.Where(row => row.IdealCycleSeconds is > 0).Sum(row => row.IdealCycleSeconds!.Value * row.TotalParts);
                var covered = group.Where(row => row.IdealCycleSeconds is > 0).Sum(row => row.TotalParts);
                ideal = covered > 0 ? earned / covered : null;
            }

            var factors = OeeMath.Compute(new OeeInput
            {
                PlannedMs = planned,
                RunMs = run,
                TotalParts = total,
                ScrapParts = scrap,
                IdealCycleSeconds = ideal
            });
            return new OeeRow
            {
                DeviceId = day ? first.DeviceId : "",
                DisplayName = day ? first.DisplayName : "",
                Workshop = first.Workshop,
                Line = day ? first.Line : group.Key.DeviceId,
                Day = group.Key.Day,
                Shift = day ? "" : group.Key.Shift,
                PlannedMs = planned,
                RunMs = run,
                IdleMs = group.Sum(row => row.IdleMs),
                AlarmMs = group.Sum(row => row.AlarmMs),
                SetupMs = group.Sum(row => row.SetupMs),
                WaitingMs = group.Sum(row => row.WaitingMs),
                PlannedStopMs = group.Sum(row => row.PlannedStopMs),
                OfflineMs = group.Sum(row => row.OfflineMs),
                StoppedMs = group.Sum(row => row.StoppedMs),
                TotalParts = total,
                ScrapParts = scrap,
                GoodParts = factors.GoodParts,
                IdealCycleSeconds = ideal,
                Availability = factors.Availability,
                Performance = factors.Performance,
                Quality = factors.Quality,
                Oee = factors.Oee,
                Flag = factors.Flag
            };
        }).OrderBy(row => row.Day).ThenBy(row => row.Line).ToList();
    }

    private List<DeviceRef> Devices(string? deviceId, string? line)
    {
        return store.ReadPublished().Devices
            .Where(device => string.IsNullOrWhiteSpace(deviceId) || string.Equals(device.Metadata.Id, deviceId, StringComparison.OrdinalIgnoreCase))
            .Where(device => string.IsNullOrWhiteSpace(line) || string.Equals(device.Spec.Line, line, StringComparison.OrdinalIgnoreCase))
            .Select(device => new DeviceRef
            {
                Id = device.Metadata.Id,
                DisplayName = string.IsNullOrWhiteSpace(device.Metadata.DisplayName) ? device.Metadata.Id : device.Metadata.DisplayName,
                Workshop = device.Spec.Workshop ?? "",
                Line = device.Spec.Line ?? "",
                Enabled = device.Spec.Enabled
            })
            .ToList();
    }

    private static List<(long Start, long End)> HoleIntervals(DeviceRef device, IReadOnlyList<PlannedStopRow> stops, IReadOnlyList<StateSegment> segments, long start, long end)
    {
        var holes = new List<(long Start, long End)>();
        foreach (var stop in stops)
        {
            var match = string.Equals(stop.Scope, "device", StringComparison.OrdinalIgnoreCase) && string.Equals(stop.OwnerId, device.Id, StringComparison.OrdinalIgnoreCase)
                || string.Equals(stop.Scope, "line", StringComparison.OrdinalIgnoreCase) && string.Equals(stop.OwnerId, device.Line, StringComparison.OrdinalIgnoreCase);
            if (match)
            {
                holes.Add((stop.StartUnixMs, stop.EndUnixMs));
            }
        }

        foreach (var segment in segments)
        {
            if (!string.Equals(segment.State, MachineState.Planned, StringComparison.Ordinal))
            {
                continue;
            }

            holes.Add((segment.StartedUnixMs, segment.EndedUnixMs ?? end));
        }

        return holes;
    }

    private List<PartSample> ScrapSamples(IReadOnlyList<string> ids, string point, DateTimeOffset from, DateTimeOffset to)
    {
        var frames = new List<PartSample>();
        foreach (var id in ids)
        {
            foreach (var frame in database.HistoryFrames(id, [point], from.ToUnixTimeMilliseconds(), to.ToUnixTimeMilliseconds(), 2000))
            {
                if (frame.Numbers.TryGetValue(point, out var value) && value is double number)
                {
                    frames.Add(new PartSample(id, frame.UnixMs, number));
                }
            }
        }

        return frames;
    }

    private Dictionary<string, string> ProgramNames(IReadOnlyList<string> ids, DateTimeOffset from, DateTimeOffset to)
    {
        var names = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var id in ids)
        {
            var frames = database.HistoryFrames(id, ["program"], from.ToUnixTimeMilliseconds(), to.ToUnixTimeMilliseconds(), 20);
            var text = frames.LastOrDefault()?.Texts.GetValueOrDefault("program");
            if (!string.IsNullOrWhiteSpace(text))
            {
                names[id] = text;
            }
        }

        return names;
    }

    private static double Parts(IReadOnlyList<PartSample> samples, string deviceId, long start, long end)
    {
        var rows = samples.Where(sample => sample.DeviceId == deviceId && sample.TimestampUnixMs < end).OrderBy(sample => sample.TimestampUnixMs).ToList();
        var before = rows.LastOrDefault(sample => sample.TimestampUnixMs < start);
        var inside = rows.Where(sample => sample.TimestampUnixMs >= start).ToList();
        if (!string.IsNullOrEmpty(before.DeviceId))
        {
            inside.Insert(0, before);
        }

        return UtilizationMath.PartDelta(inside);
    }

    private static long WindowLength(OeeRow row) => row.PlannedMs;

    private static string Minutes(long ms) => (ms / 60000d).ToString("0.###", CultureInfo.InvariantCulture);

    private static string Num(double? value) => value?.ToString("0.####", CultureInfo.InvariantCulture) ?? "";

    private static string CsvCell(string value) => "\"" + (value ?? "").Replace("\"", "\"\"") + "\"";

    public const string Formula =
        "OEE = 可用率 × 性能率 × 质量率。可用率 = 运行时间 / 计划生产时间。计划生产时间 = 班次计划（配置了休息时段时为班次长度减去休息；节假日为 0）再减去计划停机。性能率 = 理想节拍秒 × 总产量 / 运行秒；没有理想节拍时性能率和 OEE 留空。质量率 = (总产量 − 报废) / 总产量；总产量为 0 且没有报废时质量率记为 1。计划生产时间为 0 时 OEE 记为 0。";
}

public sealed class OeeReport
{
    public long FromUnixMs { get; set; }

    public long ToUnixMs { get; set; }

    public List<OeeRow> Rows { get; set; } = [];

    public List<OeeRow> Days { get; set; } = [];

    public List<OeeRow> Lines { get; set; } = [];

    public List<ParetoBar> Pareto { get; set; } = [];

    public List<WaterfallStep> Waterfall { get; set; } = [];

    public string Formula { get; set; } = "";
}

public sealed class OeeRow
{
    public string DeviceId { get; set; } = "";

    public string DisplayName { get; set; } = "";

    public string Workshop { get; set; } = "";

    public string Line { get; set; } = "";

    public string Day { get; set; } = "";

    public string Shift { get; set; } = "";

    public long PlannedMs { get; set; }

    public long RunMs { get; set; }

    public long IdleMs { get; set; }

    public long AlarmMs { get; set; }

    public long SetupMs { get; set; }

    public long WaitingMs { get; set; }

    public long PlannedStopMs { get; set; }

    public long OfflineMs { get; set; }

    public long StoppedMs { get; set; }

    public double TotalParts { get; set; }

    public double ScrapParts { get; set; }

    public double GoodParts { get; set; }

    public double? IdealCycleSeconds { get; set; }

    public double Availability { get; set; }

    public double? Performance { get; set; }

    public double Quality { get; set; }

    public double? Oee { get; set; }

    public string Flag { get; set; } = "";
}
