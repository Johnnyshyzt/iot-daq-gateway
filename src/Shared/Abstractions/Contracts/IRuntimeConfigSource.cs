using Gateway.Abstractions.Configuration;

namespace Gateway.Abstractions.Contracts;

/// <summary>
/// Published configuration the collector runs. The host implementation reads the database.
/// </summary>
public interface IRuntimeConfigSource
{
    RuntimeConfigSnapshot Load();
}

public sealed class RuntimeConfigSnapshot
{
    public GatewayConfiguration Configuration { get; init; } = new();

    public string? Revision { get; init; }

    public string DisplayName { get; init; } = "";

    public string Source { get; init; } = "database";
}
