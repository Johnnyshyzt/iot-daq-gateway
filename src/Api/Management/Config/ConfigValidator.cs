using System.Text.RegularExpressions;
using Studio.Contracts;

namespace Studio.Host.Config;

public static partial class ConfigValidator
{
    private static readonly string[] LogLevels = ["Trace", "Debug", "Information", "Warning", "Error", "Critical"];
    private static readonly string[] Adapters = ["fanuc.fake", "fanuc.focas"];
    private static readonly string[] DataTypes = ["string", "number", "bool", "int", "int32", "int64", "float", "double"];

    public static ValidationResult Validate(ConfigBundle bundle)
    {
        var issues = new List<ValidationIssue>();
        bundle.PointTemplates ??= [];
        bundle.PointSets ??= [];
        bundle.Devices ??= [];
        var gateway = bundle.Gateway;
        if (!string.Equals(gateway.ApiVersion, StudioApi.Version, StringComparison.Ordinal) || gateway.Kind != "Gateway")
        {
            Error(issues, "gateway", "gateway.yaml 的 apiVersion 或 kind 不正确");
        }

        if (!SafeId().IsMatch(gateway.Metadata.SiteId))
        {
            Error(issues, "gateway.metadata.siteId", "站点标识只能包含字母、数字、下划线和连字符");
        }

        if (string.IsNullOrWhiteSpace(gateway.Metadata.Name) || gateway.Metadata.Name.Length > 80)
        {
            Error(issues, "gateway.metadata.name", "站点名称不能为空，且不超过 80 个字符");
        }

        if (!LogLevels.Contains(gateway.Spec.LogLevel, StringComparer.Ordinal))
        {
            Error(issues, "gateway.spec.logLevel", "日志级别必须是 Trace、Debug、Information、Warning、Error 或 Critical");
        }

        if (gateway.Spec.Features.ProgramWrite)
        {
            Warning(issues, "gateway.spec.features.programWrite", "程序写入尚未实现，建议保持关闭");
        }

        var interval = gateway.Spec.Acquisition.DefaultIntervalMs;
        if (interval is < 100 or > 86_400_000)
        {
            Error(issues, "gateway.spec.acquisition.defaultIntervalMs", "默认采集周期需在 100 到 86400000 毫秒之间");
        }

        if (bundle.Devices.Count == 0)
        {
            Error(issues, "devices", "至少需要一台 Fanuc 设备");
        }

        var deviceIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var device in bundle.Devices)
        {
            var id = device.Metadata.Id ?? "";
            var path = string.IsNullOrEmpty(id) ? "devices" : $"devices/{id}";
            if (!SafeId().IsMatch(id))
            {
                Error(issues, path, "设备 Id 只能包含字母、数字、下划线和连字符，且必须以字母或数字开头");
            }
            else if (!deviceIds.Add(id))
            {
                Error(issues, path, "设备 Id 重复");
            }

            if (!string.Equals(device.ApiVersion, StudioApi.Version, StringComparison.Ordinal) || device.Kind != "Device")
            {
                Error(issues, path, "设备文档的 apiVersion 或 kind 不正确");
            }

            if (string.IsNullOrWhiteSpace(device.Metadata.DisplayName))
            {
                Error(issues, $"{path}.displayName", "请填写设备显示名称");
            }

            if (!Adapters.Contains(device.Spec.Adapter, StringComparer.Ordinal))
            {
                Error(issues, $"{path}.adapter", "M1 只支持 fanuc.fake 和 fanuc.focas");
            }

            if (FanucPointCatalog.IsFanuc(device.Spec.Adapter))
            {
                var templateId = (device.Spec.PointTemplateId ?? "").Trim();
                device.Spec.PointTemplateId = templateId;
                if (!SafeId().IsMatch(templateId))
                {
                    Error(issues, $"{path}.pointTemplateId", "请选择点位模板。一类模板给多台同类设备用，不必每台各写一张地址表。");
                }
                else
                {
                    var template = bundle.PointTemplates.FirstOrDefault(item =>
                        string.Equals(item.Metadata.Id, templateId, StringComparison.OrdinalIgnoreCase));
                    if (template is null)
                    {
                        Error(issues, $"{path}.pointTemplateId", $"点位模板「{templateId}」不存在。");
                    }
                    else if (!FanucPointCatalog.MatchesFamily(template.Spec.Adapter, device.Spec.Adapter))
                    {
                        Error(
                            issues,
                            $"{path}.pointTemplateId",
                            $"点位模板「{templateId}」的适配器族是「{template.Spec.Adapter}」，与设备适配器「{device.Spec.Adapter}」不一致。发那科设备请使用发那科模板。");
                    }
                }
            }

            if (device.Spec.IntervalMs is < 100 or > 86_400_000)
            {
                Error(issues, $"{path}.intervalMs", "采集周期需在 100 到 86400000 毫秒之间");
            }

            if (string.IsNullOrWhiteSpace(device.Spec.Connection.Host) || device.Spec.Connection.Host.Length > 253)
            {
                Error(issues, $"{path}.connection.host", "请填写设备地址");
            }

            if (device.Spec.Connection.Port is < 1 or > 65535)
            {
                Error(issues, $"{path}.connection.port", "端口需在 1 到 65535 之间");
            }

            if (device.Spec.Connection.FocasTimeoutMs is < 100 or > 120_000)
            {
                Error(issues, $"{path}.connection.focasTimeoutMs", "FOCAS 超时需在 100 到 120000 毫秒之间");
            }
        }

        if (bundle.Devices.Count > 0 && bundle.Devices.All(device => !device.Spec.Enabled))
        {
            Warning(issues, "devices", "没有启用的设备，发布后不会采集数据");
        }

        var templateIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        bundle.PointTemplates ??= [];
        foreach (var template in bundle.PointTemplates)
        {
            template.Metadata ??= new PointTemplateMetadata();
            template.Spec ??= new PointTemplateSpec();
            template.Spec.Points ??= [];
            var id = (template.Metadata.Id ?? "").Trim();
            template.Metadata.Id = id;
            var path = string.IsNullOrEmpty(id) ? "point-templates" : $"point-templates/{id}";
            if (!SafeId().IsMatch(id))
            {
                Error(issues, path, "点位模板 Id 只能包含字母、数字、下划线和连字符，且必须以字母或数字开头");
            }
            else if (!templateIds.Add(id))
            {
                Error(issues, path, "点位模板 Id 重复");
            }

            if (!string.Equals(template.ApiVersion, StudioApi.Version, StringComparison.Ordinal) || template.Kind != "PointTemplate")
            {
                Error(issues, path, "点位模板的 apiVersion 或 kind 不正确");
            }

            if (string.IsNullOrWhiteSpace(template.Metadata.DisplayName) || template.Metadata.DisplayName.Length > 128)
            {
                Error(issues, $"{path}.displayName", "请填写模板显示名称，且不超过 128 个字符");
            }

            template.Spec.Adapter = (template.Spec.Adapter ?? "").Trim().ToLowerInvariant();
            if (!string.Equals(template.Spec.Adapter, FanucPointCatalog.Family, StringComparison.Ordinal))
            {
                Error(issues, $"{path}.adapter", "M1 点位模板只支持发那科（adapter: fanuc）。fanuc.fake 与 fanuc.focas 共用这一族，不能按其他品牌编目录。");
            }

            ValidatePointList(issues, path, template.Spec.Points, "模板点位", fanuc: true);
            if (template.Spec.Points.Count == 0)
            {
                Warning(issues, path, "点位模板还没有任何点");
            }
        }

        var pointDevices = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var set in bundle.PointSets)
        {
            var deviceId = set.Metadata.DeviceId ?? "";
            var path = $"points/{deviceId}";
            if (!deviceIds.Contains(deviceId))
            {
                Error(issues, path, "点位集引用了不存在的设备");
                continue;
            }

            if (!pointDevices.Add(deviceId))
            {
                Error(issues, path, "同一设备有多份点位集");
            }

            if (!string.Equals(set.ApiVersion, StudioApi.Version, StringComparison.Ordinal) || set.Kind != "PointSet")
            {
                Error(issues, path, "点位文档的 apiVersion 或 kind 不正确");
            }

            var device = bundle.Devices.First(item =>
                string.Equals(item.Metadata.Id, deviceId, StringComparison.OrdinalIgnoreCase));
            var fanuc = FanucPointCatalog.IsFanuc(device.Spec.Adapter);
            ValidatePointList(issues, path, set.Spec.Points, "本机覆盖点位", fanuc);
        }

        foreach (var device in bundle.Devices)
        {
            if (!FanucPointCatalog.IsFanuc(device.Spec.Adapter))
            {
                continue;
            }

            var template = bundle.PointTemplates.FirstOrDefault(item =>
                string.Equals(item.Metadata.Id, device.Spec.PointTemplateId, StringComparison.OrdinalIgnoreCase));
            if (template is null)
            {
                continue;
            }

            var set = bundle.PointSets.FirstOrDefault(item =>
                string.Equals(item.Metadata.DeviceId, device.Metadata.Id, StringComparison.OrdinalIgnoreCase));
            var enabledPoints = PointExpansion.EffectivePoints(template, set).Count(point => point.Enabled);
            if (device.Spec.Enabled && enabledPoints == 0)
            {
                Error(issues, $"devices/{device.Metadata.Id}.pointTemplateId", "启用的设备至少需要一个启用的点位。请在点位模板里启用，或去掉把点位关掉的本机覆盖。");
            }
        }

        ValidateMqtt(bundle.Mqtt, issues);
        return new ValidationResult
        {
            Valid = issues.All(issue => issue.Severity != "error"),
            Issues = issues
        };
    }

    private static void ValidateMqtt(MqttSinkDocument mqtt, List<ValidationIssue> issues)
    {
        if (!string.Equals(mqtt.ApiVersion, StudioApi.Version, StringComparison.Ordinal) || mqtt.Kind != "MqttSink")
        {
            Error(issues, "sinks/mqtt", "MQTT 文档的 apiVersion 或 kind 不正确");
        }

        if (!SafeId().IsMatch(mqtt.Metadata.Id ?? ""))
        {
            Error(issues, "sinks/mqtt.metadata.id", "MQTT 标识不合法");
        }

        var broker = mqtt.Spec.Broker;
        if (string.IsNullOrWhiteSpace(broker.Host))
        {
            Error(issues, "sinks/mqtt.broker.host", "请填写 Broker 地址");
        }

        if (broker.Port is < 1 or > 65535)
        {
            Error(issues, "sinks/mqtt.broker.port", "Broker 端口需在 1 到 65535 之间");
        }

        if (string.IsNullOrWhiteSpace(broker.ClientId))
        {
            Error(issues, "sinks/mqtt.broker.clientId", "请填写 Client Id");
        }

        if (mqtt.Spec.Qos is < 0 or > 2)
        {
            Error(issues, "sinks/mqtt.qos", "QoS 只能是 0、1 或 2");
        }

        if (string.IsNullOrWhiteSpace(mqtt.Spec.TopicTemplate)
            || !mqtt.Spec.TopicTemplate.Contains("{deviceId}", StringComparison.Ordinal)
            || !mqtt.Spec.TopicTemplate.Contains("{point}", StringComparison.Ordinal))
        {
            Error(issues, "sinks/mqtt.topicTemplate", "主题模板需包含 {deviceId} 和 {point}");
        }

        if (string.IsNullOrWhiteSpace(mqtt.Spec.StatusTopic)
            || !mqtt.Spec.StatusTopic.Contains("{deviceId}", StringComparison.Ordinal))
        {
            Warning(issues, "sinks/mqtt.statusTopic", "状态主题建议包含 {deviceId}");
        }

        if (string.IsNullOrWhiteSpace(broker.PasswordFromEnv) && !string.IsNullOrWhiteSpace(broker.UsernameFromEnv))
        {
            Warning(issues, "sinks/mqtt.broker.passwordFromEnv", "已填写用户名环境变量，但没有密码环境变量");
        }
    }

    private static void ValidatePointList(
        List<ValidationIssue> issues,
        string path,
        List<PointDefinition> points,
        string role,
        bool fanuc)
    {
        var pointIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var point in points)
        {
            point.Id = (point.Id ?? "").Trim();
            var known = fanuc && FanucPointCatalog.TryNormalize(point);
            var pointId = point.Id ?? "";
            var pointPath = $"{path}/{pointId}";
            if (fanuc && !known)
            {
                Error(
                    issues,
                    pointPath,
                    $"{role} Id「{pointId}」不在发那科适配器目录中。只能使用 {FanucPointCatalog.IdList}。覆盖不能新增目录以外的点，也不能手填协议地址。");
            }

            if (!SafeId().IsMatch(pointId))
            {
                Error(issues, pointPath, "点位 Id 只能包含字母、数字、下划线和连字符");
            }
            else if (!pointIds.Add(pointId))
            {
                Error(issues, pointPath, "点位 Id 重复");
            }

            if (!fanuc && string.IsNullOrWhiteSpace(point.Address))
            {
                Error(issues, $"{pointPath}.address", "请填写点位地址");
            }

            if (!DataTypes.Contains(point.DataType, StringComparer.Ordinal))
            {
                Error(issues, $"{pointPath}.dataType", "数据类型必须是 string、bool、int32、int64、float、double、int 或 number");
            }

            if (!double.IsFinite(point.Scale))
            {
                Error(issues, $"{pointPath}.scale", "倍率必须是有限数字");
            }

            if (!double.IsFinite(point.Deadband) || point.Deadband < 0)
            {
                Error(issues, $"{pointPath}.deadband", "死区必须是大于等于 0 的有限数字");
            }
        }
    }

    public static bool IsSafeId(string? id) => id is not null && SafeId().IsMatch(id);

    private static void Error(List<ValidationIssue> issues, string path, string message) =>
        issues.Add(new ValidationIssue { Severity = "error", Path = path, Message = message });

    private static void Warning(List<ValidationIssue> issues, string path, string message) =>
        issues.Add(new ValidationIssue { Severity = "warning", Path = path, Message = message });

    [GeneratedRegex("^[A-Za-z0-9][A-Za-z0-9_-]{0,63}$")]
    private static partial Regex SafeId();
}
