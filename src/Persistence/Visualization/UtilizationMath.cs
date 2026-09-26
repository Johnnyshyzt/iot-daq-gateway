namespace IotDaq.Persistence.Visualization;

public readonly record struct StateSegment(string DeviceId, string State, long StartedUnixMs, long? EndedUnixMs);

public readonly record struct PartSample(string DeviceId, long TimestampUnixMs, double Value);

public sealed class DeviceRef
{
    public string Id { get; set; } = "";

    public string DisplayName { get; set; } = "";

    public string Workshop { get; set; } = "";

    public string Line { get; set; } = "";

    public string Adapter { get; set; } = "";

    public bool Enabled { get; set; } = true;

    public string Host { get; set; } = "";

    public int Port { get; set; }
}

public sealed class UtilizationRow
{
    public string DeviceId { get; set; } = "";

    public string DisplayName { get; set; } = "";

    public string Workshop { get; set; } = "";

    public string Line { get; set; } = "";

    public string Day { get; set; } = "";

    public string Shift { get; set; } = "";

    public long RunMs { get; set; }

    public long IdleMs { get; set; }

    public long AlarmMs { get; set; }

    public long OfflineMs { get; set; }

    public long StoppedMs { get; set; }

    public long PlannedMs { get; set; }

    public double Utilization { get; set; }

    public double PartCount { get; set; }
}

public static class UtilizationMath
{
    public static List<(string State, long Start, long End)> Timeline(IReadOnlyList<StateSegment> segments, long startMs, long endMs)
    {
        var pieces = new List<(string State, long Start, long End)>();
        if (endMs <= startMs)
        {
            return pieces;
        }

        var cursor = startMs;
        foreach (var segment in segments
                     .Select(segment => (
                         Start: Math.Max(segment.StartedUnixMs, startMs),
                         End: Math.Min(segment.EndedUnixMs ?? endMs, endMs),
                         State: MachineState.Normalize(segment.State)))
                     .Where(segment => segment.End > segment.Start)
                     .OrderBy(segment => segment.Start)
                     .ThenBy(segment => segment.End))
        {
            if (segment.Start > cursor)
            {
                pieces.Add((MachineState.Offline, cursor, segment.Start));
            }

            var from = Math.Max(segment.Start, cursor);
            if (segment.End > from)
            {
                pieces.Add((segment.State, from, segment.End));
                cursor = segment.End;
            }
        }

        if (cursor < endMs)
        {
            pieces.Add((MachineState.Offline, cursor, endMs));
        }

        return pieces;
    }

    public static Dictionary<string, long> Durations(IReadOnlyList<StateSegment> segments, long startMs, long endMs)
    {
        var totals = MachineState.All.ToDictionary(state => state, _ => 0L, StringComparer.Ordinal);
        if (endMs <= startMs)
        {
            return totals;
        }

        var cursor = startMs;
        foreach (var segment in segments
                     .Select(segment => (
                         Start: Math.Max(segment.StartedUnixMs, startMs),
                         End: Math.Min(segment.EndedUnixMs ?? endMs, endMs),
                         State: MachineState.Normalize(segment.State)))
                     .Where(segment => segment.End > segment.Start)
                     .OrderBy(segment => segment.Start)
                     .ThenBy(segment => segment.End))
        {
            if (segment.Start > cursor)
            {
                totals[MachineState.Offline] += segment.Start - cursor;
            }

            var from = Math.Max(segment.Start, cursor);
            if (segment.End > from)
            {
                totals[segment.State] += segment.End - from;
                cursor = segment.End;
            }
        }

        if (cursor < endMs)
        {
            totals[MachineState.Offline] += endMs - cursor;
        }

        return totals;
    }

    /// <summary>
    /// Positive steps count as production. A drop is a counter reset: the new reading is what
    /// has accumulated since the reset, and the drop itself is not subtracted.
    /// </summary>
    public static double PartDelta(IReadOnlyList<PartSample> samples)
    {
        if (samples.Count < 2)
        {
            return 0;
        }

        var ordered = samples.OrderBy(sample => sample.TimestampUnixMs).ToList();
        double total = 0;
        var previous = ordered[0].Value;
        for (var i = 1; i < ordered.Count; i++)
        {
            var current = ordered[i].Value;
            if (current >= previous)
            {
                total += current - previous;
            }
            else
            {
                total += Math.Max(0, current);
            }

            previous = current;
        }

        return total;
    }

    public static double Ratio(long runMs, long plannedMs) =>
        plannedMs <= 0 ? 0 : Math.Round(runMs / (double)plannedMs, 4);

    public static long ScaledPlanned(long plannedMs, long lengthMs, long overlapMs)
    {
        if (plannedMs <= 0 || lengthMs <= 0 || overlapMs <= 0)
        {
            return 0;
        }

        var overlap = Math.Min(overlapMs, lengthMs);
        return (long)Math.Round(plannedMs * (overlap / (double)lengthMs), MidpointRounding.AwayFromZero);
    }

    public static List<UtilizationRow> Build(
        IReadOnlyList<DeviceRef> devices,
        IReadOnlyList<StateSegment> segments,
        IReadOnlyList<PartSample> parts,
        ShiftCalendar calendar,
        DateTimeOffset from,
        DateTimeOffset to,
        DateTimeOffset now)
    {
        var end = to > now ? now : to;
        if (end <= from)
        {
            return [];
        }

        var windows = calendar.Windows(from, end);
        var rows = new List<UtilizationRow>();
        foreach (var device in devices)
        {
            var deviceSegments = segments.Where(segment => segment.DeviceId == device.Id).ToList();
            var deviceParts = parts.Where(sample => sample.DeviceId == device.Id).ToList();
            foreach (var window in windows)
            {
                var overlapStart = window.Start > from ? window.Start : from;
                var overlapEnd = window.End < end ? window.End : end;
                if (overlapEnd <= overlapStart)
                {
                    continue;
                }

                var durations = Durations(
                    deviceSegments,
                    overlapStart.ToUnixTimeMilliseconds(),
                    overlapEnd.ToUnixTimeMilliseconds());
                var planned = ScaledPlanned(
                    window.PlannedMs,
                    window.LengthMs,
                    (long)(overlapEnd - overlapStart).TotalMilliseconds);
                var partSamples = deviceParts
                    .Where(sample => sample.TimestampUnixMs >= overlapStart.ToUnixTimeMilliseconds()
                        && sample.TimestampUnixMs < overlapEnd.ToUnixTimeMilliseconds())
                    .ToList();
                var before = deviceParts
                    .Where(sample => sample.TimestampUnixMs < overlapStart.ToUnixTimeMilliseconds())
                    .OrderByDescending(sample => sample.TimestampUnixMs)
                    .FirstOrDefault();
                if (!string.IsNullOrEmpty(before.DeviceId))
                {
                    partSamples.Insert(0, before);
                }

                rows.Add(new UtilizationRow
                {
                    DeviceId = device.Id,
                    DisplayName = device.DisplayName,
                    Workshop = device.Workshop,
                    Line = device.Line,
                    Day = window.Day.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture),
                    Shift = window.Name,
                    RunMs = durations[MachineState.Running],
                    IdleMs = durations[MachineState.Idle],
                    AlarmMs = durations[MachineState.Alarm],
                    OfflineMs = durations[MachineState.Offline],
                    StoppedMs = durations[MachineState.Stopped],
                    PlannedMs = planned,
                    Utilization = Ratio(durations[MachineState.Running], planned),
                    PartCount = PartDelta(partSamples)
                });
            }
        }

        return rows;
    }

    public static List<UtilizationRow> RollupDays(IReadOnlyList<UtilizationRow> shifts)
    {
        return shifts
            .GroupBy(row => (row.DeviceId, row.Day))
            .Select(group => Sum(group, group.Key.Day, ""))
            .OrderBy(row => row.Day, StringComparer.Ordinal)
            .ThenBy(row => row.DeviceId, StringComparer.Ordinal)
            .ToList();
    }

    public static List<UtilizationRow> RollupLines(IReadOnlyList<UtilizationRow> shifts)
    {
        return shifts
            .GroupBy(row => (Workshop: row.Workshop, row.Line, row.Day, row.Shift))
            .Select(group =>
            {
                var row = Sum(group, group.Key.Day, group.Key.Shift);
                row.DeviceId = "";
                row.DisplayName = "";
                row.Workshop = group.Key.Workshop;
                row.Line = group.Key.Line;
                return row;
            })
            .OrderBy(row => row.Day, StringComparer.Ordinal)
            .ThenBy(row => row.Line, StringComparer.Ordinal)
            .ToList();
    }

    private static UtilizationRow Sum(IEnumerable<UtilizationRow> rows, string day, string shift)
    {
        var list = rows.ToList();
        var first = list[0];
        var run = list.Sum(row => row.RunMs);
        var planned = list.Sum(row => row.PlannedMs);
        return new UtilizationRow
        {
            DeviceId = first.DeviceId,
            DisplayName = first.DisplayName,
            Workshop = first.Workshop,
            Line = first.Line,
            Day = day,
            Shift = shift,
            RunMs = run,
            IdleMs = list.Sum(row => row.IdleMs),
            AlarmMs = list.Sum(row => row.AlarmMs),
            OfflineMs = list.Sum(row => row.OfflineMs),
            StoppedMs = list.Sum(row => row.StoppedMs),
            PlannedMs = planned,
            Utilization = Ratio(run, planned),
            PartCount = list.Sum(row => row.PartCount)
        };
    }
}
