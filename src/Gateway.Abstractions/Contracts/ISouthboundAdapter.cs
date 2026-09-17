using Gateway.Abstractions.Models;

namespace Gateway.Abstractions.Contracts;

/// <summary>
/// Device-family adapter. Implementations must not assume an industrial PC —
/// they run in a normal process or container.
/// </summary>
public interface ISouthboundAdapter : IAsyncDisposable
{
    string AdapterKind { get; }

    string DeviceId { get; }

    Task ConnectAsync(CancellationToken cancellationToken);

    Task<IReadOnlyList<Observation>> CollectAsync(CancellationToken cancellationToken);

    Task<DeviceHealth> GetHealthAsync(CancellationToken cancellationToken);
}
