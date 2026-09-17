using Gateway.Abstractions.Configuration;
using Gateway.Abstractions.Contracts;
using Gateway.Abstractions.Models;

namespace Gateway.Host.Programs;

/// <summary>
/// V1 program surface: read-only and disabled unless the feature flag is set.
/// Transfer is not implemented.
/// </summary>
internal sealed class FeatureGatedProgramService : IProgramService
{
    private readonly GatewayConfiguration _config;

    public FeatureGatedProgramService(GatewayConfiguration config)
    {
        _config = config;
    }

    public bool IsEnabled => _config.ProgramTransfer.Enabled;

    public Task<IReadOnlyList<CncProgramInfo>> ListAsync(string deviceId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        EnsureEnabled();
        _ = deviceId;
        IReadOnlyList<CncProgramInfo> empty = [];
        return Task.FromResult(empty);
    }

    public Task<string?> ReadAsync(string deviceId, string programId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        EnsureEnabled();
        _ = deviceId;
        _ = programId;
        return Task.FromResult<string?>(null);
    }

    private void EnsureEnabled()
    {
        if (!IsEnabled)
        {
            throw new InvalidOperationException(
                "IProgramService is disabled (programTransfer.enabled defaults to false in V1).");
        }
    }
}
