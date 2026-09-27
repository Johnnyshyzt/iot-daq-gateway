using Adapters.Cnc.Drivers;
using Cnc.Catalog;

namespace Studio.Host.Shop;

public sealed class TransferChannelView
{
    public string Channel { get; init; } = "";

    public string AdapterId { get; init; } = "";

    public bool CanTransfer { get; init; }

    public string Note { get; init; } = "";
}

public sealed class SignalView
{
    public string ItemId { get; init; } = "";

    public string AdapterId { get; init; } = "";

    public string Level { get; init; } = "";

    public string Note { get; init; } = "";
}

public sealed class BrandTransferView
{
    public string BrandId { get; init; } = "";

    public string NameZh { get; init; } = "";

    public List<TransferChannelView> Channels { get; init; } = [];

    public List<SignalView> ToolSignals { get; init; } = [];

    public string Counting { get; init; } = "";
}

/// <summary>
/// Per-brand transfer notes stay inside what the drivers in this repo actually do.
/// LSV2 file commands are not implemented. FTP STOR and a DNC folder drop are generic paths.
/// </summary>
public static class TransferCatalog
{
    private static readonly string[] SignalItems = ["toolNumber", "partCount", "partCountTotal", "state", "isRunning", "cycleTime"];

    public static IReadOnlyList<BrandTransferView> DescribeAll()
    {
        return CncCatalog.Current.Brands.Select(Describe).ToList();
    }

    public static BrandTransferView Describe(string? brandId)
    {
        var brand = CncCatalog.Current.FindBrand(brandId);
        return brand is null
            ? new BrandTransferView { BrandId = brandId ?? "", Counting = "未知品牌。可用手动计数或计算点 toolNumber / partCount。" }
            : Describe(brand);
    }

    public static bool IsGenericChannel(string? channel) =>
        channel is "dnc-folder" or "smb-folder" or "ftp";

    private static BrandTransferView Describe(CatalogBrand brand)
    {
        var channels = new List<TransferChannelView>();
        var signals = new List<SignalView>();
        foreach (var adapter in brand.Adapters.Concat(CncCatalog.Current.GenericAdapters))
        {
            channels.Add(Channel(adapter));
            foreach (var item in SignalItems)
            {
                if (!brand.ItemIds.Contains(item, StringComparer.OrdinalIgnoreCase) && adapter.Kind != "simulator")
                {
                    continue;
                }

                var (level, note) = DriverCatalog.Level(adapter, brand.Id, item);
                if (level == DriverCatalog.NotAvailable && adapter.Kind == "simulator")
                {
                    continue;
                }

                signals.Add(new SignalView
                {
                    ItemId = item,
                    AdapterId = adapter.Id,
                    Level = level,
                    Note = note
                });
            }
        }

        channels.Add(new TransferChannelView
        {
            Channel = "dnc-folder",
            AdapterId = "generic",
            CanTransfer = true,
            Note = "把已批准程序写到设备参数 dncFolder，或请求里给出的目录。机床侧 DNC 读取该目录。不调用厂商协议。"
        });
        channels.Add(new TransferChannelView
        {
            Channel = "smb-folder",
            AdapterId = "generic",
            CanTransfer = true,
            Note = "若目录是已经挂载的共享路径，按普通文件夹写入。网关不实现 SMB 协议。"
        });
        var liveTool = signals.Any(item => item.ItemId == "toolNumber" && item.Level is DriverCatalog.Supported or DriverCatalog.ViaSdk);
        var counting = liveTool
            ? "驱动或模拟器能提供刀号时，按件数增量和切削时间累计。没有刀号的品牌用手动计数，或用计算点写出 toolNumber / partCount。"
            : "这个品牌的现有驱动没有刀号。请用手动计数，或用计算点 / 规则写出 toolNumber 和 partCount。";
        return new BrandTransferView
        {
            BrandId = brand.Id,
            NameZh = brand.NameZh,
            Channels = channels,
            ToolSignals = signals,
            Counting = counting
        };
    }

    private static TransferChannelView Channel(CatalogAdapter adapter)
    {
        if (adapter.Kind == "simulator" || adapter.Protocol is "sim" or "fake")
        {
            return new TransferChannelView
            {
                Channel = "simulator",
                AdapterId = adapter.Id,
                CanTransfer = false,
                Note = "模拟器不连接机床，不传程序。"
            };
        }

        if (adapter.Protocol == "ftp")
        {
            return new TransferChannelView
            {
                Channel = "ftp",
                AdapterId = adapter.Id,
                CanTransfer = true,
                Note = "标准 FTP STOR / RETR，写到连接里的路径。不是厂商私有协议。"
            };
        }

        if (adapter.Protocol == "lsv2")
        {
            return new TransferChannelView
            {
                Channel = "lsv2",
                AdapterId = adapter.Id,
                CanTransfer = false,
                Note = "现有 LSV2 驱动只读 R_RI / R_VR（含当前程序名和刀号）。没有文件传输命令，本版本不向 TNC 写程序。"
            };
        }

        if (adapter.Protocol == "focas")
        {
            return new TransferChannelView
            {
                Channel = "focas",
                AdapterId = adapter.Id,
                CanTransfer = false,
                Note = "FOCAS 绑定只读状态和坐标，不传程序，也不调用未公开的文件接口。"
            };
        }

        if (DriverCatalog.RequiresSdk(adapter))
        {
            return new TransferChannelView
            {
                Channel = adapter.Protocol,
                AdapterId = adapter.Id,
                CanTransfer = false,
                Note = "厂商 SDK 未核对文件传输入口。不会发明调用。可用 DNC 目录或 FTP。"
            };
        }

        return new TransferChannelView
        {
            Channel = adapter.Protocol,
            AdapterId = adapter.Id,
            CanTransfer = false,
            Note = "这个协议驱动只采集状态，不传程序文件。可用 DNC 目录或 FTP。"
        };
    }
}
