using Gateway.Abstractions.Models;

namespace Gateway.Abstractions.Contracts;

/// <summary>
/// Persists collector samples. MQTT publish stays independent of this write.
/// </summary>
public interface ISampleWriter
{
    void Write(IReadOnlyList<Observation> observations);
}
