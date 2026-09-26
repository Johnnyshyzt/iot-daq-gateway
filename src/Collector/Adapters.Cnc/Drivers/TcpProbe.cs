using System.Diagnostics;
using System.Net.Sockets;

namespace Adapters.Cnc.Drivers;

public static class TcpProbe
{
    public static async Task<TcpProbeResult> TryAsync(string host, int port, int timeoutMs, CancellationToken cancellationToken)
    {
        var watch = Stopwatch.StartNew();
        if (string.IsNullOrWhiteSpace(host) || port is < 1 or > 65535)
        {
            return new TcpProbeResult(false, (int)watch.ElapsedMilliseconds, "主机或端口无效");
        }

        try
        {
            using var client = new TcpClient();
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            linked.CancelAfter(Math.Clamp(timeoutMs, 100, 120_000));
            await client.ConnectAsync(host.Trim(), port, linked.Token).ConfigureAwait(false);
            watch.Stop();
            return new TcpProbeResult(true, (int)watch.ElapsedMilliseconds, null);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            watch.Stop();
            var reason = ex is OperationCanceledException ? "连接超时" : ex.Message;
            return new TcpProbeResult(false, (int)watch.ElapsedMilliseconds, reason);
        }
    }
}

public readonly record struct TcpProbeResult(bool Ok, int ElapsedMs, string? Error);
