using System.Globalization;
using Gateway.Abstractions.Models;

namespace IotDaq.Persistence.Visualization;

/// <summary>
/// Turns a batch of observations into latest values, history, alarm episodes, and state transitions.
/// Callers load the open rows, apply the batch, then save the context.
/// </summary>
public sealed class SampleIngest
{
    private readonly Dictionary<string, SampleLatestRow> _latest = new(StringComparer.Ordinal);
    private readonly Dictionary<string, AlarmRow> _openAlarms = new(StringComparer.Ordinal);
    private readonly Dictionary<string, StateTransitionRow> _openStates = new(StringComparer.Ordinal);

    public List<SampleLatestRow> NewLatest { get; } = [];

    public List<SampleHistoryRow> History { get; } = [];

    public List<AlarmRow> NewAlarms { get; } = [];

    public List<StateTransitionRow> NewTransitions { get; } = [];

    public static SampleIngest Load(GatewayDbContext db, IReadOnlyCollection<string> deviceIds)
    {
        var ingest = new SampleIngest();
        if (deviceIds.Count == 0)
        {
            return ingest;
        }

        foreach (var row in db.SampleLatest.Where(row => deviceIds.Contains(row.DeviceId)))
        {
            ingest._latest[Key(row.DeviceId, row.PointId)] = row;
        }

        foreach (var row in db.Alarms.Where(row => deviceIds.Contains(row.DeviceId) && row.Active))
        {
            ingest._openAlarms[Key(row.DeviceId, row.PointId)] = row;
        }

        foreach (var row in db.StateTransitions.Where(row => deviceIds.Contains(row.DeviceId) && row.EndedUnixMs == null))
        {
            ingest._openStates[row.DeviceId] = row;
        }

        return ingest;
    }

    public void Apply(IReadOnlyList<Observation> observations)
    {
        foreach (var group in observations.GroupBy(observation => observation.DeviceId, StringComparer.Ordinal))
        {
            var batch = group.OrderBy(observation => observation.Timestamp).ToList();
            var specific = batch
                .Select(observation => (observation, Text: FormatValue(observation.Value)))
                .Where(item => AlarmLogic.IsSpecificPoint(item.observation.Point) && AlarmLogic.IsActive(item.observation.Point, item.Text))
                .Select(item => item.observation)
                .ToList();
            var preferred = specific.FirstOrDefault(observation =>
                    string.Equals(observation.Point, "alarm", StringComparison.OrdinalIgnoreCase))
                ?? specific.FirstOrDefault();
            var codeSource = batch.FirstOrDefault(observation =>
                string.Equals(observation.Point, "alarmNumber", StringComparison.OrdinalIgnoreCase)
                || observation.Point.EndsWith("_warningNumber", StringComparison.Ordinal));

            foreach (var observation in batch)
            {
                var text = FormatValue(observation.Value);
                var when = observation.Timestamp.ToUnixTimeMilliseconds();
                TouchLatest(observation, text, when);
                History.Add(new SampleHistoryRow
                {
                    Id = Guid.NewGuid().ToString("N"),
                    DeviceId = observation.DeviceId,
                    PointId = observation.Point,
                    ValueText = text,
                    NumericValue = ToNumber(observation.Value),
                    Quality = observation.Quality,
                    Unit = observation.Unit,
                    TimestampUnixMs = when
                });

                if (string.Equals(observation.Point, "state", StringComparison.OrdinalIgnoreCase))
                {
                    var normalized = string.Equals(observation.Quality, "bad", StringComparison.OrdinalIgnoreCase)
                        ? MachineState.Offline
                        : MachineState.Normalize(text);
                    TouchState(observation.DeviceId, normalized, text ?? "", when);
                }

                ApplyAlarm(observation, text, when, preferred, codeSource);
            }
        }
    }

    public bool ApplyStatus(string deviceId, string status, DateTimeOffset timestamp)
    {
        var normalized = MachineState.FromConnection(status);
        if (normalized.Length == 0 || string.IsNullOrWhiteSpace(deviceId))
        {
            return false;
        }

        return TouchState(deviceId, normalized, status, timestamp.ToUnixTimeMilliseconds());
    }

    public void Attach(GatewayDbContext db)
    {
        if (NewLatest.Count > 0)
        {
            db.SampleLatest.AddRange(NewLatest);
        }

        if (History.Count > 0)
        {
            db.SampleHistory.AddRange(History);
        }

        if (NewAlarms.Count > 0)
        {
            db.Alarms.AddRange(NewAlarms);
        }

        if (NewTransitions.Count > 0)
        {
            db.StateTransitions.AddRange(NewTransitions);
        }
    }

    private void TouchLatest(Observation observation, string? text, long when)
    {
        var key = Key(observation.DeviceId, observation.Point);
        if (!_latest.TryGetValue(key, out var latest))
        {
            latest = new SampleLatestRow
            {
                DeviceId = observation.DeviceId,
                PointId = observation.Point
            };
            _latest[key] = latest;
            NewLatest.Add(latest);
        }

        latest.ValueText = text;
        latest.NumericValue = ToNumber(observation.Value);
        latest.Quality = observation.Quality;
        latest.Unit = observation.Unit;
        latest.TimestampUnixMs = when;
    }

    private void ApplyAlarm(Observation observation, string? text, long when, Observation? preferred, Observation? codeSource)
    {
        var active = AlarmLogic.IsActive(observation.Point, text);
        if (string.Equals(observation.Point, "state", StringComparison.OrdinalIgnoreCase))
        {
            if (active && preferred is not null)
            {
                Close(observation.DeviceId, observation.Point, when);
                return;
            }
        }
        else if (preferred is not null
            && !string.Equals(observation.Point, preferred.Point, StringComparison.OrdinalIgnoreCase)
            && AlarmLogic.IsSpecificPoint(observation.Point)
            && !string.Equals(observation.Point, "estop", StringComparison.OrdinalIgnoreCase))
        {
            if (!active)
            {
                Close(observation.DeviceId, observation.Point, when);
            }

            return;
        }

        if (!active)
        {
            Close(observation.DeviceId, observation.Point, when);
            return;
        }

        var message = text ?? "";
        var code = AlarmLogic.CodeFrom(observation.Point, message);
        if (codeSource is not null && string.Equals(observation.Point, preferred?.Point, StringComparison.OrdinalIgnoreCase))
        {
            var fromNumber = AlarmLogic.CodeFrom(codeSource.Point, FormatValue(codeSource.Value));
            if (!string.Equals(fromNumber, codeSource.Point, StringComparison.Ordinal))
            {
                code = fromNumber;
            }
        }

        var key = Key(observation.DeviceId, observation.Point);
        if (_openAlarms.TryGetValue(key, out var current)
            && string.Equals(current.Message, message, StringComparison.Ordinal)
            && string.Equals(current.Code, code, StringComparison.Ordinal))
        {
            return;
        }

        Close(observation.DeviceId, observation.Point, when);
        var created = new AlarmRow
        {
            Id = Guid.NewGuid().ToString("N"),
            DeviceId = observation.DeviceId,
            PointId = observation.Point,
            Code = code,
            Message = message,
            Severity = AlarmLogic.SeverityFor(observation.Point),
            Active = true,
            RaisedUnixMs = when
        };
        _openAlarms[key] = created;
        NewAlarms.Add(created);
    }

    private void Close(string deviceId, string point, long when)
    {
        var key = Key(deviceId, point);
        if (!_openAlarms.TryGetValue(key, out var row))
        {
            return;
        }

        row.Active = false;
        row.ClearedUnixMs = when;
        row.DurationMs = Math.Max(0, when - row.RaisedUnixMs);
        _openAlarms.Remove(key);
    }

    private bool TouchState(string deviceId, string state, string raw, long when)
    {
        if (_openStates.TryGetValue(deviceId, out var open))
        {
            if (string.Equals(open.State, state, StringComparison.Ordinal))
            {
                return false;
            }

            if (when < open.StartedUnixMs)
            {
                return false;
            }

            open.EndedUnixMs = when;
        }

        var created = new StateTransitionRow
        {
            Id = Guid.NewGuid().ToString("N"),
            DeviceId = deviceId,
            State = state,
            RawValue = raw,
            StartedUnixMs = when
        };
        _openStates[deviceId] = created;
        NewTransitions.Add(created);
        return true;
    }

    private static string Key(string deviceId, string point) => deviceId + "\n" + point;

    public static string? FormatValue(object? value) => value switch
    {
        null => null,
        string text => text,
        bool flag => flag ? "true" : "false",
        IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
        _ => value.ToString()
    };

    public static double? ToNumber(object? value) => value switch
    {
        null => null,
        bool => null,
        string => null,
        byte number => number,
        short number => number,
        int number => number,
        long number => number,
        float number => number,
        double number => number,
        decimal number => (double)number,
        _ => null
    };
}
