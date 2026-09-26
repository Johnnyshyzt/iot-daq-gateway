using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Cnc.Catalog;

/// <summary>
/// Canonical CNC data items and the per-brand mapping seeded from
/// <c>docs/catalog/cnc-data-template.xlsx</c>.
/// </summary>
public sealed class CncCatalog
{
    public const string EmbeddedResourceName = "cnc-catalog.json";

    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip
    };

    private static readonly Lazy<CncCatalog> Shared = new(LoadEmbedded);

    private readonly Dictionary<string, CatalogItem> _items;
    private readonly Dictionary<string, CatalogBrand> _brands;
    private readonly Dictionary<string, CatalogAdapter> _adapters;

    private CncCatalog(CatalogFile file)
    {
        Version = file.Version;
        Source = file.Source;
        Notes = file.Notes ?? [];
        Items = file.Items ?? [];
        Brands = file.Brands ?? [];
        GenericAdapters = file.GenericAdapters ?? [];
        _items = Items.ToDictionary(item => item.Id, StringComparer.OrdinalIgnoreCase);
        _brands = Brands.ToDictionary(brand => brand.Id, StringComparer.OrdinalIgnoreCase);
        _adapters = Brands
            .SelectMany(brand => brand.Adapters)
            .Concat(GenericAdapters)
            .ToDictionary(adapter => adapter.Id, StringComparer.OrdinalIgnoreCase);
    }

    public static CncCatalog Current => Shared.Value;

    public string Version { get; }

    public string Source { get; }

    public IReadOnlyList<string> Notes { get; }

    public IReadOnlyList<CatalogItem> Items { get; }

    public IReadOnlyList<CatalogBrand> Brands { get; }

    public IReadOnlyList<CatalogAdapter> GenericAdapters { get; }

    public IEnumerable<CatalogAdapter> AllAdapters => _adapters.Values;

    public CatalogItem? FindItem(string? id) =>
        id is not null && _items.TryGetValue(id, out var item) ? item : null;

    public CatalogBrand? FindBrand(string? id) =>
        id is not null && _brands.TryGetValue(id, out var brand) ? brand : null;

    public CatalogAdapter? FindAdapter(string? id) =>
        id is not null && _adapters.TryGetValue(id, out var adapter) ? adapter : null;

    public bool IsKnownAdapter(string? id) => FindAdapter(id) is not null;

    public CatalogItem? FindItemForBrand(string? brandId, string? itemId)
    {
        var brand = FindBrand(brandId);
        var item = FindItem(itemId);
        if (brand is null || item is null)
        {
            return null;
        }

        return brand.ItemIds.Contains(item.Id, StringComparer.OrdinalIgnoreCase) ? item : null;
    }

    /// <summary>
    /// Brand implied by an adapter id. <c>generic.ftp</c> has no brand; callers use the device brand.
    /// </summary>
    public string? BrandOfAdapter(string? adapterId)
    {
        var adapter = FindAdapter(adapterId);
        if (adapter is null || string.IsNullOrWhiteSpace(adapter.BrandId))
        {
            return null;
        }

        return adapter.BrandId;
    }

    public string? ResolveBrand(string? adapterId, string? explicitBrandId)
    {
        if (!string.IsNullOrWhiteSpace(explicitBrandId) && FindBrand(explicitBrandId) is not null)
        {
            return FindBrand(explicitBrandId)!.Id;
        }

        return BrandOfAdapter(adapterId);
    }

    public string AddressFor(string brandId, string itemId)
    {
        if (string.Equals(brandId, "fanuc", StringComparison.OrdinalIgnoreCase))
        {
            if (string.Equals(itemId, "state", StringComparison.OrdinalIgnoreCase))
            {
                return "cnc/statinfo";
            }

            if (string.Equals(itemId, "alarm", StringComparison.OrdinalIgnoreCase))
            {
                return "cnc/alarm";
            }

            if (string.Equals(itemId, "program", StringComparison.OrdinalIgnoreCase))
            {
                return "cnc/program";
            }
        }

        return "catalog/" + itemId;
    }

    public string StandardTemplateId(string brandId) =>
        string.Equals(brandId, "fanuc", StringComparison.OrdinalIgnoreCase)
            ? "fanuc-catalog"
            : brandId + "-standard";

    public static CncCatalog LoadEmbedded()
    {
        var assembly = typeof(CncCatalog).Assembly;
        var name = assembly.GetManifestResourceNames().FirstOrDefault(resource =>
            resource.EndsWith(EmbeddedResourceName, StringComparison.Ordinal));
        if (name is null)
        {
            throw new InvalidOperationException("Embedded CNC catalog resource is missing.");
        }

        using var stream = assembly.GetManifestResourceStream(name)
            ?? throw new InvalidOperationException("Embedded CNC catalog stream is missing.");
        var file = JsonSerializer.Deserialize<CatalogFile>(stream, Json)
            ?? throw new InvalidOperationException("CNC catalog JSON is empty.");
        return new CncCatalog(file);
    }
}

public sealed class CatalogFile
{
    public string Version { get; set; } = "";

    public string Source { get; set; } = "";

    public List<string> Notes { get; set; } = [];

    public List<CatalogItem> Items { get; set; } = [];

    public List<CatalogBrand> Brands { get; set; } = [];

    public List<CatalogAdapter> GenericAdapters { get; set; } = [];
}

public sealed class CatalogItem
{
    public string Id { get; set; } = "";

    public string NameZh { get; set; } = "";

    public string DataType { get; set; } = "string";

    public string Unit { get; set; } = "";

    public string Category { get; set; } = "";

    public bool BrandSpecific { get; set; }

    public string? BrandId { get; set; }
}

public sealed class CatalogBrand
{
    public string Id { get; set; } = "";

    public string NameZh { get; set; } = "";

    public string NameEn { get; set; } = "";

    public string SheetHeader { get; set; } = "";

    public List<CatalogMapping> Mappings { get; set; } = [];

    public List<string> ItemIds { get; set; } = [];

    public List<CatalogModel> Models { get; set; } = [];

    public List<CatalogAdapter> Adapters { get; set; } = [];
}

public sealed class CatalogMapping
{
    public string Source { get; set; } = "";

    public string ItemId { get; set; } = "";

    public bool BrandSpecific { get; set; }
}

public sealed class CatalogModel
{
    public string Id { get; set; } = "";

    public string Name { get; set; } = "";
}

public sealed class CatalogAdapter
{
    public string Id { get; set; } = "";

    public string? BrandId { get; set; }

    public string Kind { get; set; } = "";

    public string Protocol { get; set; } = "";

    public int Phase { get; set; }

    public string DisplayName { get; set; } = "";

    public string? Note { get; set; }

    public List<CatalogParameter> Parameters { get; set; } = [];
}

public sealed class CatalogParameter
{
    public string Name { get; set; } = "";

    public string Type { get; set; } = "string";

    public string Label { get; set; } = "";

    public bool Required { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    public object? Default { get; set; }
}
