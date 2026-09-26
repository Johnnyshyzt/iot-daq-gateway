namespace IotDaq.Persistence;

/// <summary>
/// Edge schema. Timestamps are Unix milliseconds so the history table can later
/// become a TimescaleDB hypertable on a timestamptz generated from this column.
/// No provider-specific column types are used.
/// </summary>
public sealed class SchemaInfoRow
{
    public int Id { get; set; } = 1;

    public int Version { get; set; }

    public string Provider { get; set; } = "";
}

public sealed class CatalogMetaRow
{
    public int Id { get; set; } = 1;

    public string Version { get; set; } = "";

    public string Source { get; set; } = "";
}

public sealed class BrandRow
{
    public string Id { get; set; } = "";

    public string NameZh { get; set; } = "";

    public string NameEn { get; set; } = "";

    public int SortOrder { get; set; }
}

public sealed class ControllerModelRow
{
    public string Id { get; set; } = "";

    public string BrandId { get; set; } = "";

    public string Name { get; set; } = "";
}

public sealed class CatalogItemRow
{
    public string Id { get; set; } = "";

    public string NameZh { get; set; } = "";

    public string DataType { get; set; } = "string";

    public string Unit { get; set; } = "";

    public string Category { get; set; } = "";

    public bool BrandSpecific { get; set; }

    public string? BrandId { get; set; }
}

public sealed class BrandItemRow
{
    public string BrandId { get; set; } = "";

    public string SourceName { get; set; } = "";

    public string ItemId { get; set; } = "";

    public bool BrandSpecific { get; set; }
}

public sealed class AdapterRow
{
    public string Id { get; set; } = "";

    public string? BrandId { get; set; }

    public string Kind { get; set; } = "";

    public string Protocol { get; set; } = "";

    public int Phase { get; set; }

    public string DisplayName { get; set; } = "";

    public string? Note { get; set; }

    public string ParametersJson { get; set; } = "[]";
}

public sealed class DeviceGroupRow
{
    public string Id { get; set; } = "";

    public string Name { get; set; } = "";

    public string Workshop { get; set; } = "";

    public string Line { get; set; } = "";
}

public sealed class UserRow
{
    public string Username { get; set; } = "";

    public string Role { get; set; } = "";

    public string PasswordHash { get; set; } = "";

    public bool MustChangePassword { get; set; }

    public string Mode { get; set; } = "";
}

public sealed class ConfigBundleRow
{
    public string Slot { get; set; } = "";

    public string Hash { get; set; } = "";

    public string Json { get; set; } = "";

    public long UpdatedUnixMs { get; set; }
}

public sealed class ConfigGatewayRow
{
    public string Slot { get; set; } = "";

    public string SiteId { get; set; } = "";

    public string Name { get; set; } = "";

    public string LogLevel { get; set; } = "";

    public bool ProgramWrite { get; set; }

    public int DefaultIntervalMs { get; set; }

    public bool ChangeOnly { get; set; }
}

public sealed class ConfigDeviceRow
{
    public string Slot { get; set; } = "";

    public string Id { get; set; } = "";

    public string DisplayName { get; set; } = "";

    public string? BrandId { get; set; }

    public string? ControllerModelId { get; set; }

    public string Adapter { get; set; } = "";

    public bool Enabled { get; set; }

    public int IntervalMs { get; set; }

    public string? PointTemplateId { get; set; }

    public string Host { get; set; } = "";

    public int Port { get; set; }

    public int? TimeoutMs { get; set; }

    public string? Path { get; set; }

    public string? Namespace { get; set; }

    public string? Workshop { get; set; }

    public string? Line { get; set; }

    public string? GroupId { get; set; }
}

public sealed class ConfigTemplateRow
{
    public string Slot { get; set; } = "";

    public string Id { get; set; } = "";

    public string DisplayName { get; set; } = "";

    public string Adapter { get; set; } = "";
}

public sealed class ConfigTemplatePointRow
{
    public string Slot { get; set; } = "";

    public string TemplateId { get; set; } = "";

    public string PointId { get; set; } = "";

    public string Address { get; set; } = "";

    public string DataType { get; set; } = "";

    public string Unit { get; set; } = "";

    public double Scale { get; set; }

    public double Deadband { get; set; }

    public bool Enabled { get; set; }

    public int SortOrder { get; set; }
}

public sealed class ConfigPointSetRow
{
    public string Slot { get; set; } = "";

    public string DeviceId { get; set; } = "";

    public string PointId { get; set; } = "";

    public string Address { get; set; } = "";

    public string DataType { get; set; } = "";

    public string Unit { get; set; } = "";

    public double Scale { get; set; }

    public double Deadband { get; set; }

    public bool Enabled { get; set; }

    public int SortOrder { get; set; }
}

public sealed class ConfigMqttRow
{
    public string Slot { get; set; } = "";

    public string BrokerHost { get; set; } = "";

    public int BrokerPort { get; set; }

    public string ClientId { get; set; } = "";

    public string? UsernameFromEnv { get; set; }

    public string? PasswordFromEnv { get; set; }

    public bool Tls { get; set; }

    public int Qos { get; set; }

    public bool Retain { get; set; }

    public string TopicTemplate { get; set; } = "";

    public string StatusTopic { get; set; } = "";
}

public sealed class ConfigRevisionRow
{
    public string Id { get; set; } = "";

    public string Revision { get; set; } = "";

    public long CreatedUnixMs { get; set; }

    public string Action { get; set; } = "";

    public string? Note { get; set; }

    public string BundleJson { get; set; } = "";
}

public sealed class SampleLatestRow
{
    public string DeviceId { get; set; } = "";

    public string PointId { get; set; } = "";

    public string? ValueText { get; set; }

    public double? NumericValue { get; set; }

    public string Quality { get; set; } = "good";

    public string? Unit { get; set; }

    public long TimestampUnixMs { get; set; }
}

public sealed class SampleHistoryRow
{
    public string Id { get; set; } = "";

    public string DeviceId { get; set; } = "";

    public string PointId { get; set; } = "";

    public string? ValueText { get; set; }

    public double? NumericValue { get; set; }

    public string Quality { get; set; } = "good";

    public string? Unit { get; set; }

    public long TimestampUnixMs { get; set; }
}

public sealed class AlarmRow
{
    public string Id { get; set; } = "";

    public string DeviceId { get; set; } = "";

    public string PointId { get; set; } = "";

    public string Message { get; set; } = "";

    public string Severity { get; set; } = "alarm";

    public bool Active { get; set; }

    public long RaisedUnixMs { get; set; }
}
