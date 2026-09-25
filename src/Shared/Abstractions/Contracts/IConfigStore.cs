using Gateway.Abstractions.Models;

namespace Gateway.Abstractions.Contracts;

/// <summary>
/// File-backed draft configuration. YAML files are the source of truth;
/// a database must not become the only copy. Each document's fields are
/// defined by the JSON Schema files under <c>schemas/</c>.
/// The Api module's ConfigStore is the M1 file store and does not implement this interface yet.
/// Collector reads published YAML directly and does not register an implementation.
/// </summary>
public interface IConfigStore
{
    /// <summary>Published revision (lowercase sha256 hex), or null when nothing has been published.</summary>
    Task<string?> GetPublishedRevisionAsync(CancellationToken cancellationToken);

    Task<IReadOnlyList<ConfigDocument>> ListDraftAsync(CancellationToken cancellationToken);

    Task<ConfigDocument?> ReadDraftAsync(string relativePath, CancellationToken cancellationToken);

    /// <summary>
    /// Writes one draft document. <paramref name="document"/>.<see cref="ConfigDocument.RelativePath"/>
    /// uses forward slashes and must be <c>gateway.yaml</c>, <c>devices/{id}.yaml</c>,
    /// <c>points/{deviceId}.yaml</c>, or <c>sinks/mqtt.yaml</c>.
    /// </summary>
    Task WriteDraftAsync(ConfigDocument document, CancellationToken cancellationToken);

    Task DeleteDraftAsync(string relativePath, CancellationToken cancellationToken);
}
