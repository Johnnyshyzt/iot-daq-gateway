using System.Collections.Concurrent;
using System.Text.Json;
using Gateway.Abstractions.Models;

namespace Gateway.Host.Acquisition;

internal sealed class ChangeOnlyFilter
{
    private readonly ConcurrentDictionary<string, string> _last = new(StringComparer.Ordinal);
    private readonly bool _enabled;

    public ChangeOnlyFilter(bool enabled)
    {
        _enabled = enabled;
    }

    public bool ShouldPublish(Observation observation)
    {
        if (!_enabled)
        {
            return true;
        }

        var key = $"{observation.DeviceId}/{observation.Point}";
        var serialized = JsonSerializer.Serialize(observation.Value);
        if (_last.TryGetValue(key, out var previous) && previous == serialized)
        {
            return false;
        }

        _last[key] = serialized;
        return true;
    }
}
