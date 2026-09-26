using System.Diagnostics;
using Cnc.Catalog;
using Gateway.Abstractions.Configuration;
using Gateway.Abstractions.Models;
using Microsoft.Extensions.Logging;

namespace Adapters.Cnc.Drivers;

public sealed class FtpShareDriver : CatalogProtocolDriver
{
    public FtpShareDriver(DeviceBinding binding, CatalogAdapter adapter, ILogger logger)
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
                Message = $"FTP 网络不可达：{tcp.Error}",
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
                Handshake = true,
                ReachableMs = tcp.ElapsedMs,
                HandshakeMs = (int)watch.ElapsedMilliseconds,
                Message = $"FTP 登录并读取状态文件成功，映射 {samples.Count} 个目录项。",
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
                Message = "FTP 端口已连通，登录或读取状态文件失败。",
                Error = ex.Message,
                SdkStatus = "none"
            };
        }
    }

    protected override async Task<IReadOnlyList<Observation>> ReadAsync(CancellationToken cancellationToken)
    {
        var body = await FtpStatus.DownloadAsync(
            Settings.Host,
            Settings.Port,
            Settings.Path,
            Settings.Username,
            Settings.Password,
            Settings.TimeoutMs,
            cancellationToken).ConfigureAwait(false);
        var parsed = FtpStatus.Parse(body);
        var rows = new List<Observation>();
        foreach (var pair in parsed)
        {
            if (CncCatalog.Current.FindItem(pair.Key) is null || !Settings.Wants(pair.Key))
            {
                continue;
            }

            rows.Add(PointValues.Make(DeviceId, pair.Key, pair.Value, ItemUnit(pair.Key)));
        }

        SetOnline($"FTP {Settings.Host}:{Settings.Port} {Settings.Path}");
        return rows;
    }
}
