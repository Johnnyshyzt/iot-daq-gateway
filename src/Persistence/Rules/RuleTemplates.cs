namespace IotDaq.Persistence.Rules;

public static class RuleTemplates
{
    public static IReadOnlyList<RuleTemplate> All { get; } =
    [
        new()
        {
            Key = "spindle-load",
            Name = "主轴负载超过 90% 持续 5 分钟",
            Expression = "spindleLoad > 90",
            DurationMs = 5 * 60 * 1000,
            DebounceMs = 60 * 1000,
            Description = "主轴负载连续高于 90% 五分钟后报警。",
            ActionsJson = """[{"type":"alarm","severity":"warning","code":"SPINDLE_LOAD","message":"主轴负载超过 90% 已持续 5 分钟"}]"""
        },
        new()
        {
            Key = "cycle-counter",
            Name = "由产量计数得到节拍",
            Expression = "counterInc(partCount) > 0",
            DurationMs = 0,
            DebounceMs = 0,
            Description = "产量计数增加时记下一次节拍。计算点「距上次计数的秒数」用 since(partCount)。",
            ActionsJson = """[{"type":"write","pointId":"calc.partStep","value":1},{"type":"event","name":"part.step","message":"产量计数增加"}]"""
        },
        new()
        {
            Key = "idle-long",
            Name = "空闲超过 N 分钟",
            Expression = "state == \"IDLE\" or state == \"待机\" or state == \"空闲\"",
            DurationMs = 10 * 60 * 1000,
            DebounceMs = 60 * 1000,
            Description = "状态保持空闲达到持续时间后报警。把持续时间改成现场的 N 分钟。",
            ActionsJson = """[{"type":"alarm","severity":"info","code":"IDLE_LONG","message":"设备空闲已超过设定时间"},{"type":"reason","reasonCode":"OPERATOR"}]"""
        }
    ];
}

public sealed class RuleTemplate
{
    public string Key { get; init; } = "";

    public string Name { get; init; } = "";

    public string Expression { get; init; } = "";

    public long DurationMs { get; init; }

    public long DebounceMs { get; init; }

    public string Description { get; init; } = "";

    public string ActionsJson { get; init; } = "[]";
}
