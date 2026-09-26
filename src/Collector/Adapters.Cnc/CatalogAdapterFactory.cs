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

        var logger = loggerFactory.CreateLogger<Phase2DriverStub>();
        return adapter.Protocol switch
        {
            "opcua" => new SiemensOpcUaDriver(binding, adapter, logger),
            "mtconnect" => new MtConnectDriver(binding, adapter, logger),
            "lsv2" => new HeidenhainLsv2Driver(binding, adapter, logger),
            "mitsubishi" => new MitsubishiDriver(binding, adapter, logger),
            "syntec" => new SyntecDriver(binding, adapter, logger),
            "gsk" => new GskDriver(binding, adapter, logger),
            "knd" => new KndDriver(binding, adapter, logger),
            "hnc" => new HuazhongDriver(binding, adapter, logger),
            "brother" => new BrotherDriver(binding, adapter, logger),
            "fagor" => new FagorDriver(binding, adapter, logger),
            "kede" => new KedeDriver(binding, adapter, logger),
            "jdsoft" => new JdSoftDriver(binding, adapter, logger),
            "delta" => new DeltaDriver(binding, adapter, logger),
            "baoyuan" => new BaoyuanDriver(binding, adapter, logger),
            "ftp" => new FtpShareDriver(binding, adapter, logger),
            _ => new Phase2DriverStub(binding, adapter, logger)
        };
    }
}
