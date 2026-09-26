using Cnc.Catalog;
using Gateway.Abstractions.Configuration;
using Gateway.Abstractions.Contracts;
using Microsoft.Extensions.Logging;

namespace Adapters.Cnc.Drivers;

public static class CncDriverFactory
{
    public static ISouthboundAdapter Create(DeviceBinding binding, CatalogAdapter adapter, ILoggerFactory loggerFactory)
    {
        var logger = loggerFactory.CreateLogger("Adapters.Cnc." + adapter.Id);
        return adapter.Protocol switch
        {
            "opcua" => new OpcUaDriver(binding, adapter, logger),
            "mtconnect" => new MtConnectDriver(binding, adapter, logger),
            "lsv2" => new Lsv2Driver(binding, adapter, logger),
            "haas-q" => new HaasQDriver(binding, adapter, logger),
            "modbus" => new ModbusDriver(binding, adapter, logger),
            "ftp" => new FtpShareDriver(binding, adapter, logger),
            "brother" => new BrotherCncDriver(binding, adapter, logger),
            "ezsocket" or "syntec" or "gsk" or "knd" or "hnc" or "baoyuan" or "kede" or "jdsoft" or "mitsubishi" =>
                new VendorSdkDriver(binding, adapter, logger),
            _ => new Phase2DriverStub(binding, adapter, logger)
        };
    }
}
