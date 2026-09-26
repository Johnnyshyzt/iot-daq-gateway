using Cnc.Catalog;
using Studio.Contracts;

namespace Studio.Host.Config;

public static class PointCatalogNormalizer
{
    public static bool TryNormalize(string? brandId, PointDefinition point)
    {
        if (string.Equals(brandId, FanucPointCatalog.Family, StringComparison.OrdinalIgnoreCase)
            && FanucPointCatalog.TryNormalize(point))
        {
            return true;
        }

        var item = CncCatalog.Current.FindItemForBrand(brandId, point.Id);
        if (item is null || string.IsNullOrWhiteSpace(brandId))
        {
            return false;
        }

        point.Id = item.Id;
        point.Address = CncCatalog.Current.AddressFor(brandId, item.Id);
        point.DataType = item.DataType;
        if (string.IsNullOrWhiteSpace(point.Unit))
        {
            point.Unit = item.Unit;
        }

        return true;
    }
}
