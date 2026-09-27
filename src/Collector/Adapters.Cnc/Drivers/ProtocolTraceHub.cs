namespace Adapters.Cnc.Drivers;

public interface IProtocolTraceSink
{
    void Write(string deviceId, string direction, ReadOnlySpan<byte> raw, string? decoded);
}

/// <summary>
/// Optional raw-frame sink. Drivers that already build a request call this. A null sink is a no-op.
/// </summary>
public static class ProtocolTraceHub
{
    private static readonly AsyncLocal<string?> CurrentDevice = new();

    public static IProtocolTraceSink? Sink { get; set; }

    public static IDisposable Begin(string deviceId) => new Scope(deviceId);

    public static void Write(string deviceId, string direction, ReadOnlySpan<byte> raw, string? decoded)
    {
        if (string.IsNullOrWhiteSpace(deviceId))
        {
            return;
        }

        Sink?.Write(deviceId, direction, raw, decoded);
    }

    public static void WriteCurrent(string direction, ReadOnlySpan<byte> raw, string? decoded)
    {
        var deviceId = CurrentDevice.Value;
        if (string.IsNullOrWhiteSpace(deviceId))
        {
            return;
        }

        Write(deviceId, direction, raw, decoded);
    }

    private sealed class Scope : IDisposable
    {
        private readonly string? _previous;

        public Scope(string deviceId)
        {
            _previous = CurrentDevice.Value;
            CurrentDevice.Value = deviceId;
        }

        public void Dispose() => CurrentDevice.Value = _previous;
    }
}
