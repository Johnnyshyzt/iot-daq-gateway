using Cnc.Catalog;

namespace Adapters.Cnc.Drivers;

public static class DriverCatalog
{
    public const string Supported = "supported";
    public const string ViaSdk = "viaSdk";
    public const string NotAvailable = "notAvailable";

    private static readonly HashSet<string> SiemensOpc = Set(
        "state", "workMode", "program", "programBlock", "partCount",
        "feedRate", "feedRateCmd", "feedOverride",
        "spindleSpeed", "spindleSpeedCmd", "spindleOverride",
        "machinePosition", "machinePositionX", "machinePositionY", "machinePositionZ",
        "toolNumber", "alarm");

    private static readonly HashSet<string> MtConnect = Set(
        "state", "workMode", "estop", "alarm", "program", "programComment", "programBlock", "programLine",
        "partCount", "partCountTotal", "partCountTarget",
        "feedRate", "feedRateCmd", "feedOverride",
        "spindleSpeed", "spindleSpeedCmd", "spindleOverride",
        "machinePosition", "machinePositionX", "machinePositionY", "machinePositionZ",
        "absolutePosition", "absolutePositionX", "absolutePositionY", "absolutePositionZ",
        "toolNumber");

    private static readonly HashSet<string> Lsv2 = Set(
        "state", "workMode", "isRunning", "program", "programMain", "programLine",
        "feedOverride", "spindleOverride", "rapidOverride",
        "alarm", "alarmNumber", "toolNumber",
        "machinePosition", "machinePositionX", "machinePositionY", "machinePositionZ",
        "softwareVersion");

    private static readonly HashSet<string> HaasQ = Set(
        "serialNumber", "softwareVersion", "systemType", "workMode", "state", "toolNumber",
        "powerOnTime", "runTime", "cycleTime", "partCount", "partCountTotal", "program");

    private static readonly HashSet<string> Modbus = Set(
        "state", "alarm", "alarmNumber", "program", "partCount", "partCountTotal",
        "spindleSpeed", "spindleSpeedCmd", "spindleOverride",
        "feedRate", "feedRateCmd", "feedOverride",
        "machinePosition", "machinePositionX", "machinePositionY", "machinePositionZ",
        "absolutePositionX", "absolutePositionY", "absolutePositionZ",
        "toolNumber", "workMode");

    private static readonly HashSet<string> Focas = Set(
        "state", "alarm", "alarmNumber", "program", "programMain", "estop", "workMode", "isRunning",
        "spindleSpeed", "feedRate",
        "machinePosition", "machinePositionX", "machinePositionY", "machinePositionZ",
        "absolutePosition", "absolutePositionX", "absolutePositionY", "absolutePositionZ",
        "softwareVersion", "systemType", "axisCount");

    private static readonly HashSet<string> SdkIntent = Set(
        "state", "alarm", "program", "spindleSpeed", "feedRate", "machinePosition", "partCount", "toolNumber", "workMode");

    public static DriverSupportReport Describe(string? brandId)
    {
        var catalog = CncCatalog.Current;
        var brand = catalog.FindBrand(brandId) ?? catalog.Brands.FirstOrDefault();
        if (brand is null)
        {
            return new DriverSupportReport { BrandId = brandId ?? "", Drivers = [] };
        }

        var adapters = brand.Adapters.Concat(catalog.GenericAdapters).ToList();
        return new DriverSupportReport
        {
            BrandId = brand.Id,
            Drivers = adapters.Select(adapter => Describe(brand, adapter)).ToList()
        };
    }

    public static DriverSupportEntry Describe(CatalogBrand brand, CatalogAdapter adapter)
    {
        var items = new List<ItemSupportEntry>();
        foreach (var itemId in brand.ItemIds)
        {
            var (level, note) = Level(adapter, brand.Id, itemId);
            items.Add(new ItemSupportEntry { ItemId = itemId, Level = level, Note = note });
        }

        return new DriverSupportEntry
        {
            AdapterId = adapter.Id,
            Protocol = adapter.Protocol,
            DisplayName = adapter.DisplayName,
            RequiresSdk = RequiresSdk(adapter),
            Verification = Verification(adapter),
            Items = items
        };
    }

    public static (string Level, string Note) Level(CatalogAdapter adapter, string brandId, string itemId)
    {
        if (adapter.Kind == "simulator" || adapter.Protocol is "sim" or "fake")
        {
            return (Supported, "模拟器产生，不连接机床");
        }

        if (adapter.Protocol == "brother")
        {
            return (NotAvailable, "CNC-C00 没有可核对的公开帧格式");
        }

        if (adapter.Protocol == "focas")
        {
            return Focas.Contains(itemId)
                ? (ViaSdk, "FOCAS 已绑定。坐标按 0.001 mm 换算，需现场验收")
                : (NotAvailable, "当前 FOCAS 绑定没有这项");
        }

        if (adapter.Protocol == "opcua")
        {
            if (OpcUaMaps.UsesSiemensMap(adapter.Id, brandId) && SiemensOpc.Contains(itemId))
            {
                return (Supported, "SINUMERIK 公开变量路径");
            }

            if (!OpcUaMaps.UsesSiemensMap(adapter.Id, brandId) && (SiemensOpc.Contains(itemId) || MtConnect.Contains(itemId)))
            {
                return (Supported, "点位地址为 NodeId，或浏览名与数据项 Id 一致");
            }

            return (NotAvailable, "没有内置节点，也没有按浏览名尝试");
        }

        if (adapter.Protocol == "mtconnect" && MtConnect.Contains(itemId))
        {
            return (Supported, "MTConnect DataItem");
        }

        if (adapter.Protocol == "lsv2" && Lsv2.Contains(itemId))
        {
            return (Supported, "LSV2 R_RI / R_VR。主轴转速和进给速度没有对应报文");
        }

        if (adapter.Protocol == "haas-q" && HaasQ.Contains(itemId))
        {
            return (Supported, "Q100/Q104/Q500 等");
        }

        if (adapter.Protocol == "modbus" && Modbus.Contains(itemId))
        {
            return (Supported, "默认保持寄存器，点位地址可覆盖");
        }

        if (adapter.Protocol == "ftp")
        {
            return (Supported, "状态文件里有这个键时读取");
        }

        if (RequiresSdk(adapter))
        {
            return SdkIntent.Contains(itemId)
                ? (ViaSdk, "声明经由 SDK。公开函数签名未核对，找到文件也不会调用")
                : (NotAvailable, "SDK 驱动未声明这项");
        }

        return (NotAvailable, "");
    }

    public static bool RequiresSdk(CatalogAdapter adapter) =>
        adapter.Protocol is "focas" or "ezsocket" or "syntec" or "gsk" or "knd" or "hnc" or "baoyuan" or "kede" or "jdsoft" or "mitsubishi";

    private static string Verification(CatalogAdapter adapter) => adapter.Protocol switch
    {
        "sim" or "fake" => "模拟器，不连接机床",
        "opcua" => "协议级实现。西门子变量路径来自公开的 SINUMERIK OPC UA 说明，需在真实机床上验收。",
        "mtconnect" => "已用 MTConnect XML 夹具验证解析。需在真实 Agent 上验收。",
        "lsv2" => "帧格式对照公开的 LSV2 描述。需在真实 TNC 上验收。",
        "haas-q" => "Q 应答解析已用夹具验证。需在打开 Setting 143 的机床上验收。",
        "modbus" => "功能码 03/04 已用进程内服务器验证。默认寄存器不是台达出厂表。",
        "ftp" => "FTP RETR 与键值解析已用进程内服务器验证。",
        "focas" => "Windows 上对 Fwlib64 做 P/Invoke。缺库不崩溃。坐标换算需现场验收。",
        "brother" => "未实现专有握手。请改用 MTConnect 或 OPC UA。",
        _ when RequiresSdk(adapter) => "只检查 data/sdk 目录。没有可核对的公开函数签名，不会调用未知入口。",
        _ => "未实现"
    };

    private static HashSet<string> Set(params string[] items) =>
        new(items, StringComparer.OrdinalIgnoreCase);
}

public sealed class DriverSupportReport
{
    public string BrandId { get; init; } = "";

    public List<DriverSupportEntry> Drivers { get; init; } = [];
}

public sealed class DriverSupportEntry
{
    public string AdapterId { get; init; } = "";

    public string Protocol { get; init; } = "";

    public string DisplayName { get; init; } = "";

    public bool RequiresSdk { get; init; }

    public string Verification { get; init; } = "";

    public List<ItemSupportEntry> Items { get; init; } = [];
}

public sealed class ItemSupportEntry
{
    public string ItemId { get; init; } = "";

    public string Level { get; init; } = DriverCatalog.NotAvailable;

    public string Note { get; init; } = "";
}
