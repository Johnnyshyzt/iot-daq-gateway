using IotDaq.Persistence.Visualization;

namespace IotDaq.Persistence.Rules;

public sealed class StateMapRule
{
    public string Scope { get; init; } = "";

    public string OwnerId { get; init; } = "";

    public string RawValue { get; init; } = "";

    public string State { get; init; } = "";
}

public static class StateClassifier
{
    public static string Classify(
        string? raw,
        string deviceId,
        string? brandId,
        string? templateId,
        IReadOnlyList<StateMapRule> maps)
    {
        var text = (raw ?? "").Trim();
        var mapped = Find(maps, "device", deviceId, text)
            ?? Find(maps, "template", templateId, text)
            ?? Find(maps, "brand", brandId, text);
        if (mapped is not null)
        {
            return MachineState.Canonical(mapped);
        }

        return MachineState.Normalize(text);
    }

    private static string? Find(IReadOnlyList<StateMapRule> maps, string scope, string? owner, string raw)
    {
        if (string.IsNullOrWhiteSpace(owner) || raw.Length == 0)
        {
            return null;
        }

        foreach (var map in maps)
        {
            if (string.Equals(map.Scope, scope, StringComparison.OrdinalIgnoreCase)
                && string.Equals(map.OwnerId, owner, StringComparison.OrdinalIgnoreCase)
                && string.Equals(map.RawValue, raw, StringComparison.OrdinalIgnoreCase))
            {
                return map.State;
            }
        }

        return null;
    }
}
