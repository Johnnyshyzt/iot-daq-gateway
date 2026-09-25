using Studio.Contracts;

namespace Studio.Host.Config;

public static class ConfigDiffer
{
    public static DiffView Compare(ConfigBundle published, ConfigBundle draft)
    {
        var changes = new List<ConfigChange>();
        CompareGateway(changes, published.Gateway, draft.Gateway);
        CompareDevices(changes, published.Devices, draft.Devices);
        ComparePoints(changes, published.PointSets, draft.PointSets);
        CompareMqtt(changes, published.Mqtt, draft.Mqtt);
        return new DiffView
        {
            Dirty = changes.Count > 0,
            Changes = changes
        };
    }

    private static void CompareGateway(List<ConfigChange> changes, GatewayDocument before, GatewayDocument after)
    {
        const string path = "gateway";
        Field(changes, path, "站点标识", before.Metadata.SiteId, after.Metadata.SiteId);
        Field(changes, path, "站点名称", before.Metadata.Name, after.Metadata.Name);
        Field(changes, path, "日志级别", before.Spec.LogLevel, after.Spec.LogLevel);
        Field(changes, path, "程序写入", YesNo(before.Spec.Features.ProgramWrite), YesNo(after.Spec.Features.ProgramWrite));
        Field(changes, path, "默认采集周期", Milliseconds(before.Spec.Acquisition.DefaultIntervalMs), Milliseconds(after.Spec.Acquisition.DefaultIntervalMs));
        Field(changes, path, "仅变化上传", YesNo(before.Spec.Acquisition.ChangeOnly), YesNo(after.Spec.Acquisition.ChangeOnly));
    }

    private static void CompareDevices(List<ConfigChange> changes, List<DeviceDocument> before, List<DeviceDocument> after)
    {
        var published = ById(before, device => device.Metadata.Id);
        var draft = ById(after, device => device.Metadata.Id);
        foreach (var id in draft.Keys.Except(published.Keys, StringComparer.Ordinal).Order(StringComparer.Ordinal))
        {
            changes.Add(new ConfigChange
            {
                Path = $"devices/{id}",
                Kind = "added",
                Summary = $"新增设备 {id}（{draft[id].Metadata.DisplayName}）"
            });
        }

        foreach (var id in published.Keys.Except(draft.Keys, StringComparer.Ordinal).Order(StringComparer.Ordinal))
        {
            changes.Add(new ConfigChange
            {
                Path = $"devices/{id}",
                Kind = "removed",
                Summary = $"移除设备 {id}"
            });
        }

        foreach (var id in published.Keys.Intersect(draft.Keys, StringComparer.Ordinal).Order(StringComparer.Ordinal))
        {
            var left = published[id];
            var right = draft[id];
            var path = $"devices/{id}";
            Field(changes, path, "显示名称", left.Metadata.DisplayName, right.Metadata.DisplayName);
            Field(changes, path, "适配器", left.Spec.Adapter, right.Spec.Adapter);
            Field(changes, path, "启用", YesNo(left.Spec.Enabled), YesNo(right.Spec.Enabled));
            Field(changes, path, "采集周期", Milliseconds(left.Spec.IntervalMs), Milliseconds(right.Spec.IntervalMs));
            Field(changes, path, "地址", left.Spec.Connection.Host, right.Spec.Connection.Host);
            Field(changes, path, "端口", left.Spec.Connection.Port.ToString(System.Globalization.CultureInfo.InvariantCulture), right.Spec.Connection.Port.ToString(System.Globalization.CultureInfo.InvariantCulture));
            Field(changes, path, "FOCAS 超时", Timeout(left.Spec.Connection.FocasTimeoutMs), Timeout(right.Spec.Connection.FocasTimeoutMs));
        }
    }

    private static void ComparePoints(List<ConfigChange> changes, List<PointSetDocument> before, List<PointSetDocument> after)
    {
        var published = ById(before, set => set.Metadata.DeviceId);
        var draft = ById(after, set => set.Metadata.DeviceId);
        foreach (var id in draft.Keys.Except(published.Keys, StringComparer.Ordinal).Order(StringComparer.Ordinal))
        {
            changes.Add(new ConfigChange
            {
                Path = $"points/{id}",
                Kind = "added",
                Summary = $"新增点位集 {id}（{draft[id].Spec.Points.Count} 个点）"
            });
        }

        foreach (var id in published.Keys.Except(draft.Keys, StringComparer.Ordinal).Order(StringComparer.Ordinal))
        {
            changes.Add(new ConfigChange
            {
                Path = $"points/{id}",
                Kind = "removed",
                Summary = $"移除点位集 {id}"
            });
        }

        foreach (var id in published.Keys.Intersect(draft.Keys, StringComparer.Ordinal).Order(StringComparer.Ordinal))
        {
            ComparePointList(changes, id, published[id].Spec.Points, draft[id].Spec.Points);
        }
    }

    private static void ComparePointList(List<ConfigChange> changes, string deviceId, List<PointDefinition> before, List<PointDefinition> after)
    {
        var published = ById(before, point => point.Id);
        var draft = ById(after, point => point.Id);
        foreach (var id in draft.Keys.Except(published.Keys, StringComparer.Ordinal).Order(StringComparer.Ordinal))
        {
            changes.Add(new ConfigChange
            {
                Path = $"points/{deviceId}/{id}",
                Kind = "added",
                Summary = $"设备 {deviceId} 新增点位 {id}"
            });
        }

        foreach (var id in published.Keys.Except(draft.Keys, StringComparer.Ordinal).Order(StringComparer.Ordinal))
        {
            changes.Add(new ConfigChange
            {
                Path = $"points/{deviceId}/{id}",
                Kind = "removed",
                Summary = $"设备 {deviceId} 移除点位 {id}"
            });
        }

        foreach (var id in published.Keys.Intersect(draft.Keys, StringComparer.Ordinal).Order(StringComparer.Ordinal))
        {
            var left = published[id];
            var right = draft[id];
            var path = $"points/{deviceId}/{id}";
            Field(changes, path, "地址", left.Address, right.Address);
            Field(changes, path, "类型", left.DataType, right.DataType);
            Field(changes, path, "单位", left.Unit, right.Unit);
            Field(changes, path, "倍率", Number(left.Scale), Number(right.Scale));
            Field(changes, path, "死区", Number(left.Deadband), Number(right.Deadband));
            Field(changes, path, "启用", YesNo(left.Enabled), YesNo(right.Enabled));
        }
    }

    private static void CompareMqtt(List<ConfigChange> changes, MqttSinkDocument before, MqttSinkDocument after)
    {
        const string path = "sinks/mqtt";
        Field(changes, path, "标识", before.Metadata.Id, after.Metadata.Id);
        Field(changes, path, "Broker 地址", before.Spec.Broker.Host, after.Spec.Broker.Host);
        Field(changes, path, "Broker 端口", before.Spec.Broker.Port.ToString(System.Globalization.CultureInfo.InvariantCulture), after.Spec.Broker.Port.ToString(System.Globalization.CultureInfo.InvariantCulture));
        Field(changes, path, "Client Id", before.Spec.Broker.ClientId, after.Spec.Broker.ClientId);
        Field(changes, path, "用户名环境变量", before.Spec.Broker.UsernameFromEnv, after.Spec.Broker.UsernameFromEnv);
        Field(changes, path, "密码环境变量", before.Spec.Broker.PasswordFromEnv, after.Spec.Broker.PasswordFromEnv);
        Field(changes, path, "TLS", YesNo(before.Spec.Broker.Tls), YesNo(after.Spec.Broker.Tls));
        Field(changes, path, "主题模板", before.Spec.TopicTemplate, after.Spec.TopicTemplate);
        Field(changes, path, "QoS", before.Spec.Qos.ToString(System.Globalization.CultureInfo.InvariantCulture), after.Spec.Qos.ToString(System.Globalization.CultureInfo.InvariantCulture));
        Field(changes, path, "保留消息", YesNo(before.Spec.Retain), YesNo(after.Spec.Retain));
        Field(changes, path, "状态主题", before.Spec.StatusTopic, after.Spec.StatusTopic);
    }

    private static Dictionary<string, T> ById<T>(IEnumerable<T> items, Func<T, string?> id)
    {
        var map = new Dictionary<string, T>(StringComparer.Ordinal);
        foreach (var item in items)
        {
            map[id(item) ?? ""] = item;
        }

        return map;
    }

    private static void Field(List<ConfigChange> changes, string path, string label, string? before, string? after)
    {
        if (string.Equals(before ?? "", after ?? "", StringComparison.Ordinal))
        {
            return;
        }

        changes.Add(new ConfigChange
        {
            Path = path,
            Kind = "modified",
            Summary = $"{label}：{Show(before)} → {Show(after)}"
        });
    }

    private static string Show(string? value) => string.IsNullOrEmpty(value) ? "（空）" : value;

    private static string YesNo(bool value) => value ? "是" : "否";

    private static string Milliseconds(int value) => $"{value.ToString(System.Globalization.CultureInfo.InvariantCulture)} ms";

    private static string Timeout(int? value) => value?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "（空）";

    private static string Number(double value) => value.ToString("G", System.Globalization.CultureInfo.InvariantCulture);
}
