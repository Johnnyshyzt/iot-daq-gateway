using Gateway.Abstractions.Configuration;

namespace Gateway.Host.Configuration;

internal sealed record LoadedGateway(
    GatewayConfiguration Configuration,
    string SourcePath,
    string? Revision,
    string DisplayName,
    bool IsBundle);
