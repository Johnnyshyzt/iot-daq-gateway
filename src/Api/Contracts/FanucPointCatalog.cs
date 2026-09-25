namespace Studio.Contracts;

/// <summary>
/// Fanuc adapter point catalog shared by validation, draft save, and Studio.
/// Addresses are internal conventions for YAML compatibility. Collection uses point id.
/// </summary>
public static class FanucPointCatalog
{
    public const string FakeAdapter = "fanuc.fake";

    public const string FocasAdapter = "fanuc.focas";

    public const string Notice =
        "这些是发那科适配器约定的点位，不是通用 PLC 地址。其他品牌以后会有各自的目录。";

    private static readonly PointCatalogEntry[] Entries =
    [
        Entry("state", "string", "机床状态（IDLE / RUNNING / ALARM）", "cnc/statinfo"),
        Entry("alarm", "string", "报警号；正常为 0", "cnc/alarm"),
        Entry("program", "string", "当前程序，如 O0001", "cnc/program")
    ];

    public static bool IsFanuc(string? adapter) =>
        string.Equals(adapter, FakeAdapter, StringComparison.Ordinal)
        || string.Equals(adapter, FocasAdapter, StringComparison.Ordinal);

    public static string IdList => string.Join("、", Entries.Select(entry => entry.Id));

    public static PointCatalogDocument Describe(string adapter) => new()
    {
        Adapter = adapter,
        Scope = "fanuc",
        Message = Notice,
        Points = Entries.Select(Clone).ToList()
    };

    public static PointCatalogEntry? Find(string? id) =>
        Entries.FirstOrDefault(entry => string.Equals(entry.Id, id, StringComparison.OrdinalIgnoreCase));

    public static bool TryNormalize(PointDefinition point)
    {
        var entry = Find(point.Id);
        if (entry is null)
        {
            return false;
        }

        point.Id = entry.Id;
        point.Address = entry.Address;
        point.DataType = entry.DataType;
        return true;
    }

    public static List<PointDefinition> DefaultPoints() =>
        Entries.Select(entry => new PointDefinition
        {
            Id = entry.Id,
            Address = entry.Address,
            DataType = entry.DataType,
            Unit = "",
            Scale = entry.Scale,
            Deadband = entry.Deadband,
            Enabled = true
        }).ToList();

    private static PointCatalogEntry Entry(string id, string dataType, string description, string address) => new()
    {
        Id = id,
        DataType = dataType,
        Description = description,
        Scale = 1,
        Deadband = 0,
        Address = address
    };

    private static PointCatalogEntry Clone(PointCatalogEntry entry) => new()
    {
        Id = entry.Id,
        DataType = entry.DataType,
        Description = entry.Description,
        Scale = entry.Scale,
        Deadband = entry.Deadband,
        Address = entry.Address
    };
}

public sealed class PointCatalogDocument
{
    public string Adapter { get; set; } = "";

    public string Scope { get; set; } = "";

    public string Message { get; set; } = "";

    public List<PointCatalogEntry> Points { get; set; } = [];
}

public sealed class PointCatalogEntry
{
    public string Id { get; set; } = "";

    public string DataType { get; set; } = "string";

    public string Description { get; set; } = "";

    public double Scale { get; set; } = 1;

    public double Deadband { get; set; }

    /// <summary>
    /// Internal YAML address. Not a user-editable protocol address.
    /// </summary>
    public string Address { get; set; } = "";
}
