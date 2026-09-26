using Cnc.Catalog;
using Gateway.Abstractions.Configuration;
using Microsoft.Extensions.Logging;

namespace Adapters.Cnc;

/// <summary>Phase 2: Siemens OPC UA session. Do not add a vendor SDK to this repo.</summary>
public sealed class SiemensOpcUaDriver(DeviceBinding binding, CatalogAdapter adapter, ILogger logger)
    : Phase2DriverStub(binding, adapter, logger);

/// <summary>Phase 2: Mazak and Haas MTConnect current/sample. Do not add a vendor SDK.</summary>
public sealed class MtConnectDriver(DeviceBinding binding, CatalogAdapter adapter, ILogger logger)
    : Phase2DriverStub(binding, adapter, logger);

/// <summary>Phase 2: Heidenhain LSV2. Do not add a vendor SDK.</summary>
public sealed class HeidenhainLsv2Driver(DeviceBinding binding, CatalogAdapter adapter, ILogger logger)
    : Phase2DriverStub(binding, adapter, logger);

/// <summary>Phase 2: Mitsubishi family, including Citizen and DMG MORI variants.</summary>
public sealed class MitsubishiDriver(DeviceBinding binding, CatalogAdapter adapter, ILogger logger)
    : Phase2DriverStub(binding, adapter, logger);

/// <summary>Phase 2: Syntec controller protocol.</summary>
public sealed class SyntecDriver(DeviceBinding binding, CatalogAdapter adapter, ILogger logger)
    : Phase2DriverStub(binding, adapter, logger);

/// <summary>Phase 2: GSK controller protocol.</summary>
public sealed class GskDriver(DeviceBinding binding, CatalogAdapter adapter, ILogger logger)
    : Phase2DriverStub(binding, adapter, logger);

/// <summary>Phase 2: KND controller protocol.</summary>
public sealed class KndDriver(DeviceBinding binding, CatalogAdapter adapter, ILogger logger)
    : Phase2DriverStub(binding, adapter, logger);

/// <summary>Phase 2: Huazhong HNC controller protocol.</summary>
public sealed class HuazhongDriver(DeviceBinding binding, CatalogAdapter adapter, ILogger logger)
    : Phase2DriverStub(binding, adapter, logger);

/// <summary>Phase 2: Brother controller protocol.</summary>
public sealed class BrotherDriver(DeviceBinding binding, CatalogAdapter adapter, ILogger logger)
    : Phase2DriverStub(binding, adapter, logger);

/// <summary>Phase 2: Fagor controller protocol.</summary>
public sealed class FagorDriver(DeviceBinding binding, CatalogAdapter adapter, ILogger logger)
    : Phase2DriverStub(binding, adapter, logger);

/// <summary>Phase 2: KEDE controller protocol.</summary>
public sealed class KedeDriver(DeviceBinding binding, CatalogAdapter adapter, ILogger logger)
    : Phase2DriverStub(binding, adapter, logger);

/// <summary>Phase 2: JDSoft controller protocol.</summary>
public sealed class JdSoftDriver(DeviceBinding binding, CatalogAdapter adapter, ILogger logger)
    : Phase2DriverStub(binding, adapter, logger);

/// <summary>Phase 2: Delta controller protocol.</summary>
public sealed class DeltaDriver(DeviceBinding binding, CatalogAdapter adapter, ILogger logger)
    : Phase2DriverStub(binding, adapter, logger);

/// <summary>Phase 2: Baoyuan / LNC controller protocol.</summary>
public sealed class BaoyuanDriver(DeviceBinding binding, CatalogAdapter adapter, ILogger logger)
    : Phase2DriverStub(binding, adapter, logger);

/// <summary>Phase 2: generic FTP or file-share drop (Sheet2 "Other").</summary>
public sealed class FtpShareDriver(DeviceBinding binding, CatalogAdapter adapter, ILogger logger)
    : Phase2DriverStub(binding, adapter, logger);
