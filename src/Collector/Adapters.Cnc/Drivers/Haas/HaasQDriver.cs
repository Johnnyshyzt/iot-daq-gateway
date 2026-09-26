using System.Diagnostics;
using System.Net.Sockets;
using System.Text;
using Cnc.Catalog;
using Gateway.Abstractions.Configuration;
using Gateway.Abstractions.Models;
using Microsoft.Extensions.Logging;

namespace Adapters.Cnc.Drivers;

public sealed class HaasQDriver : CatalogProtocolDriver
{
    public HaasQDriver(DeviceBinding binding, CatalogAdapter adapter, ILogger logger)
        : base(binding, adapter, logger)
    {
    }

    public override async Task<ConnectionReport> ProbeAsync(CancellationToken cancellationToken)
    {
        var tcp = await TcpProbe.TryAsync(Settings.Host, Settings.Port, Settings.TimeoutMs, cancellationToken).ConfigureAwait(false);
        if (!tcp.Ok)
        {
            return new ConnectionReport
            {
                Reachable = false,
                ReachableMs = tcp.ElapsedMs,
                Message = $"哈斯 Q 指令网络不可达：{tcp.Error}",
                Error = tcp.Error,
                SdkStatus = "none"
            };
        }

        var watch = Stopwatch.StartNew();
        try
        {
            var samples = await ReadAsync(cancellationToken).ConfigureAwait(false);
            watch.Stop();
            return new ConnectionReport
            {
                Reachable = true,
                Handshake = samples.Count > 0,
                ReachableMs = tcp.ElapsedMs,
                HandshakeMs = (int)watch.ElapsedMilliseconds,
                Message = samples.Count > 0
                    ? $"哈斯 Q 指令握手成功，读到 {samples.Count} 个目录项。"
                    : "端口已连通，但没有读到 Q 应答。请确认 Setting 143 已打开。",
                SdkStatus = "none",
                Samples = samples
            };
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            watch.Stop();
            return new ConnectionReport
            {
                Reachable = true,
                Handshake = false,
                ReachableMs = tcp.ElapsedMs,
                HandshakeMs = (int)watch.ElapsedMilliseconds,
                Message = "端口已连通，Q 指令读取失败。",
                Error = ex.Message,
                SdkStatus = "none"
            };
        }
    }

    protected override async Task<IReadOnlyList<Observation>> ReadAsync(CancellationToken cancellationToken)
    {
        using var client = new TcpClient();
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        linked.CancelAfter(Settings.TimeoutMs);
        await client.ConnectAsync(Settings.Host, Settings.Port, linked.Token).ConfigureAwait(false);
        await using var stream = client.GetStream();
        var buffer = await ReadUntilPromptAsync(stream, linked.Token).ConfigureAwait(false);
        var values = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        foreach (var code in HaasQCodec.DefaultQueries)
        {
            var query = Encoding.ASCII.GetBytes(HaasQCodec.Query(code));
            await stream.WriteAsync(query, linked.Token).ConfigureAwait(false);
            var reply = await ReadUntilPromptAsync(stream, linked.Token).ConfigureAwait(false);
            HaasQCodec.Apply(code, reply, values);
        }

        if (values.Count == 0 && buffer.Contains("Q", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("哈斯控制返回了提示符，但 Q 应答为空");
        }

        SetOnline($"Haas Q {Settings.Host}:{Settings.Port}");
        return values.Select(pair => PointValues.Make(DeviceId, pair.Key, pair.Value, ItemUnit(pair.Key))).ToList();
    }

    internal static async Task<string> ReadUntilPromptAsync(NetworkStream stream, CancellationToken cancellationToken)
    {
        var collected = new StringBuilder();
        var bytes = new byte[256];
        while (collected.ToString().IndexOf('>') < 0)
        {
            var read = await stream.ReadAsync(bytes, cancellationToken).ConfigureAwait(false);
            if (read == 0)
            {
                break;
            }

            collected.Append(Encoding.ASCII.GetString(bytes, 0, read));
            if (collected.Length > 8192)
            {
                break;
            }
        }

        var text = collected.ToString();
        var reply = HaasQCodec.TakeReply(text, out _);
        return reply.Length == 0 ? text : reply;
    }
}
