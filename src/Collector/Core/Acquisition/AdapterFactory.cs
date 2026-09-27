using Gateway.Abstractions.Configuration;
using Gateway.Abstractions.Contracts;

namespace Gateway.Host.Acquisition;

internal static class AdapterFactory
{
    public static ISouthboundAdapter CreateOne(
        DeviceBinding device,
        IEnumerable<ISouthboundAdapterFactory> factories)
    {
        var byKind = factories.ToDictionary(factory => factory.AdapterKind, factory => factory, StringComparer.OrdinalIgnoreCase);
        if (!byKind.TryGetValue(device.Adapter, out var factory))
        {
            var known = string.Join(", ", byKind.Keys.Order(StringComparer.Ordinal));
            throw new InvalidOperationException(
                $"Unknown adapter '{device.Adapter}' for device '{device.Id}'. Known: {known}.");
        }

        return factory.Create(device);
    }

    public static List<ISouthboundAdapter> Create(
        GatewayConfiguration config,
        IEnumerable<ISouthboundAdapterFactory> factories)
    {
        var adapters = new List<ISouthboundAdapter>();
        foreach (var device in config.Devices.Where(item => item.Enabled))
        {
            adapters.Add(CreateOne(device, factories));
        }

        if (adapters.Count == 0)
        {
            throw new InvalidOperationException("No enabled devices in configuration.");
        }

        return adapters;
    }
}
