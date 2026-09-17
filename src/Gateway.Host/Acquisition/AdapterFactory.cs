using Gateway.Abstractions.Configuration;
using Gateway.Abstractions.Contracts;

namespace Gateway.Host.Acquisition;

internal static class AdapterFactory
{
    public static IReadOnlyList<ISouthboundAdapter> Create(
        GatewayConfiguration config,
        IEnumerable<ISouthboundAdapterFactory> factories)
    {
        var byKind = factories.ToDictionary(f => f.AdapterKind, f => f, StringComparer.OrdinalIgnoreCase);
        var adapters = new List<ISouthboundAdapter>();

        foreach (var device in config.Devices.Where(d => d.Enabled))
        {
            if (!byKind.TryGetValue(device.Adapter, out var factory))
            {
                var known = string.Join(", ", byKind.Keys.Order(StringComparer.Ordinal));
                throw new InvalidOperationException(
                    $"Unknown adapter '{device.Adapter}' for device '{device.Id}'. Known: {known}.");
            }

            adapters.Add(factory.Create(device));
        }

        if (adapters.Count == 0)
        {
            throw new InvalidOperationException("No enabled devices in configuration.");
        }

        return adapters;
    }
}
