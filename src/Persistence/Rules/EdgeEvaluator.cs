using System.Globalization;
using Gateway.Abstractions.Models;

namespace IotDaq.Persistence.Rules;

public sealed class ComputedSpec
{
    public string PointId { get; init; } = "";

    public string Name { get; init; } = "";

    public string Unit { get; init; } = "";

    public required ExpressionProgram Program { get; init; }
}

public sealed class ComputeBatch
{
    public List<Observation> Observations { get; init; } = [];

    public Dictionary<string, double?> Numbers { get; init; } = new(StringComparer.OrdinalIgnoreCase);

    public Dictionary<string, string?> Texts { get; init; } = new(StringComparer.OrdinalIgnoreCase);
}

public static class EdgeEvaluator
{
    public static ComputeBatch Compute(
        string deviceId,
        IReadOnlyList<ComputedSpec> specs,
        IReadOnlyList<Observation> batch,
        IReadOnlyDictionary<string, double?> seedNumbers,
        IReadOnlyDictionary<string, string?> seedTexts,
        PointMemory memory,
        long nowMs)
    {
        var numbers = new Dictionary<string, double?>(seedNumbers, StringComparer.OrdinalIgnoreCase);
        var texts = new Dictionary<string, string?>(seedTexts, StringComparer.OrdinalIgnoreCase);
        foreach (var observation in batch.OrderBy(item => item.Timestamp))
        {
            texts[observation.Point] = ObservationText(observation.Value);
            if (ToNumber(observation.Value) is double number)
            {
                numbers[observation.Point] = number;
                memory.Observe(observation.Point, observation.Timestamp.ToUnixTimeMilliseconds(), number);
            }
        }

        var created = new List<Observation>();
        var batchResult = new ComputeBatch { Numbers = numbers, Texts = texts, Observations = created };
        foreach (var spec in Order(specs))
        {
            var value = ExpressionEngine.Evaluate(spec.Program, new EvalContext
            {
                Memory = memory,
                Numbers = numbers,
                Texts = texts,
                NowMs = nowMs,
                DurationScope = deviceId + ":" + spec.PointId
            });
            if (!value.Ok || value.Number is null && value.Bool is null && value.Text is null)
            {
                continue;
            }

            var number = value.Number;
            if (value.Bool.HasValue && number is null)
            {
                number = value.Bool.Value ? 1 : 0;
            }

            var text = value.Text ?? number?.ToString("0.####", CultureInfo.InvariantCulture) ?? (value.Bool == true ? "true" : "false");
            if (number is double observed)
            {
                numbers[spec.PointId] = observed;
                memory.Observe(spec.PointId, nowMs, observed);
            }

            texts[spec.PointId] = text;
            created.Add(new Observation
            {
                DeviceId = deviceId,
                Point = spec.PointId,
                Value = number is double numeric ? numeric : text,
                Timestamp = DateTimeOffset.FromUnixTimeMilliseconds(nowMs),
                Quality = "good",
                Unit = spec.Unit,
                Computed = true
            });
        }

        return batchResult;
    }

    public static List<ComputedSpec> Order(IReadOnlyList<ComputedSpec> specs)
    {
        var byId = specs.ToDictionary(spec => spec.PointId, StringComparer.OrdinalIgnoreCase);
        var pending = new HashSet<string>(byId.Keys, StringComparer.OrdinalIgnoreCase);
        var ordered = new List<ComputedSpec>();
        var guard = 0;
        while (pending.Count > 0 && guard++ < specs.Count + 2)
        {
            var ready = pending.Where(id => byId[id].Program.References.All(reference =>
                !pending.Contains(reference) || string.Equals(reference, id, StringComparison.OrdinalIgnoreCase))).ToList();
            if (ready.Count == 0)
            {
                break;
            }

            foreach (var id in ready)
            {
                pending.Remove(id);
                ordered.Add(byId[id]);
            }
        }

        return ordered;
    }

    public static string? ObservationText(object? value) => value switch
    {
        null => null,
        string text => text,
        bool flag => flag ? "true" : "false",
        IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
        _ => value.ToString()
    };

    public static double? ToNumber(object? value) => value switch
    {
        byte number => number,
        short number => number,
        int number => number,
        long number => number,
        float number => number,
        double number => number,
        decimal number => (double)number,
        string text when double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed) => parsed,
        _ => null
    };
}
