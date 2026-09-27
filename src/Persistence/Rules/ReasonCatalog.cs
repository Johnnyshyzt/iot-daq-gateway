namespace IotDaq.Persistence.Rules;

public static class ReasonCatalog
{
    public static IReadOnlyList<ReasonSeed> Defaults { get; } =
    [
        Node("planned", null, "PLANNED", "计划停机", 10),
        Node("planned.break", "planned", "BREAK", "休息", 11),
        Node("planned.maintain", "planned", "MAINTAIN", "保养", 12),
        Node("planned.changeover", "planned", "CHANGEOVER", "换型", 13),
        Node("planned.service", "planned", "SERVICE", "计划检修", 14),
        Node("fault", null, "FAULT", "故障", 20),
        Node("fault.mechanical", "fault", "MECH", "机械故障", 21),
        Node("fault.electrical", "fault", "ELEC", "电气故障", 22),
        Node("fault.tool", "fault", "TOOL", "刀具异常", 23),
        Node("fault.program", "fault", "PROG", "程序错误", 24),
        Node("material", null, "MATERIAL", "待料", 30),
        Node("material.blank", "material", "BLANK", "待毛坯", 31),
        Node("material.tool", "material", "WAIT_TOOL", "待刀具", 32),
        Node("material.qc", "material", "QC", "待质检", 33),
        Node("people", null, "PEOPLE", "人员", 40),
        Node("people.operator", "people", "OPERATOR", "待操作工", 41),
        Node("people.training", "people", "TRAINING", "培训", 42),
        Node("other", null, "OTHER", "其他", 50),
        Node("other.unknown", "other", "UNKNOWN", "未说明", 51)
    ];

    public static ReasonSeed? Find(string? codeOrId)
    {
        if (string.IsNullOrWhiteSpace(codeOrId))
        {
            return null;
        }

        return Defaults.FirstOrDefault(item =>
            string.Equals(item.Id, codeOrId, StringComparison.OrdinalIgnoreCase)
            || string.Equals(item.Code, codeOrId, StringComparison.OrdinalIgnoreCase));
    }

    private static ReasonSeed Node(string id, string? parent, string code, string name, int sort) => new()
    {
        Id = id,
        ParentId = parent,
        Code = code,
        Name = name,
        Sort = sort
    };
}

public sealed class ReasonSeed
{
    public string Id { get; init; } = "";

    public string? ParentId { get; init; }

    public string Code { get; init; } = "";

    public string Name { get; init; } = "";

    public int Sort { get; init; }
}
