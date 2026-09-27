using IotDaq.Persistence;
using IotDaq.Persistence.Shop;
using Studio.Host.Config;

namespace Studio.Host.Shop;

public sealed class ToolLifeService(GatewayPersistence database, ConfigStore store)
{
    public int Tick(string? deviceId)
    {
        var ids = new HashSet<string>(StringComparer.Ordinal);
        if (!string.IsNullOrWhiteSpace(deviceId))
        {
            ids.Add(deviceId);
        }
        else
        {
            foreach (var device in store.ReadPublished().Devices)
            {
                ids.Add(device.Metadata.Id);
            }

            foreach (var life in database.ListToolLife(null))
            {
                ids.Add(life.DeviceId);
            }
        }

        var count = 0;
        foreach (var id in ids)
        {
            var samples = database.Latest(id);
            if (samples.Count == 0)
            {
                continue;
            }

            database.AdvanceTool(id, Signal(samples));
            count++;
        }

        return count;
    }

    public static ToolSignal Signal(IReadOnlyList<SampleView> samples)
    {
        string? Text(string point)
        {
            return samples.FirstOrDefault(sample => sample.PointId.Equals(point, StringComparison.OrdinalIgnoreCase))?.Value;
        }

        double? Number(string point)
        {
            return samples.FirstOrDefault(sample => sample.PointId.Equals(point, StringComparison.OrdinalIgnoreCase))?.NumericValue;
        }

        var computed = samples.Any(sample => sample.Computed && sample.PointId is "toolNumber" or "partCount" or "partCountTotal" or "state" or "isRunning" or "cycleTime");
        var unix = samples.Max(sample => sample.TimestampUnixMs);
        return new ToolSignal(
            Text("toolNumber"),
            Number("partCount") ?? Number("partCountTotal"),
            ToolLifeMath.IsCutting(Text("state"), Text("isRunning")),
            Number("cycleTime"),
            unix,
            computed ? "computed" : "points");
    }
}
