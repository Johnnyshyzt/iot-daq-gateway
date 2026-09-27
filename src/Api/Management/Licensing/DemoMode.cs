using Cnc.Catalog;
using Studio.Contracts;
using Studio.Host.Config;

namespace Studio.Host.Licensing;

/// <summary>
/// Adds clearly labeled simulator devices. It does not invent vendor SDK calls.
/// </summary>
public static class DemoMode
{
    public const string SettingKey = "demo.labeled";

    public static readonly DemoDevice[] Devices =
    [
        new("demo-fanuc", "fanuc.sim", "演示数据 · 发那科", "车削"),
        new("demo-siemens", "siemens.sim", "演示数据 · 西门子", "铣削"),
        new("demo-mitsubishi", "mitsubishi.sim", "演示数据 · 三菱", "加工中心"),
        new("demo-syntec", "syntec.sim", "演示数据 · 新代", "雕铣"),
        new("demo-gsk", "gsk.sim", "演示数据 · 广数", "车削"),
        new("demo-haas", "haas.sim", "演示数据 · 哈斯", "加工中心")
    ];

    public static DemoSeedResult Seed(ConfigStore store, bool publish)
    {
        var added = new List<string>();
        foreach (var device in Devices)
        {
            var exists = store.ListDevices().Any(item =>
                string.Equals(item.Metadata.Id, device.Id, StringComparison.Ordinal));
            if (exists)
            {
                continue;
            }

            var brandId = CncCatalog.Current.BrandOfAdapter(device.Adapter) ?? device.Adapter.Split('.')[0];
            var templateId = EnsureDemoTemplate(store, brandId);
            store.UpsertDevice(device.Id, new DeviceDocument
            {
                Metadata = new DeviceMetadata
                {
                    Id = device.Id,
                    DisplayName = device.DisplayName
                },
                Spec = new DeviceSpec
                {
                    Adapter = device.Adapter,
                    Enabled = true,
                    IntervalMs = 1000,
                    Workshop = "演示车间",
                    Line = device.Line,
                    PointTemplateId = templateId,
                    Connection = new DeviceConnection
                    {
                        Host = "127.0.0.1",
                        Port = 8193,
                        FocasTimeoutMs = 3000
                    }
                }
            });
            added.Add(device.Id);
        }

        var published = false;
        if (publish)
        {
            var outcome = store.Publish("载入演示数据");
            published = outcome.Published;
            if (!outcome.Published)
            {
                var issue = outcome.Issues.FirstOrDefault(item => item.Severity == "error")?.Message ?? "发布未通过校验";
                return new DemoSeedResult
                {
                    Added = added,
                    Published = false,
                    Message = "演示设备已写入草稿，但没有发布：" + issue
                };
            }
        }

        store.Database.SetSetting(SettingKey, "1");
        return new DemoSeedResult
        {
            Added = added,
            Published = published,
            Message = added.Count == 0
                ? "演示设备已经在草稿里，名称都带「演示数据」。"
                : "已加入 " + added.Count.ToString(System.Globalization.CultureInfo.InvariantCulture) + " 台模拟设备，名称带「演示数据」。这不是真实机床。"
        };
    }

    private static string EnsureDemoTemplate(ConfigStore store, string brandId)
    {
        var id = brandId + "-demo";
        if (store.ListPointTemplates().Any(item => string.Equals(item.Metadata.Id, id, StringComparison.OrdinalIgnoreCase)))
        {
            return id;
        }

        var catalog = CncCatalog.Current;
        var brand = catalog.FindBrand(brandId);
        var points = new List<PointDefinition>();
        foreach (var pointId in new[] { "state", "alarm", "program", "spindleLoad", "partCount", "scrapCount" })
        {
            if (brand is not null && !brand.ItemIds.Contains(pointId, StringComparer.OrdinalIgnoreCase))
            {
                continue;
            }

            var item = catalog.FindItem(pointId);
            var numeric = !string.Equals(item?.DataType, "string", StringComparison.OrdinalIgnoreCase)
                && pointId is not ("state" or "alarm" or "program");
            points.Add(new PointDefinition
            {
                Id = pointId,
                Address = "catalog/" + pointId,
                DataType = numeric ? "number" : "string",
                Unit = item?.Unit ?? "",
                Scale = 1,
                Deadband = 0,
                Enabled = true
            });
        }

        store.UpsertPointTemplate(id, new PointTemplateDocument
        {
            Metadata = new PointTemplateMetadata
            {
                Id = id,
                DisplayName = "演示 · " + brandId
            },
            Spec = new PointTemplateSpec
            {
                Adapter = brandId,
                Points = points
            }
        });
        return id;
    }
}

public sealed record DemoDevice(string Id, string Adapter, string DisplayName, string Line);

public sealed class DemoSeedResult
{
    public List<string> Added { get; set; } = [];

    public bool Published { get; set; }

    public string Message { get; set; } = "";
}
