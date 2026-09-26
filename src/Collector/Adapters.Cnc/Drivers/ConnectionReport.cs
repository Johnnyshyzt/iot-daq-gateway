using Gateway.Abstractions.Models;

namespace Adapters.Cnc.Drivers;

public sealed class ConnectionReport
{
    public bool Reachable { get; init; }

    public bool Handshake { get; init; }

    public int ReachableMs { get; init; }

    public int HandshakeMs { get; init; }

    public string Message { get; init; } = "";

    public string? Error { get; init; }

    public string? SdkStatus { get; init; }

    public IReadOnlyList<Observation> Samples { get; init; } = [];

    public bool Ok => Handshake;
}

public interface IConnectionProbe
{
    Task<ConnectionReport> ProbeAsync(CancellationToken cancellationToken);
}
