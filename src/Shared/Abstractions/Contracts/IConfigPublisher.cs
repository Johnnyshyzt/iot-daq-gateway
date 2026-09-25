using Gateway.Abstractions.Models;

namespace Gateway.Abstractions.Contracts;

/// <summary>
/// Validates a draft bundle, publishes it as a content-addressed revision,
/// and rolls the published tree back. The revision algorithm is specified
/// in <c>docs/config/README.md</c>. The Api module's ConfigStore publishes files in M1
/// and does not implement this interface yet. The Host process reads the published tree directly.
/// </summary>
public interface IConfigPublisher
{
    /// <summary>
    /// Checks the current draft against JSON Schema and the cross-file rules.
    /// Invalid drafts return <see cref="ConfigValidationResult.Valid"/> false
    /// instead of throwing.
    /// </summary>
    Task<ConfigValidationResult> ValidateDraftAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Publishes a valid draft. Returns the lowercase sha256 revision.
    /// Identical content yields the same revision.
    /// </summary>
    Task<string> PublishAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Makes an existing revision active and resets the draft to that snapshot.
    /// </summary>
    /// <exception cref="ArgumentException">Thrown when <paramref name="revision"/> is empty.</exception>
    /// <exception cref="InvalidOperationException">Thrown when the snapshot does not exist.</exception>
    Task<string> RollbackAsync(string revision, CancellationToken cancellationToken);
}
