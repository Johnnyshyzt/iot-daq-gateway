using System.Globalization;
using Cnc.Catalog;
using Gateway.Abstractions.Configuration;
using Gateway.Abstractions.Contracts;
using Gateway.Abstractions.Models;
using Microsoft.Extensions.Logging;

namespace Adapters.Cnc;

public sealed class BrandSimulatorAdapter : ISouthboundAdapter
{
    private readonly ILogger _logger;
    private readonly CatalogBrand _brand;
    private readonly string _host;
    private readonly int _port;
    private readonly string[]? _points;
    private bool _connected;

    public BrandSimulatorAdapter(DeviceBinding binding, CatalogAdapter adapter, ILogger logger)
    {
        _logger = logger;
        AdapterKind = adapter.Id;
        DeviceId = binding.Id;
        var catalog = CncCatalog.Current;
        _brand = catalog.FindBrand(adapter.BrandId)
            ?? throw new InvalidOperationException($"模拟器 {adapter.Id} 没有对应品牌。");
        _host = Option(binding.Options, "host") ?? "127.0.0.1";
        _port = OptionInt(binding.Options, "port", 8193);
        _points = ReadPoints(binding.Options);
    }

    public string AdapterKind { get; }

    public string DeviceId { get; }

    public Task ConnectAsync(CancellationToken cancellationToken)
    {
        _connected = true;
        _logger.LogInformation(
            "Simulator {Adapter} {DeviceId} ready ({Host}:{Port}, brand {Brand})",
            AdapterKind,
            DeviceId,
            _host,
            _port,
            _brand.Id);
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<Observation>> CollectAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!_connected)
        {
            throw new InvalidOperationException($"Adapter {DeviceId} is not connected.");
        }

        return Task.FromResult(Sample(DeviceId, _brand.Id, DateTimeOffset.UtcNow, _points));
    }

    public static IReadOnlyList<Observation> Sample(string deviceId, string brandId, DateTimeOffset now, IReadOnlyCollection<string>? only)
    {
        var catalog = CncCatalog.Current;
        var brand = catalog.FindBrand(brandId) ?? throw new InvalidOperationException($"未知品牌 {brandId}");
        var snapshot = MachineSimulation.At(now, deviceId, brand.Id);
        var wanted = only is null
            ? null
            : new HashSet<string>(only.Where(id => !string.IsNullOrWhiteSpace(id)), StringComparer.OrdinalIgnoreCase);
        var rows = new List<Observation>();
        foreach (var itemId in brand.ItemIds)
        {
            if (wanted is not null && !wanted.Contains(itemId))
            {
                continue;
            }

            var item = catalog.FindItem(itemId);
            if (item is null)
            {
                continue;
            }

            rows.Add(new Observation
            {
                DeviceId = deviceId,
                Point = item.Id,
                Value = ValueFor(snapshot, item),
                Timestamp = now,
                Quality = snapshot.Alarm && item.Id is "state" or "alarm" ? "uncertain" : "good",
                Unit = string.IsNullOrEmpty(item.Unit) ? null : item.Unit
            });
        }

        return rows;
    }

    public static object? ValueFor(MachineSimulation.Snapshot snapshot, CatalogItem item)
    {
        var key = item.BrandSpecific && item.Id.Contains('_', StringComparison.Ordinal)
            ? item.Id[(item.Id.IndexOf('_', StringComparison.Ordinal) + 1)..]
            : item.Id;
        var canonical = key switch
        {
            "state" => (object?)snapshot.State,
            "workMode" => snapshot.WorkMode,
            "alarm" => snapshot.AlarmText,
            "alarmNumber" => snapshot.Alarm ? "EX100" : "0",
            "estop" => snapshot.Estop,
            "isRunning" => snapshot.State == "RUNNING",
            "isReset" => snapshot.State == "IDLE",
            "isCutting" => snapshot.State == "RUNNING",
            "program" => snapshot.Program,
            "programMain" => snapshot.ProgramMain,
            "programComment" => snapshot.ProgramComment,
            "programBlock" => snapshot.State == "RUNNING" ? "G01 X10. F200" : "",
            "programLine" => snapshot.ProgramLine,
            "programPath" => "/nc/" + snapshot.Program,
            "partCount" => snapshot.PartCount,
            "partCountTotal" => snapshot.PartCountTotal,
            "partCountTarget" => snapshot.PartCountTotal + 10,
            "cycleTime" => snapshot.CycleSecondsValue,
            "powerOnTime" => snapshot.PowerOnSeconds,
            "runTime" => snapshot.RunSeconds,
            "cutTime" => snapshot.CutSeconds,
            "remainTime" => snapshot.State == "RUNNING" ? Math.Max(0, 50 - snapshot.CycleSecondsValue) : 0,
            "spindleSpeed" => snapshot.SpindleSpeed,
            "spindleSpeedCmd" => snapshot.SpindleSpeedCmd,
            "spindleOverride" => snapshot.SpindleOverride,
            "spindleLoad" => snapshot.SpindleLoad,
            "spindleTemp" => snapshot.TempC + 4,
            "feedRate" => snapshot.FeedRate,
            "feedRateCmd" => snapshot.FeedRateCmd,
            "feedOverride" => snapshot.FeedOverride,
            "rapidOverride" => snapshot.RapidOverride,
            "machinePosition" => FormatAxes(snapshot),
            "relativePosition" => FormatAxes(snapshot),
            "absolutePosition" => FormatAxes(snapshot),
            "distanceToGo" => snapshot.State == "RUNNING" ? "X1.200 Y0.000 Z0.400" : "X0.000 Y0.000 Z0.000",
            "machinePositionX" or "absolutePositionX" or "relativePositionX" or "actualPositionX" => snapshot.AxisX,
            "machinePositionY" or "absolutePositionY" or "relativePositionY" or "actualPositionY" => snapshot.AxisY,
            "machinePositionZ" or "absolutePositionZ" or "relativePositionZ" or "actualPositionZ" => snapshot.AxisZ,
            "distanceToGoX" => snapshot.State == "RUNNING" ? 1.2 : 0,
            "distanceToGoY" => 0d,
            "distanceToGoZ" => snapshot.State == "RUNNING" ? 0.4 : 0,
            "axisLoadX" or "axisLoadY" or "axisLoadZ" => snapshot.Load,
            "toolNumber" => snapshot.State == "RUNNING" ? "T01" : "T00",
            "toolNext" => "T02",
            "serialNumber" => "SIM-" + item.BrandId,
            "softwareVersion" => "sim-1.0",
            "systemType" => "CNC",
            "axisCount" => 3,
            "servoLoad" => snapshot.Load,
            _ => null
        };
        if (canonical is not null || !item.BrandSpecific)
        {
            return canonical ?? TypedFallback(item, snapshot);
        }

        return TypedFallback(item, snapshot);
    }

    public Task<DeviceHealth> GetHealthAsync(CancellationToken cancellationToken)
    {
        return Task.FromResult(new DeviceHealth
        {
            DeviceId = DeviceId,
            Status = _connected ? AdapterStatus.Online : AdapterStatus.Offline,
            Message = _connected ? $"{_brand.NameZh} 模拟器已连接" : "模拟器未连接",
            Timestamp = DateTimeOffset.UtcNow
        });
    }

    public ValueTask DisposeAsync()
    {
        _connected = false;
        return ValueTask.CompletedTask;
    }

    private static object? TypedFallback(CatalogItem item, MachineSimulation.Snapshot snapshot)
    {
        return item.DataType switch
        {
            "bool" => snapshot.Alarm,
            "number" => item.Category switch
            {
                "temperature" => snapshot.TempC,
                "load" or "spindle" => snapshot.Load,
                "time" => snapshot.RunSeconds,
                "counter" => snapshot.PartCount,
                "axis" => snapshot.AxisX,
                "feed" => snapshot.FeedRate,
                _ => snapshot.State == "RUNNING" ? 1 : 0
            },
            _ => item.Category switch
            {
                "alarm" => snapshot.AlarmText,
                "program" => snapshot.Program,
                "state" => snapshot.State,
                "system-info" => "SIM",
                _ => snapshot.State
            }
        };
    }

    private static string FormatAxes(MachineSimulation.Snapshot snapshot) =>
        string.Create(CultureInfo.InvariantCulture, $"X{snapshot.AxisX:0.000} Y{snapshot.AxisY:0.000} Z{snapshot.AxisZ:0.000}");

    private static string[]? ReadPoints(IReadOnlyDictionary<string, object?> options)
    {
        if (!options.TryGetValue("points", out var value) || value is null)
        {
            return null;
        }

        if (value is string text)
        {
            return text.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        }

        if (value is System.Collections.IEnumerable list and not string)
        {
            var parts = new List<string>();
            foreach (var item in list)
            {
                var part = Convert.ToString(item, CultureInfo.InvariantCulture);
                if (!string.IsNullOrWhiteSpace(part))
                {
                    parts.Add(part);
                }
            }

            return parts.ToArray();
        }

        return null;
    }

    private static string? Option(IReadOnlyDictionary<string, object?> options, string key)
    {
        if (!options.TryGetValue(key, out var value) || value is null)
        {
            return null;
        }

        return Convert.ToString(value, CultureInfo.InvariantCulture);
    }

    private static int OptionInt(IReadOnlyDictionary<string, object?> options, string key, int fallback)
    {
        var text = Option(options, key);
        return int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var number) ? number : fallback;
    }
}
