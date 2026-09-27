namespace IotDaq.Persistence;

public sealed class ToolRow
{
    public string Id { get; set; } = "";

    public string ToolNumber { get; set; } = "";

    public string Description { get; set; } = "";

    public double? LifeLimitCount { get; set; }

    public double? LifeLimitMinutes { get; set; }

    public double WarningPercent { get; set; } = 80;

    public bool Enabled { get; set; } = true;

    public string UpdatedBy { get; set; } = "";

    public long UpdatedUnixMs { get; set; }
}

public sealed class ToolPocketRow
{
    public string Id { get; set; } = "";

    public string DeviceId { get; set; } = "";

    public int Pocket { get; set; }

    public string ToolNumber { get; set; } = "";
}

public sealed class ToolLifeRow
{
    public string Id { get; set; } = "";

    public string DeviceId { get; set; } = "";

    public string ToolNumber { get; set; } = "";

    public double UsedCount { get; set; }

    public double UsedCuttingMs { get; set; }

    public string Source { get; set; } = "";

    public string Level { get; set; } = "ok";

    public long UpdatedUnixMs { get; set; }
}

public sealed class ToolCursorRow
{
    public string DeviceId { get; set; } = "";

    public string ToolNumber { get; set; } = "";

    public double? LastPartCount { get; set; }

    public double? LastCycleSeconds { get; set; }

    public long LastUnixMs { get; set; }

    public bool WasCutting { get; set; }
}

public sealed class ToolChangeRow
{
    public string Id { get; set; } = "";

    public string DeviceId { get; set; } = "";

    public int? Pocket { get; set; }

    public string OldToolNumber { get; set; } = "";

    public string NewToolNumber { get; set; } = "";

    public string Note { get; set; } = "";

    public string Actor { get; set; } = "";

    public long UnixMs { get; set; }

    public bool ResetLife { get; set; } = true;
}

public sealed class NcProgramRow
{
    public string Id { get; set; } = "";

    public string Name { get; set; } = "";

    public string Comment { get; set; } = "";

    public string Status { get; set; } = "draft";

    public string CreatedBy { get; set; } = "";

    public long UpdatedUnixMs { get; set; }
}

public sealed class NcProgramVersionRow
{
    public string Id { get; set; } = "";

    public string ProgramId { get; set; } = "";

    public int Version { get; set; }

    public string Content { get; set; } = "";

    public string Checksum { get; set; } = "";

    public string Comment { get; set; } = "";

    public string Status { get; set; } = "draft";

    public string UploadedBy { get; set; } = "";

    public long UploadedUnixMs { get; set; }

    public string? ApprovedBy { get; set; }

    public long? ApprovedUnixMs { get; set; }
}

public sealed class NcProgramDeviceRow
{
    public string ProgramId { get; set; } = "";

    public string DeviceId { get; set; } = "";
}

public sealed class NcTransferRow
{
    public string Id { get; set; } = "";

    public string ProgramId { get; set; } = "";

    public string VersionId { get; set; } = "";

    public string DeviceId { get; set; } = "";

    public string Direction { get; set; } = "";

    public string Channel { get; set; } = "";

    public string Checksum { get; set; } = "";

    public string Actor { get; set; } = "";

    public long UnixMs { get; set; }

    public string Result { get; set; } = "";

    public string Message { get; set; } = "";
}

public sealed class EnrollmentTokenRow
{
    public string Id { get; set; } = "";

    public string TokenHash { get; set; } = "";

    public string Label { get; set; } = "";

    public long ExpiresUnixMs { get; set; }

    public int Uses { get; set; }

    public int MaxUses { get; set; } = 1;

    public bool Revoked { get; set; }

    public string CreatedBy { get; set; } = "";

    public long CreatedUnixMs { get; set; }
}

public sealed class FleetGatewayRow
{
    public string Id { get; set; } = "";

    public string Name { get; set; } = "";

    public string GroupId { get; set; } = "";

    public string Site { get; set; } = "";

    public string SessionHash { get; set; } = "";

    public string Version { get; set; } = "";

    public string LicenseSummary { get; set; } = "";

    public int DeviceCount { get; set; }

    public int OnlineLinks { get; set; }

    public int OfflineLinks { get; set; }

    public double? WorkingSetMb { get; set; }

    public string SummaryJson { get; set; } = "";

    public string Status { get; set; } = "offline";

    public long LastSeenUnixMs { get; set; }

    public long EnrolledUnixMs { get; set; }
}

public sealed class FleetGroupRow
{
    public string Id { get; set; } = "";

    public string Name { get; set; } = "";
}

public sealed class CentralTemplateRow
{
    public string Id { get; set; } = "";

    public string Key { get; set; } = "";

    public string Name { get; set; } = "";

    public string Kind { get; set; } = "";

    public int Version { get; set; }

    public string BodyJson { get; set; } = "";

    public string Comment { get; set; } = "";

    public string CreatedBy { get; set; } = "";

    public long CreatedUnixMs { get; set; }
}

public sealed class ConfigPushRow
{
    public string Id { get; set; } = "";

    public string TemplateKey { get; set; } = "";

    public int Version { get; set; }

    public int? PreviousVersion { get; set; }

    public string GroupId { get; set; } = "";

    public string GatewayIdsJson { get; set; } = "[]";

    public string DiffText { get; set; } = "";

    public string ConflictPolicy { get; set; } = "central-wins";

    public string Status { get; set; } = "pending";

    public bool Rollback { get; set; }

    public string CreatedBy { get; set; } = "";

    public long CreatedUnixMs { get; set; }
}

public sealed class ConfigPushTargetRow
{
    public string Id { get; set; } = "";

    public string PushId { get; set; } = "";

    public string GatewayId { get; set; } = "";

    public string Status { get; set; } = "pending";

    public string Message { get; set; } = "";

    public long UpdatedUnixMs { get; set; }
}

public sealed class RolloutRow
{
    public string Id { get; set; } = "";

    public string Version { get; set; } = "";

    public string Sha256 { get; set; } = "";

    public string PackagePath { get; set; } = "";

    public string Status { get; set; } = "staged";

    public string CreatedBy { get; set; } = "";

    public long CreatedUnixMs { get; set; }
}

public sealed class RolloutTargetRow
{
    public string Id { get; set; } = "";

    public string RolloutId { get; set; } = "";

    public string GatewayId { get; set; } = "";

    public string Status { get; set; } = "pending";

    public string Message { get; set; } = "";

    public long UpdatedUnixMs { get; set; }
}

public sealed class CentralAlertRow
{
    public string Id { get; set; } = "";

    public string GatewayId { get; set; } = "";

    public string Kind { get; set; } = "offline";

    public string Message { get; set; } = "";

    public bool Active { get; set; } = true;

    public long RaisedUnixMs { get; set; }

    public long? ClearedUnixMs { get; set; }
}

public sealed class CentralAuditRow
{
    public long Id { get; set; }

    public string GatewayId { get; set; } = "";

    public string Username { get; set; } = "";

    public string Role { get; set; } = "";

    public string Action { get; set; } = "";

    public string Target { get; set; } = "";

    public string Detail { get; set; } = "";

    public long UnixMs { get; set; }
}

public sealed class CentralDocumentRow
{
    public string Kind { get; set; } = "";

    public string TemplateKey { get; set; } = "";

    public int Version { get; set; }

    public string BodyJson { get; set; } = "";

    public bool Dirty { get; set; }

    public string Conflict { get; set; } = "";

    public long UpdatedUnixMs { get; set; }
}
