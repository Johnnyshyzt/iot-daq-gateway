namespace Studio.Contracts;

public sealed class ApiError
{
    public string Code { get; set; } = "";

    public string Message { get; set; } = "";

    public object? Details { get; set; }
}

public sealed class ConfigView
{
    public string ActiveRevision { get; set; } = "";

    public string DraftHash { get; set; } = "";

    public bool Dirty { get; set; }

    public ConfigBundle Draft { get; set; } = new();

    public ConfigBundle Published { get; set; } = new();
}

public sealed class ValidationIssue
{
    public string Severity { get; set; } = "error";

    public string Path { get; set; } = "";

    public string Message { get; set; } = "";
}

public sealed class ValidationResult
{
    public bool Valid { get; set; }

    public List<ValidationIssue> Issues { get; set; } = [];
}

public sealed class PublishRequest
{
    public string? Note { get; set; }
}

public sealed class PublishResult
{
    public string Revision { get; set; } = "";

    public DateTimeOffset PublishedAt { get; set; }

    public bool Unchanged { get; set; }

    public List<ValidationIssue> Issues { get; set; } = [];
}

public sealed class RollbackRequest
{
    public string? Revision { get; set; }
}

public sealed class RollbackResult
{
    public string Revision { get; set; } = "";

    public DateTimeOffset RolledBackAt { get; set; }
}

public sealed class RevisionInfo
{
    public string Revision { get; set; } = "";

    public DateTimeOffset CreatedAt { get; set; }

    public string Action { get; set; } = "";

    public string? Note { get; set; }
}

public sealed class RevisionList
{
    public List<RevisionInfo> Revisions { get; set; } = [];
}

public sealed class ConfigChange
{
    public string Path { get; set; } = "";

    public string Kind { get; set; } = "";

    public string Summary { get; set; } = "";
}

public sealed class DiffView
{
    public bool Dirty { get; set; }

    public List<ConfigChange> Changes { get; set; } = [];
}

public sealed class LoginRequest
{
    public string? Username { get; set; }

    public string? Password { get; set; }
}

public sealed class LoginResponse
{
    public string Token { get; set; } = "";

    public string Username { get; set; } = "";

    public string Role { get; set; } = "";

    public DateTimeOffset ExpiresAt { get; set; }
}

public sealed class UserInfo
{
    public string Username { get; set; } = "";

    public string Role { get; set; } = "";
}

public sealed class LicenseStatus
{
    public bool Enforced { get; set; }

    public string Status { get; set; } = "stub";

    public string Edition { get; set; } = "m1-dev";

    public string Message { get; set; } = "";
}

public sealed class SettingsView
{
    public string SiteId { get; set; } = "";

    public string Name { get; set; } = "";

    public string LogLevel { get; set; } = "Information";

    public bool ProgramWrite { get; set; }

    public int DefaultIntervalMs { get; set; }

    public bool ChangeOnly { get; set; }

    public string DataDirectory { get; set; } = "";

    public LicenseStatus License { get; set; } = new();

    public UserInfo CurrentUser { get; set; } = new();

    public List<UserInfo> Users { get; set; } = [];
}

public sealed class SettingsUpdate
{
    public string? SiteId { get; set; }

    public string? Name { get; set; }

    public string? LogLevel { get; set; }

    public bool ProgramWrite { get; set; }

    public int DefaultIntervalMs { get; set; } = 1000;

    public bool ChangeOnly { get; set; } = true;
}

public sealed class DeviceTestResult
{
    public string DeviceId { get; set; } = "";

    public bool Ok { get; set; }

    public string Adapter { get; set; } = "";

    public string Message { get; set; } = "";

    public int LatencyMs { get; set; }
}

public sealed class RuntimeStatus
{
    public string Name { get; set; } = "";

    public string SiteId { get; set; } = "";

    public string State { get; set; } = "running";

    public string Mode { get; set; } = "mock";

    public string ActiveRevision { get; set; } = "";

    public DateTimeOffset UtcNow { get; set; }

    public List<DeviceHealthView> Devices { get; set; } = [];

    public List<string> RecentErrors { get; set; } = [];
}

public sealed class DeviceHealthView
{
    public string Id { get; set; } = "";

    public string DisplayName { get; set; } = "";

    public bool Enabled { get; set; }

    public string Adapter { get; set; } = "";

    public string Status { get; set; } = "";

    public DateTimeOffset? LastSeen { get; set; }

    public string Message { get; set; } = "";

    public string StatusTopic { get; set; } = "";
}

public sealed class ObservationView
{
    public string DeviceId { get; set; } = "";

    public string Point { get; set; } = "";

    public string? Value { get; set; }

    public string Quality { get; set; } = "good";

    public string? Unit { get; set; }

    public string Topic { get; set; } = "";

    public DateTimeOffset Timestamp { get; set; }
}

public sealed class ObservationList
{
    public List<ObservationView> Observations { get; set; } = [];
}

public sealed class LogTail
{
    public List<string> Lines { get; set; } = [];
}
