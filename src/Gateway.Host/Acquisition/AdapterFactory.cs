using Gateway.Abstractions.Configuration;
using Gateway.Abstractions.Contracts;

namespace Gateway.Host.Acquisition;

internal static class AdapterFactory
{
    public static IReadOnlyList<ISouthboundAdapter> Create(
        GatewayConfiguration config,
        IEnumerable<ISouthboundAdapterFactory> factories)
    {
        EnsureKnownKinds(config, factories);
        var byKind = factories.ToDictionary(f => f.AdapterKind, f => f, StringComparer.OrdinalIgnoreCase);
        var adapters = new List<ISouthboundAdapter>();

        foreach (var device in config.Devices.Where(d => d.Enabled))
        {
            adapters.Add(byKind[device.Adapter].Create(device));
        }

        return adapters;
    }

    public static void EnsureKnownKinds(
        GatewayConfiguration config,
        IEnumerable<ISouthboundAdapterFactory> factories)
    {
        var byKind = factories.ToDictionary(f => f.AdapterKind, f => f, StringComparer.OrdinalIgnoreCase);
        foreach (var device in config.Devices)
        {
            if (!byKind.TryGetValue(device.Adapter, out _))
            {
                var known = string.Join(", ", byKind.Keys.Order(StringComparer.Ordinal));
                throw new InvalidOperationException(
                    $"Unknown adapter '{device.Adapter}' for device '{device.Id}'. Known: {known}.");
            }
        }
    }
}
