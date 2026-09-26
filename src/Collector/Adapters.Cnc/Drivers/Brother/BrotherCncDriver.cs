using Cnc.Catalog;
using Gateway.Abstractions.Configuration;
using Gateway.Abstractions.Models;
using Microsoft.Extensions.Logging;

namespace Adapters.Cnc.Drivers;

/// <summary>
/// Brother CNC-C00 is not implemented. Public sources do not include a frame format
/// that can be checked against a specification, so this driver never invents a handshake.
/// </summary>
public sealed class BrotherCncDriver : CatalogProtocolDriver
{
    public const string Reason =
        "兄弟 CNC-C00 没有可核对的公开帧格式，本驱动不发起专有握手。请改用 brother.mtconnect 或 brother.opcua。";

    public BrotherCncDriver(DeviceBinding binding, CatalogAdapter adapter, ILogger logger)
        : base(binding, adapter, logger)
    {
    }

    public override async Task<ConnectionReport> ProbeAsync(CancellationToken cancellationToken)
    {
        var tcp = await TcpProbe.TryAsync(Settings.Host, Settings.Port, Settings.TimeoutMs, cancellationToken).ConfigureAwait(false);
        return new ConnectionReport
        {
            Reachable = tcp.Ok,
            Handshake = false,
            ReachableMs = tcp.ElapsedMs,
            SdkStatus = "none",
            Message = Reason,
            Error = tcp.Ok ? null : tcp.Error
        };
    }

    protected override Task OnConnectAsync(CancellationToken cancellationToken)
    {
        SetOffline(Reason);
        return Task.CompletedTask;
    }

    protected override Task<IReadOnlyList<Observation>> ReadAsync(CancellationToken cancellationToken)
    {
        SetOffline(Reason);
        return Task.FromResult<IReadOnlyList<Observation>>([]);
    }
}
