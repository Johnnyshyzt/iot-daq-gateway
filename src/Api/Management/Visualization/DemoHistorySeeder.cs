using Adapters.Cnc;
using Cnc.Catalog;
using Gateway.Abstractions.Models;
using IotDaq.Persistence;
using Studio.Contracts;

namespace Studio.Host.Visualization;

/// <summary>
/// Fills an empty database with a shop-floor day so Studio charts, alarms, and utilization
/// are populated before the first live sweeps finish. Only simulator and fake Fanuc devices
/// are synthesized, and only when history is still empty.
/// </summary>
public static class DemoHistorySeeder
{
    private static readonly string[] NumericPoints =
    [
        "partCount",
        "partCountTotal",
        "spindleSpeed",
        "spindleLoad",
        "spindleOverride",
        "feedRate",
        "feedOverride",
        "rapidOverride",
        "machinePositionX",
        "machinePositionY",
        "machinePositionZ"
    ];

    public static int SeedIfEmpty(GatewayPersistence database, ConfigBundle bundle)
    {
        if (string.Equals(database.GetSetting("demoHistorySeeded"), "1", StringComparison.Ordinal))
        {
            return 0;
        }

        if (database.HasHistory())
        {
            database.SetSetting("demoHistorySeeded", "1");
            return 0;
        }

        var devices = bundle.Devices
            .Where(device => device.Spec.Enabled && IsDemoAdapter(device.Spec.Adapter))
            .ToList();
        if (devices.Count == 0 || devices.All(device => !device.Spec.Adapter.EndsWith(".sim", StringComparison.Ordinal)))
        {
            return 0;
        }

        var now = DateTimeOffset.UtcNow;
        var start = now.AddHours(-20);
        var written = 0;
        foreach (var device in devices)
        {
            var brand = CncCatalog.Current.BrandOfAdapter(device.Spec.Adapter)
                ?? device.Spec.BrandId
                ?? "fanuc";
            var points = PointIds(bundle, device);
            var batch = new List<Observation>(2048);
            string? lastState = null;
            for (var cursor = start; cursor <= now; cursor = cursor.AddSeconds(10))
            {
                var sample = device.Spec.Adapter.EndsWith(".sim", StringComparison.Ordinal)
                    ? BrandSimulatorAdapter.Sample(device.Metadata.Id, brand, cursor, points)
                    : FakeSample(device.Metadata.Id, brand, cursor);
                var state = sample.FirstOrDefault(row => string.Equals(row.Point, "state", StringComparison.OrdinalIgnoreCase));
                var stateText = state?.Value as string;
                var changed = !string.Equals(stateText, lastState, StringComparison.Ordinal);
                var numericTick = cursor.ToUnixTimeSeconds() % 600 == 0 || cursor == start;
                foreach (var observation in sample)
                {
                    var point = observation.Point;
                    var keep = changed && point is "state" or "alarm" or "alarmNumber" or "program" or "estop";
                    if (!keep && numericTick && NumericPoints.Contains(point, StringComparer.OrdinalIgnoreCase))
                    {
                        keep = true;
                    }

                    if (keep)
                    {
                        batch.Add(observation);
                    }
                }

                lastState = stateText;
                if (batch.Count >= 2000)
                {
                    database.Write(batch);
                    written += batch.Count;
                    batch = new List<Observation>(2048);
                }
            }

            if (batch.Count > 0)
            {
                database.Write(batch);
                written += batch.Count;
            }
        }

        database.SetSetting("demoHistorySeeded", "1");
        return written;
    }

    private static bool IsDemoAdapter(string adapter) =>
        adapter.EndsWith(".sim", StringComparison.OrdinalIgnoreCase)
        || string.Equals(adapter, "fanuc.fake", StringComparison.OrdinalIgnoreCase);

    private static HashSet<string> PointIds(ConfigBundle bundle, DeviceDocument device)
    {
        var template = bundle.PointTemplates.FirstOrDefault(item =>
            string.Equals(item.Metadata.Id, device.Spec.PointTemplateId, StringComparison.OrdinalIgnoreCase));
        var set = bundle.PointSets.FirstOrDefault(item =>
            string.Equals(item.Metadata.DeviceId, device.Metadata.Id, StringComparison.OrdinalIgnoreCase));
        var ids = PointExpansion.EffectivePoints(template, set)
            .Where(point => point.Enabled)
            .Select(point => point.Id);
        return new HashSet<string>(ids, StringComparer.OrdinalIgnoreCase);
    }

    private static IReadOnlyList<Observation> FakeSample(string deviceId, string brandId, DateTimeOffset now)
    {
        var snapshot = MachineSimulation.At(now, deviceId, brandId);
        return
        [
            Point(deviceId, "state", snapshot.State, now, snapshot.Alarm ? "uncertain" : "good"),
            Point(deviceId, "alarm", snapshot.Alarm ? snapshot.AlarmText : "0", now, snapshot.Alarm ? "uncertain" : "good"),
            Point(deviceId, "program", snapshot.Program, now, "good")
        ];
    }

    private static Observation Point(string deviceId, string point, object value, DateTimeOffset now, string quality) => new()
    {
        DeviceId = deviceId,
        Point = point,
        Value = value,
        Timestamp = now,
        Quality = quality
    };
}
