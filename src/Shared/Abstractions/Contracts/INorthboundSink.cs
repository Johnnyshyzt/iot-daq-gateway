using Gateway.Abstractions.Models;

namespace Gateway.Abstractions.Contracts;

/// <summary>
/// Northbound publisher. V1 is MQTT JSON only.
/// </summary>
public interface INorthboundSink : IAsyncDisposable
{
    Task StartAsync(CancellationToken cancellationToken);

    Task PublishObservationAsync(Observation observation, CancellationToken cancellationToken);

    Task PublishStatusAsync(DeviceHealth health, CancellationToken cancellationToken);
}
