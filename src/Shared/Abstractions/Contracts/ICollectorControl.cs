namespace Gateway.Abstractions.Contracts;

/// <summary>
/// In-process acquisition session. The management API calls this after publish
/// and when the runtime page asks for live status.
/// </summary>
public interface ICollectorControl
{
    Task<bool> TryReloadAsync(CancellationToken cancellationToken);

    object StatusDocument();

    object ObservationsDocument(string? deviceId, int limit);

    IReadOnlyList<string> TailLogs(int lines);
}
