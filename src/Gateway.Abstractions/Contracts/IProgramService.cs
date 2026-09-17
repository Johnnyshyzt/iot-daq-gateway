using Gateway.Abstractions.Models;

namespace Gateway.Abstractions.Contracts;

/// <summary>
/// Reserved CNC program surface. V1 is read-only and gated by a feature flag
/// that defaults to <c>false</c>. Transfer/write is intentionally omitted.
/// </summary>
public interface IProgramService
{
    bool IsEnabled { get; }

    Task<IReadOnlyList<CncProgramInfo>> ListAsync(string deviceId, CancellationToken cancellationToken);

    Task<string?> ReadAsync(string deviceId, string programId, CancellationToken cancellationToken);
}
