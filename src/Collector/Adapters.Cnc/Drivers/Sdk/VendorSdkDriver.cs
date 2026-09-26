using System.Diagnostics;
using Cnc.Catalog;
using Gateway.Abstractions.Configuration;
using Gateway.Abstractions.Models;
using Microsoft.Extensions.Logging;

namespace Adapters.Cnc.Drivers;

public sealed class VendorSdkDriver : CatalogProtocolDriver
{
    private readonly string _vendor;

    public VendorSdkDriver(DeviceBinding binding, CatalogAdapter adapter, ILogger logger)
        : base(binding, adapter, logger)
    {
        _vendor = VendorOf(adapter);
    }

    public override async Task<ConnectionReport> ProbeAsync(CancellationToken cancellationToken)
    {
        var sdk = SdkLocator.Inspect(_vendor, Settings.SdkPath);
        var tcp = await TcpProbe.TryAsync(Settings.Host, Settings.Port, Settings.TimeoutMs, cancellationToken).ConfigureAwait(false);
        var watch = Stopwatch.StartNew();
        watch.Stop();
        if (!sdk.Installed)
        {
            return new ConnectionReport
            {
                Reachable = tcp.Ok,
                Handshake = false,
                ReachableMs = tcp.ElapsedMs,
                HandshakeMs = (int)watch.ElapsedMilliseconds,
                SdkStatus = "missing",
                Message = sdk.Message + (tcp.Ok ? " 网络已连通，但没有 SDK 不能做协议握手。" : " 网络也不可达。"),
                Error = tcp.Ok ? null : tcp.Error
            };
        }

        return new ConnectionReport
        {
            Reachable = tcp.Ok,
            Handshake = false,
            ReachableMs = tcp.ElapsedMs,
            HandshakeMs = 0,
            SdkStatus = "present",
            Message = sdk.Message + " 这些文件已找到，但没有可核对的公开函数签名，不会调用未知入口。详见 docs/drivers/" + _vendor + ".md。",
            Error = tcp.Ok ? null : tcp.Error
        };
    }

    protected override Task OnConnectAsync(CancellationToken cancellationToken)
    {
        var sdk = SdkLocator.Inspect(_vendor, Settings.SdkPath);
        if (!sdk.Installed)
        {
            SetOffline(sdk.Message);
            return Task.CompletedTask;
        }

        SetDegraded(sdk.Message + " 没有可核对的公开函数签名，采集不会调用未知入口。");
        return Task.CompletedTask;
    }

    protected override Task<IReadOnlyList<Observation>> ReadAsync(CancellationToken cancellationToken)
    {
        var sdk = SdkLocator.Inspect(_vendor, Settings.SdkPath);
        if (!sdk.Installed)
        {
            SetOffline(sdk.Message);
        }
        else
        {
            SetDegraded(sdk.Message + " 未调用未知入口。");
        }

        return Task.FromResult<IReadOnlyList<Observation>>([]);
    }

    public static string VendorOf(CatalogAdapter adapter)
    {
        var protocol = adapter.Protocol;
        if (protocol is "ezsocket" or "mitsubishi")
        {
            return "mitsubishi";
        }

        if (!string.IsNullOrWhiteSpace(adapter.BrandId) && protocol is not ("opcua" or "mtconnect" or "modbus" or "ftp" or "lsv2" or "haas-q" or "brother" or "focas" or "sim" or "fake"))
        {
            return adapter.BrandId!;
        }

        return string.IsNullOrWhiteSpace(adapter.BrandId) ? protocol : adapter.BrandId;
    }
}
