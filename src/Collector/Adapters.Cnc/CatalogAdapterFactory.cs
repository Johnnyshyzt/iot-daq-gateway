using Adapters.Cnc.Drivers;
using Cnc.Catalog;
using Gateway.Abstractions.Configuration;
using Gateway.Abstractions.Contracts;
using Microsoft.Extensions.Logging;

namespace Adapters.Cnc;

public sealed class CatalogAdapterFactory(CatalogAdapter adapter, ILoggerFactory loggerFactory) : ISouthboundAdapterFactory
{
    public string AdapterKind => adapter.Id;

    public ISouthboundAdapter Create(DeviceBinding binding)
    {
        if (string.Equals(adapter.Kind, "simulator", StringComparison.Ordinal))
        {
            return new BrandSimulatorAdapter(binding, adapter, loggerFactory.CreateLogger<BrandSimulatorAdapter>());
        }

        return CncDriverFactory.Create(binding, adapter, loggerFactory);
    }
}
