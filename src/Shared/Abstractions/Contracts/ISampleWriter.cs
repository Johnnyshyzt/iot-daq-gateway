using Gateway.Abstractions.Models;

namespace Gateway.Abstractions.Contracts;

/// <summary>
/// Persists collector samples. MQTT publish stays independent of this write.
/// </summary>
public interface ISampleWriter
{
    void Write(IReadOnlyList<Observation> observations);

    /// <summary>
    /// Records a connection-level state such as offline. The default does nothing
    /// so existing writers keep compiling.
    /// </summary>
    void NoteStatus(string deviceId, string status, DateTimeOffset timestamp)
    {
    }
}
