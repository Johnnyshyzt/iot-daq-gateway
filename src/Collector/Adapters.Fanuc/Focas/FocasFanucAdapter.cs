using System.Globalization;
using Gateway.Abstractions.Contracts;
using Gateway.Abstractions.Models;
using Microsoft.Extensions.Logging;

namespace Adapters.Fanuc.Focas;

/// <summary>
/// Ethernet FOCAS adapter. Collects <c>state</c> / <c>alarm</c> / <c>program</c> from a CNC.
/// Missing <c>Fwlib64.dll</c> or a dropped socket leaves the device Offline and retries on later sweeps.
/// </summary>
public sealed class FocasFanucAdapter : ISouthboundAdapter
{
    public const string Kind = "fanuc.focas";

    private static readonly TimeSpan ConnectLogThrottle = TimeSpan.FromSeconds(30);

    private readonly ILogger<FocasFanucAdapter> _logger;
    private readonly IFocasLibrary _library;
    private readonly string _host;
    private readonly int _port;
    private readonly int _timeoutMs;
    private readonly object _sync = new();
    private ushort _handle;
    private bool _connected;
    private string _lastError = "not connected";
    private DateTimeOffset _nextConnectLogUtc = DateTimeOffset.MinValue;

    public FocasFanucAdapter(
        string deviceId,
        IReadOnlyDictionary<string, object?> options,
        ILogger<FocasFanucAdapter> logger)
        : this(deviceId, options, logger, NativeFocasLibrary.Instance)
    {
    }

    internal FocasFanucAdapter(
        string deviceId,
        IReadOnlyDictionary<string, object?> options,
        ILogger<FocasFanucAdapter> logger,
        IFocasLibrary library)
    {
        DeviceId = deviceId;
        _logger = logger;
        _library = library;
        _host = OptionReader.GetString(options, "host", "192.168.1.10");
        _port = OptionReader.GetInt(options, "port", 8193);
        _timeoutMs = OptionReader.GetInt(options, "timeoutMs", 3000);
    }

    public string AdapterKind => Kind;

    public string DeviceId { get; }

    public Task ConnectAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        TryConnect();
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<Observation>> CollectAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            if (!TryConnect())
            {
                return Task.FromResult<IReadOnlyList<Observation>>([]);
            }

            return Task.FromResult(ReadPoints());
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            DropHandle($"FOCAS collect exception: {ex.Message}");
            _logger.LogWarning(ex, "FOCAS collect failed for {DeviceId}; will reconnect on a later sweep", DeviceId);
            return Task.FromResult<IReadOnlyList<Observation>>([]);
        }
    }

    public Task<DeviceHealth> GetHealthAsync(CancellationToken cancellationToken)
    {
        lock (_sync)
        {
            return Task.FromResult(new DeviceHealth
            {
                DeviceId = DeviceId,
                Status = _connected ? AdapterStatus.Online : AdapterStatus.Offline,
                Message = _connected
                    ? $"FOCAS {_host}:{_port}"
                    : _lastError,
                Timestamp = DateTimeOffset.UtcNow
            });
        }
    }

    public ValueTask DisposeAsync()
    {
        DropHandle("disposed");
        return ValueTask.CompletedTask;
    }

    private bool TryConnect()
    {
        lock (_sync)
        {
            if (_connected)
            {
                return true;
            }

            if (!_library.TryGetAvailability(out var availability))
            {
                _lastError = availability;
                LogConnectFailure(availability);
                return false;
            }

            var timeoutSeconds = FocasNative.ToTimeoutSeconds(_timeoutMs);
            var rc = _library.AllocateHandle(_host, (ushort)_port, timeoutSeconds, out var handle);
            if (rc != FocasReturn.Ok || handle == 0)
            {
                _lastError =
                    $"cnc_allclibhndl3({_host}:{_port}) failed: {FocasReturn.Describe(rc)} " +
                    $"(timeout {timeoutSeconds}s)";
                LogConnectFailure(_lastError);
                return false;
            }

            _library.SetTimeout(handle, timeoutSeconds);
            _handle = handle;
            _connected = true;
            _lastError = string.Empty;
            _logger.LogInformation(
                "FOCAS connected {DeviceId} {Host}:{Port} handle={Handle} timeout={Timeout}s",
                DeviceId,
                _host,
                _port,
                handle,
                timeoutSeconds);
            return true;
        }
    }

    private IReadOnlyList<Observation> ReadPoints()
    {
        lock (_sync)
        {
            if (!_connected)
            {
                return [];
            }

            var handle = _handle;
            var rc = _library.ReadStatus(handle, out var status);
            if (rc != FocasReturn.Ok)
            {
                return FailRead("cnc_statinfo", rc);
            }

            var state = FocasPointMapper.MapState(status);
            var quality = FocasPointMapper.QualityFor(state);
            var alarmNumber = 0;
            var alarmRc = _library.ReadFirstAlarmNumber(handle, out alarmNumber);
            if (FocasReturn.IsSessionLost(alarmRc))
            {
                return FailRead("cnc_rdalmmsg", alarmRc);
            }

            if (alarmRc != FocasReturn.Ok)
            {
                _logger.LogDebug(
                    "cnc_rdalmmsg for {DeviceId} returned {Error}; publishing alarm=0",
                    DeviceId,
                    FocasReturn.Describe(alarmRc));
                alarmNumber = 0;
            }
            else if (alarmNumber == 0 && status.Alarm != 0)
            {
                alarmNumber = status.Alarm;
            }

            var programNumber = 0;
            var programRc = _library.ReadProgramNumber(handle, out programNumber);
            if (FocasReturn.IsSessionLost(programRc))
            {
                return FailRead("cnc_rdprgnum", programRc);
            }

            if (programRc != FocasReturn.Ok)
            {
                _logger.LogDebug(
                    "cnc_rdprgnum for {DeviceId} returned {Error}; publishing program=O0000",
                    DeviceId,
                    FocasReturn.Describe(programRc));
                programNumber = 0;
            }

            var now = DateTimeOffset.UtcNow;
            var points = new List<Observation>
            {
                new()
                {
                    DeviceId = DeviceId,
                    Point = "state",
                    Value = state,
                    Timestamp = now,
                    Quality = quality
                },
                new()
                {
                    DeviceId = DeviceId,
                    Point = "alarm",
                    Value = alarmNumber,
                    Timestamp = now,
                    Quality = quality
                },
                new()
                {
                    DeviceId = DeviceId,
                    Point = "program",
                    Value = FocasPointMapper.FormatProgram(programNumber),
                    Timestamp = now,
                    Quality = "good"
                }
            };
            if (_library is IFocasSignals signals)
            {
                try
                {
                    AppendSignals(points, signals.Read(handle, status), status, state, alarmNumber, now);
                }
                catch (Exception ex) when (ex is not OutOfMemoryException)
                {
                    _logger.LogDebug(ex, "FOCAS extra signals failed for {DeviceId}", DeviceId);
                }
            }

            return points;
        }
    }

    private void AppendSignals(
        List<Observation> points,
        FocasSignalSnapshot snapshot,
        in FocasStatInfo status,
        string state,
        int alarmNumber,
        DateTimeOffset now)
    {
        points.Add(Signal("workMode", FocasPointMapper.MapWorkMode(status.Aut), now));
        points.Add(Signal("estop", status.Emergency != 0, now));
        points.Add(Signal("isRunning", string.Equals(state, "RUNNING", StringComparison.Ordinal), now));
        points.Add(Signal("alarmNumber", alarmNumber, now));
        if (snapshot.HasSpindle)
        {
            points.Add(Signal("spindleSpeed", snapshot.Spindle, now, "rpm"));
        }

        if (snapshot.HasFeed)
        {
            points.Add(Signal("feedRate", snapshot.Feed, now));
        }

        if (snapshot.HasAxes)
        {
            var x = FormatAxis(snapshot.AxisX);
            var y = FormatAxis(snapshot.AxisY);
            var z = FormatAxis(snapshot.AxisZ);
            var combined = $"X{x} Y{y} Z{z}";
            points.Add(Signal("machinePosition", combined, now, "mm"));
            points.Add(Signal("machinePositionX", snapshot.AxisX, now, "mm"));
            points.Add(Signal("machinePositionY", snapshot.AxisY, now, "mm"));
            points.Add(Signal("machinePositionZ", snapshot.AxisZ, now, "mm"));
            points.Add(Signal("absolutePosition", combined, now, "mm"));
            points.Add(Signal("absolutePositionX", snapshot.AxisX, now, "mm"));
            points.Add(Signal("absolutePositionY", snapshot.AxisY, now, "mm"));
            points.Add(Signal("absolutePositionZ", snapshot.AxisZ, now, "mm"));
        }

        if (snapshot.HasSystem)
        {
            if (!string.IsNullOrWhiteSpace(snapshot.SystemType))
            {
                points.Add(Signal("systemType", snapshot.SystemType, now));
            }

            if (!string.IsNullOrWhiteSpace(snapshot.SoftwareVersion))
            {
                points.Add(Signal("softwareVersion", snapshot.SoftwareVersion, now));
            }

            points.Add(Signal("axisCount", snapshot.AxisCount, now));
        }

        if (snapshot.HasMainProgram)
        {
            points.Add(Signal("programMain", FocasPointMapper.FormatProgram(snapshot.MainProgram), now));
        }
    }

    private Observation Signal(string point, object? value, DateTimeOffset now, string? unit = null) => new()
    {
        DeviceId = DeviceId,
        Point = point,
        Value = value,
        Timestamp = now,
        Quality = "good",
        Unit = unit
    };

    private static string FormatAxis(double value) => value.ToString("0.###", CultureInfo.InvariantCulture);

    private IReadOnlyList<Observation> FailRead(string api, short rc)
    {
        var reason = $"{api} failed: {FocasReturn.Describe(rc)}";
        DropHandleLocked(reason);
        _logger.LogWarning(
            "FOCAS {DeviceId} lost session ({Reason}); will reconnect on a later sweep",
            DeviceId,
            reason);
        return [];
    }

    private void DropHandle(string reason)
    {
        lock (_sync)
        {
            DropHandleLocked(reason);
        }
    }

    private void DropHandleLocked(string reason)
    {
        if (_connected && _handle != 0)
        {
            try
            {
                _library.FreeHandle(_handle);
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "cnc_freelibhndl failed for {DeviceId}", DeviceId);
            }
        }

        _handle = 0;
        _connected = false;
        if (!string.Equals(reason, "disposed", StringComparison.Ordinal))
        {
            _lastError = reason;
        }
    }

    private void LogConnectFailure(string reason)
    {
        var now = DateTimeOffset.UtcNow;
        if (now < _nextConnectLogUtc)
        {
            return;
        }

        _nextConnectLogUtc = now + ConnectLogThrottle;
        _logger.LogWarning(
            "FOCAS {DeviceId} offline at {Host}:{Port}: {Reason}",
            DeviceId,
            _host,
            _port,
            reason);
    }
}
