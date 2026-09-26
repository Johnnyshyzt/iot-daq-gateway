using System.Net.Sockets;

namespace Adapters.Cnc.Drivers;

public sealed class Lsv2Client : IAsyncDisposable
{
    private readonly TcpClient _client = new();
    private NetworkStream? _stream;

    public async Task ConnectAsync(string host, int port, int timeoutMs, CancellationToken cancellationToken)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        linked.CancelAfter(timeoutMs);
        await _client.ConnectAsync(host, port, linked.Token).ConfigureAwait(false);
        _stream = _client.GetStream();
        _stream.ReadTimeout = timeoutMs;
        _stream.WriteTimeout = timeoutMs;
    }

    public async Task<Lsv2Frame> ExchangeAsync(string command, byte[] payload, CancellationToken cancellationToken)
    {
        var stream = _stream ?? throw new InvalidOperationException("LSV2 尚未连接");
        var telegram = Lsv2Codec.Encode(command, payload);
        await stream.WriteAsync(telegram, cancellationToken).ConfigureAwait(false);
        return await ReadFrameAsync(stream, cancellationToken).ConfigureAwait(false);
    }

    public static async Task<Lsv2Frame> ReadFrameAsync(Stream stream, CancellationToken cancellationToken)
    {
        var header = new byte[8];
        await ReadExactAsync(stream, header, cancellationToken).ConfigureAwait(false);
        if (!Lsv2Codec.TryReadFrame(header, out var partial, out _) && header.Length == 8)
        {
            var length = System.Buffers.Binary.BinaryPrimitives.ReadUInt32BigEndian(header);
            var payload = new byte[length];
            if (length > 0)
            {
                await ReadExactAsync(stream, payload, cancellationToken).ConfigureAwait(false);
            }

            var command = System.Text.Encoding.ASCII.GetString(header, 4, 4);
            return new Lsv2Frame(command, payload);
        }

        return partial;
    }

    public async ValueTask DisposeAsync()
    {
        if (_stream is not null)
        {
            await _stream.DisposeAsync().ConfigureAwait(false);
        }

        _client.Dispose();
    }

    private static async Task ReadExactAsync(Stream stream, byte[] buffer, CancellationToken cancellationToken)
    {
        var offset = 0;
        while (offset < buffer.Length)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(offset), cancellationToken).ConfigureAwait(false);
            if (read == 0)
            {
                throw new IOException("LSV2 连接已关闭");
            }

            offset += read;
        }
    }
}
