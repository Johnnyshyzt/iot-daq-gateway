using Studio.Contracts;

namespace Studio.Host.Config;

/// <summary>
/// Older bundles stored a full PointSet per device and no template.
/// On load, attach the built-in Fanuc template, point each Fanuc device at it,
/// and keep <c>points/{deviceId}.yaml</c> only where that machine differs.
/// A PointSet that matches the template is removed so it does not shadow the class.
/// </summary>
public static class PointTemplateMigration
{
    public static bool Apply(ConfigBundle bundle)
    {
        var changed = false;
        bundle.PointTemplates ??= [];
        bundle.PointSets ??= [];
        bundle.Devices ??= [];

        if (bundle.PointTemplates.All(template => !IdEquals(template.Metadata?.Id, ConfigDefaults.DefaultFanucTemplateId)))
        {
            bundle.PointTemplates.Add(ConfigDefaults.FanucTemplate());
            changed = true;
        }

        foreach (var device in bundle.Devices)
        {
            device.Spec ??= new DeviceSpec();
            if (!FanucPointCatalog.IsFanuc(device.Spec.Adapter))
            {
                continue;
            }

            var hadTemplate = !string.IsNullOrWhiteSpace(device.Spec.PointTemplateId);
            if (!hadTemplate)
            {
                device.Spec.PointTemplateId = ConfigDefaults.DefaultFanucTemplateId;
                changed = true;
            }

            var templateId = device.Spec.PointTemplateId ?? "";
            var template = bundle.PointTemplates.FirstOrDefault(item => IdEquals(item.Metadata?.Id, templateId));
            var set = bundle.PointSets.FirstOrDefault(item => IdEquals(item.Metadata?.DeviceId, device.Metadata?.Id));
            if (template is null || set is null)
            {
                continue;
            }

            if (!hadTemplate)
            {
                var minimal = PointExpansion.LegacyOverride(template, set);
                if (minimal.Count == 0)
                {
                    bundle.PointSets.Remove(set);
                    changed = true;
                }
                else if (!SamePoints(set.Spec?.Points, minimal))
                {
                    set.Spec ??= new PointSetSpec();
                    set.Spec.Points = minimal;
                    changed = true;
                }
            }
            else if (PointExpansion.SameEffective(template, set))
            {
                bundle.PointSets.Remove(set);
                changed = true;
            }
        }

        return changed;
    }

    private static bool SamePoints(List<PointDefinition>? left, List<PointDefinition> right)
    {
        left ??= [];
        if (left.Count != right.Count)
        {
            return false;
        }

        for (var i = 0; i < left.Count; i++)
        {
            var a = PointExpansion.NormalizeCopy(left[i]);
            var b = PointExpansion.NormalizeCopy(right[i]);
            if (!string.Equals(a.Id, b.Id, StringComparison.Ordinal)
                || a.Enabled != b.Enabled
                || !string.Equals(a.Unit ?? "", b.Unit ?? "", StringComparison.Ordinal)
                || a.Scale != b.Scale
                || a.Deadband != b.Deadband)
            {
                return false;
            }
        }

        return true;
    }

    private static bool IdEquals(string? left, string? right) =>
        string.Equals(left ?? "", right ?? "", StringComparison.Ordinal);
}
