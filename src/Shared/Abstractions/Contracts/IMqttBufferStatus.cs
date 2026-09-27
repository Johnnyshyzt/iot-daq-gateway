namespace Gateway.Abstractions.Contracts;

/// <summary>
/// Live depth of the MQTT store-and-forward spool. Safe to read when acquisition is off.
/// </summary>
public interface IMqttBufferStatus
{
    int Depth { get; }

    long Dropped { get; }

    bool Connected { get; }
}

public sealed class MqttBufferStatus : IMqttBufferStatus
{
    private int _depth;
    private long _dropped;
    private int _connected;

    public int Depth => Volatile.Read(ref _depth);

    public long Dropped => Interlocked.Read(ref _dropped);

    public bool Connected => Volatile.Read(ref _connected) == 1;

    public void Publish(int depth, long dropped, bool connected)
    {
        Volatile.Write(ref _depth, depth);
        Interlocked.Exchange(ref _dropped, dropped);
        Volatile.Write(ref _connected, connected ? 1 : 0);
    }
}
