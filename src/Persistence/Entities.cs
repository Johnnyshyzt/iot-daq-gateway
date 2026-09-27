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

    public string ContractVersion { get; set; } = "legacy";
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

    public bool Computed { get; set; }
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

    public bool Computed { get; set; }
}

public sealed class AlarmRow
{
    public string Id { get; set; } = "";

    public string DeviceId { get; set; } = "";

    public string PointId { get; set; } = "";

    public string Code { get; set; } = "";

    public string Message { get; set; } = "";

    public string Severity { get; set; } = "alarm";

    public bool Active { get; set; }

    public long RaisedUnixMs { get; set; }

    public long? ClearedUnixMs { get; set; }

    public long? DurationMs { get; set; }

    public bool Acknowledged { get; set; }

    public string? AcknowledgedBy { get; set; }

    public long? AcknowledgedUnixMs { get; set; }
}

public sealed class StateTransitionRow
{
    public string Id { get; set; } = "";

    public string DeviceId { get; set; } = "";

    public string State { get; set; } = "";

    public string RawValue { get; set; } = "";

    public long StartedUnixMs { get; set; }

    public long? EndedUnixMs { get; set; }
}

public sealed class AppSettingRow
{
    public string Key { get; set; } = "";

    public string Value { get; set; } = "";
}

public sealed class AuditEventRow
{
    public long Id { get; set; }

    public long UnixMs { get; set; }

    public string Username { get; set; } = "";

    public string Role { get; set; } = "";

    public string Action { get; set; } = "";

    public string Target { get; set; } = "";

    public string Detail { get; set; } = "";
}

public sealed class LinkStatusRow
{
    public string DeviceId { get; set; } = "";

    public string Status { get; set; } = "";

    public string Message { get; set; } = "";

    public long UnixMs { get; set; }
}

public sealed class HttpPushTargetRow
{
    public string Id { get; set; } = "";

    public string Name { get; set; } = "";

    public bool Enabled { get; set; } = true;

    public string Url { get; set; } = "";

    public string Method { get; set; } = "POST";

    public string HeadersJson { get; set; } = "[]";

    public string AuthKind { get; set; } = "none";

    public string AuthUser { get; set; } = "";

    public string AuthSecret { get; set; } = "";

    public string SignatureHeader { get; set; } = "X-DAQ-Signature";

    public bool SendValues { get; set; } = true;

    public string ValueMode { get; set; } = "change";

    public int PeriodicSeconds { get; set; } = 30;

    public bool SendStatus { get; set; } = true;

    public bool SendAlarms { get; set; } = true;

    public int BatchMax { get; set; } = 50;

    public int BatchIntervalMs { get; set; } = 1000;

    public int TimeoutMs { get; set; } = 8000;

    public int MaxRetries { get; set; } = 8;

    public int BackoffInitialMs { get; set; } = 1000;

    public int BackoffMaxMs { get; set; } = 60_000;

    public long Delivered { get; set; }

    public long Failed { get; set; }

    public long SpoolDropped { get; set; }

    public long LastSuccessUnixMs { get; set; }

    public long LastAttemptUnixMs { get; set; }

    public string LastError { get; set; } = "";

    public long UpdatedUnixMs { get; set; }
}

public sealed class ApiKeyRow
{
    public string Id { get; set; } = "";

    public string Name { get; set; } = "";

    public string Prefix { get; set; } = "";

    public string KeyHash { get; set; } = "";

    public long CreatedUnixMs { get; set; }

    public string CreatedBy { get; set; } = "";

    public long? RevokedUnixMs { get; set; }

    public long? LastUsedUnixMs { get; set; }
}

public sealed class NotificationChannelRow
{
    public string Id { get; set; } = "";

    public string Name { get; set; } = "";

    public string Kind { get; set; } = "";

    public bool Enabled { get; set; } = true;

    public string WebhookUrl { get; set; } = "";

    public string Secret { get; set; } = "";

    public string SecretFromEnv { get; set; } = "";

    public string SmtpHost { get; set; } = "";

    public int SmtpPort { get; set; } = 25;

    public string SmtpUser { get; set; } = "";

    public string MailFrom { get; set; } = "";

    public string MailTo { get; set; } = "";

    public bool SmtpSsl { get; set; } = true;

    public long UpdatedUnixMs { get; set; }
}

public sealed class NotificationRuleRow
{
    public string Id { get; set; } = "";

    public string Name { get; set; } = "";

    public bool Enabled { get; set; } = true;

    public string ChannelId { get; set; } = "";

    public string EscalationChannelId { get; set; } = "";

    public int EscalationMinutes { get; set; }

    public string DeviceIdsJson { get; set; } = "[]";

    public string GroupsJson { get; set; } = "[]";

    public string SeveritiesJson { get; set; } = "[]";

    public string CodeFilter { get; set; } = "";

    public bool OnRaise { get; set; } = true;

    public bool OnClear { get; set; }

    public string QuietStart { get; set; } = "";

    public string QuietEnd { get; set; } = "";

    public int DedupSeconds { get; set; } = 300;

    public int RatePerHour { get; set; } = 30;

    public long UpdatedUnixMs { get; set; }
}

public sealed class NotificationDeliveryRow
{
    public long Id { get; set; }

    public string ChannelId { get; set; } = "";

    public string RuleId { get; set; } = "";

    public string AlarmId { get; set; } = "";

    public string Kind { get; set; } = "";

    public string DeviceId { get; set; } = "";

    public string Code { get; set; } = "";

    public string Severity { get; set; } = "";

    public string Summary { get; set; } = "";

    public string Status { get; set; } = "pending";

    public int Attempts { get; set; }

    public string LastError { get; set; } = "";

    public long CreatedUnixMs { get; set; }

    public long? SentUnixMs { get; set; }

    public long NextAttemptUnixMs { get; set; }
}

public sealed class ReportScheduleRow
{
    public string Id { get; set; } = "default";

    public bool Enabled { get; set; }

    public bool DailyEnabled { get; set; } = true;

    public string DailyTime { get; set; } = "08:00";

    public bool ShiftEnabled { get; set; }

    public string ChannelIdsJson { get; set; } = "[]";

    public string LastDailyKey { get; set; } = "";

    public string LastShiftKey { get; set; } = "";

    public long UpdatedUnixMs { get; set; }
}

public sealed class InstalledLicenseRow
{
    public int Id { get; set; } = 1;

    public string DocumentText { get; set; } = "";

    public string Customer { get; set; } = "";

    public string Edition { get; set; } = "";

    public string ImportedBy { get; set; } = "";

    public long ImportedUnixMs { get; set; }
}

public sealed class SecurityStateRow
{
    public int Id { get; set; } = 1;

    public string Payload { get; set; } = "";

    public long UpdatedUnixMs { get; set; }
}

public sealed class SelfTestRunRow
{
    public long Id { get; set; }

    public string DeviceId { get; set; } = "";

    public bool Passed { get; set; }

    public string Summary { get; set; } = "";

    public string StagesJson { get; set; } = "[]";

    public long StartedUnixMs { get; set; }

    public long FinishedUnixMs { get; set; }
}

public sealed class ComputedPointRow
{
    public string Id { get; set; } = "";

    public string Scope { get; set; } = "device";

    public string OwnerId { get; set; } = "";

    public string PointId { get; set; } = "";

    public string Name { get; set; } = "";

    public string Unit { get; set; } = "";

    public string Expression { get; set; } = "";

    public bool Enabled { get; set; } = true;

    public long UpdatedUnixMs { get; set; }

    public string UpdatedBy { get; set; } = "";
}

public sealed class EdgeRuleRow
{
    public string Id { get; set; } = "";

    public string Name { get; set; } = "";

    public bool Enabled { get; set; } = true;

    public string Scope { get; set; } = "all";

    public string OwnerId { get; set; } = "";

    public string Expression { get; set; } = "";

    public long DurationMs { get; set; }

    public long DebounceMs { get; set; }

    public string ActionsJson { get; set; } = "[]";

    public string TemplateKey { get; set; } = "";

    public long UpdatedUnixMs { get; set; }

    public string UpdatedBy { get; set; } = "";
}

public sealed class RuleLogRow
{
    public long Id { get; set; }

    public string RuleId { get; set; } = "";

    public string DeviceId { get; set; } = "";

    public long UnixMs { get; set; }

    public bool Fired { get; set; }

    public string Message { get; set; } = "";

    public string Detail { get; set; } = "";
}

public sealed class RuleEventRow
{
    public long Id { get; set; }

    public string RuleId { get; set; } = "";

    public string DeviceId { get; set; } = "";

    public string Name { get; set; } = "";

    public string Message { get; set; } = "";

    public long UnixMs { get; set; }

    public bool Published { get; set; }
}

public sealed class DowntimeReasonRow
{
    public string Id { get; set; } = "";

    public string? ParentId { get; set; }

    public string Code { get; set; } = "";

    public string Name { get; set; } = "";

    public int Sort { get; set; }

    public bool Enabled { get; set; } = true;
}

public sealed class DowntimeEventRow
{
    public string Id { get; set; } = "";

    public string DeviceId { get; set; } = "";

    public string State { get; set; } = "";

    public long StartedUnixMs { get; set; }

    public long? EndedUnixMs { get; set; }

    public string? ReasonId { get; set; }

    public string Note { get; set; } = "";

    public string Source { get; set; } = "";

    public string? RuleId { get; set; }

    public string? AssignedBy { get; set; }

    public long? AssignedUnixMs { get; set; }

    public string? TransitionId { get; set; }
}

public sealed class StateMapRow
{
    public string Id { get; set; } = "";

    public string Scope { get; set; } = "brand";

    public string OwnerId { get; set; } = "";

    public string RawValue { get; set; } = "";

    public string State { get; set; } = "";
}

public sealed class PlannedStopRow
{
    public string Id { get; set; } = "";

    public string Scope { get; set; } = "device";

    public string OwnerId { get; set; } = "";

    public string Name { get; set; } = "";

    public long StartUnixMs { get; set; }

    public long EndUnixMs { get; set; }
}

public sealed class CycleTimeRow
{
    public string Id { get; set; } = "";

    public string Scope { get; set; } = "device";

    public string OwnerId { get; set; } = "";

    public string Program { get; set; } = "";

    public double IdealSeconds { get; set; }
}

public sealed class ScrapEntryRow
{
    public string Id { get; set; } = "";

    public string DeviceId { get; set; } = "";

    public long UnixMs { get; set; }

    public double Quantity { get; set; }

    public string Note { get; set; } = "";

    public string EnteredBy { get; set; } = "";

    public string Source { get; set; } = "manual";
}
