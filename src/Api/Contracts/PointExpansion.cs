namespace Studio.Contracts;

/// <summary>
/// Expands a device class template plus an optional per-device override into the points
/// the collector publishes for that device id.
///
/// Merge rules:
/// - No PointSet: effective points are the template points.
/// - A PointSet replaces template points with the same id (enabled, unit, scale, deadband).
///   Address and data type stay on the Fanuc catalog entry when the id is known.
/// - A catalog id that is not on the template is appended. This is how one machine can
///   enable an extra catalog point without a new template.
/// - An id outside the Fanuc catalog is kept so validation can reject it. Overrides cannot
///   invent non-catalog ids, and expansion does not fabricate an address for them.
/// - Omitting a template point from the override leaves that template point unchanged.
///   To turn a point off for one machine, the override must set <c>enabled: false</c>.
/// </summary>
public static class PointExpansion
{
    public static List<PointDefinition> EffectivePoints(PointTemplateDocument? template, PointSetDocument? overrides)
    {
        var result = new List<PointDefinition>();
        if (template is not null)
        {
            foreach (var point in template.Spec?.Points ?? [])
            {
                result.Add(NormalizeCopy(point));
            }
        }

        foreach (var point in overrides?.Spec?.Points ?? [])
        {
            var id = (point.Id ?? "").Trim();
            var index = result.FindIndex(item => string.Equals(item.Id, id, StringComparison.OrdinalIgnoreCase));
            if (index >= 0)
            {
                var merged = Clone(result[index]);
                merged.Unit = point.Unit ?? "";
                merged.Scale = point.Scale;
                merged.Deadband = point.Deadband;
                merged.Enabled = point.Enabled;
                if (!string.IsNullOrWhiteSpace(point.DataType))
                {
                    merged.DataType = point.DataType;
                }

                if (!string.IsNullOrWhiteSpace(point.Address))
                {
                    merged.Address = point.Address;
                }

                result[index] = NormalizeCopy(merged);
            }
            else if (id.Length > 0)
            {
                result.Add(NormalizeCopy(point));
            }
        }

        return result;
    }

    public static bool SameEffective(PointTemplateDocument template, PointSetDocument? overrides)
    {
        var baseline = EffectivePoints(template, null);
        var effective = EffectivePoints(template, overrides);
        return SameList(baseline, effective);
    }

    /// <summary>
    /// Turns a legacy full point table (the old per-device PointSet, written before templates)
    /// into a minimal override against <paramref name="template"/>.
    /// Points the old table omitted are disabled. Points that match the template are dropped.
    /// Ids the template does not contain are kept so validation can still reject them.
    /// </summary>
    public static List<PointDefinition> LegacyOverride(PointTemplateDocument template, PointSetDocument legacy)
    {
        var result = new List<PointDefinition>();
        var oldPoints = legacy.Spec?.Points ?? [];
        var templatePoints = template.Spec?.Points ?? [];
        foreach (var templatePoint in templatePoints)
        {
            var normalizedTemplate = NormalizeCopy(templatePoint);
            var old = oldPoints.FirstOrDefault(point =>
                string.Equals((point.Id ?? "").Trim(), normalizedTemplate.Id, StringComparison.OrdinalIgnoreCase));
            if (old is null)
            {
                var disabled = Clone(normalizedTemplate);
                disabled.Enabled = false;
                result.Add(disabled);
                continue;
            }

            var merged = Clone(normalizedTemplate);
            merged.Unit = old.Unit ?? "";
            merged.Scale = old.Scale;
            merged.Deadband = old.Deadband;
            merged.Enabled = old.Enabled;
            merged = NormalizeCopy(merged);
            if (!TuningEquals(normalizedTemplate, merged))
            {
                result.Add(merged);
            }
        }

        foreach (var old in oldPoints)
        {
            var id = (old.Id ?? "").Trim();
            var onTemplate = templatePoints.Any(point =>
                string.Equals((point.Id ?? "").Trim(), id, StringComparison.OrdinalIgnoreCase));
            if (!onTemplate && id.Length > 0)
            {
                result.Add(NormalizeCopy(old));
            }
        }

        return result;
    }

    public static PointDefinition NormalizeCopy(PointDefinition point)
    {
        var copy = Clone(point);
        copy.Id = (copy.Id ?? "").Trim();
        copy.Address = (copy.Address ?? "").Trim();
        copy.DataType = (copy.DataType ?? "").Trim().ToLowerInvariant();
        copy.Unit ??= "";
        FanucPointCatalog.TryNormalize(copy);
        return copy;
    }

    private static bool SameList(List<PointDefinition> left, List<PointDefinition> right)
    {
        if (left.Count != right.Count)
        {
            return false;
        }

        for (var i = 0; i < left.Count; i++)
        {
            if (!string.Equals(left[i].Id, right[i].Id, StringComparison.Ordinal) || !TuningEquals(left[i], right[i]))
            {
                return false;
            }
        }

        return true;
    }

    private static bool TuningEquals(PointDefinition left, PointDefinition right) =>
        left.Enabled == right.Enabled
        && string.Equals(left.Unit ?? "", right.Unit ?? "", StringComparison.Ordinal)
        && left.Scale == right.Scale
        && left.Deadband == right.Deadband;

    private static PointDefinition Clone(PointDefinition point) => new()
    {
        Id = point.Id ?? "",
        Address = point.Address ?? "",
        DataType = point.DataType ?? "",
        Unit = point.Unit ?? "",
        Scale = point.Scale,
        Deadband = point.Deadband,
        Enabled = point.Enabled
    };
}
