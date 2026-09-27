using Gateway.Abstractions.Models;

namespace Gateway.Abstractions.Contracts;

/// <summary>
/// Northbound publisher. MQTT is the original sink; HTTP push and OPC UA are separate.
/// </summary>
public interface INorthboundSink : IAsyncDisposable
{
    Task StartAsync(CancellationToken cancellationToken);

    Task PublishObservationAsync(Observation observation, CancellationToken cancellationToken);

    Task PublishStatusAsync(DeviceHealth health, CancellationToken cancellationToken);

    /// <summary>
    /// Publishes an already-serialized contract document. The sink spools it when the broker is down.
    /// </summary>
    Task PublishDocumentAsync(string topic, string json, CancellationToken cancellationToken);
}
