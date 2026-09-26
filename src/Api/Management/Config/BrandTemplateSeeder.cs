using Cnc.Catalog;
using Studio.Contracts;

namespace Studio.Host.Config;

public static class BrandTemplateSeeder
{
    public static bool Ensure(ConfigBundle bundle)
    {
        bundle.PointTemplates ??= [];
        var catalog = CncCatalog.Current;
        var changed = false;
        foreach (var brand in catalog.Brands)
        {
            var id = catalog.StandardTemplateId(brand.Id);
            if (bundle.PointTemplates.Any(template =>
                    string.Equals(template.Metadata.Id, id, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            bundle.PointTemplates.Add(Create(catalog, brand, id));
            changed = true;
        }

        return changed;
    }

    public static PointTemplateDocument Create(CncCatalog catalog, CatalogBrand brand, string id)
    {
        var points = new List<PointDefinition>();
        foreach (var itemId in brand.ItemIds)
        {
            var item = catalog.FindItem(itemId);
            if (item is null)
            {
                continue;
            }

            points.Add(new PointDefinition
            {
                Id = item.Id,
                Address = catalog.AddressFor(brand.Id, item.Id),
                DataType = item.DataType,
                Unit = item.Unit,
                Scale = 1,
                Deadband = 0,
                Enabled = true
            });
        }

        return new PointTemplateDocument
        {
            Metadata = new PointTemplateMetadata
            {
                Id = id,
                DisplayName = brand.NameZh + " 标准目录"
            },
            Spec = new PointTemplateSpec
            {
                Adapter = brand.Id,
                Points = points
            }
        };
    }
}
