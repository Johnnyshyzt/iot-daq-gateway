using System.Diagnostics;
using System.Globalization;
using Cnc.Catalog;
using Gateway.Abstractions.Configuration;
using Gateway.Abstractions.Models;
using Microsoft.Extensions.Logging;

namespace Adapters.Cnc.Drivers;

public sealed class Lsv2Driver : CatalogProtocolDriver
{
    private Lsv2Client? _client;

    public Lsv2Driver(DeviceBinding binding, CatalogAdapter adapter, ILogger logger)
        : base(binding, adapter, logger)
    {
    }

    public override async Task<ConnectionReport> ProbeAsync(CancellationToken cancellationToken)
    {
        var tcp = await TcpProbe.TryAsync(Settings.Host, Settings.Port, Settings.TimeoutMs, cancellationToken).ConfigureAwait(false);
        if (!tcp.Ok)
        {
            return Fail(false, tcp, 0, $"LSV2 网络不可达：{tcp.Error}", tcp.Error);
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
                Message = $"LSV2 登录成功，读到 {samples.Count} 个目录项。主轴转速和进给速度没有对应的公开报文。",
                SdkStatus = "none",
                Samples = samples
            };
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            watch.Stop();
            return Fail(true, tcp, (int)watch.ElapsedMilliseconds, "LSV2 已连通，登录或读数失败。", ex.Message);
        }
        finally
        {
            await DropAsync().ConfigureAwait(false);
        }
    }

    protected override async Task<IReadOnlyList<Observation>> ReadAsync(CancellationToken cancellationToken)
    {
        var client = await EnsureAsync(cancellationToken).ConfigureAwait(false);
        var rows = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        var execution = await InfoAsync(client, Lsv2Codec.ExecState, cancellationToken).ConfigureAwait(false);
        var programState = await InfoAsync(client, Lsv2Codec.ProgramState, cancellationToken).ConfigureAwait(false);
        if (execution is not null)
        {
            var exec = Lsv2Codec.UInt16Value(execution);
            var pgm = programState is null ? (ushort)8 : Lsv2Codec.UInt16Value(programState);
            rows["workMode"] = Lsv2Codec.MapExecution(exec);
            rows["state"] = Lsv2Codec.MapMachineState(exec, pgm);
            rows["isRunning"] = rows["state"] as string == "RUNNING";
        }

        var selected = await InfoAsync(client, Lsv2Codec.SelectedProgram, cancellationToken).ConfigureAwait(false);
        if (selected is not null && Lsv2Codec.TryProgram(selected, out var line, out var main, out var current))
        {
            rows["programLine"] = line;
            if (!string.IsNullOrEmpty(main))
            {
                rows["programMain"] = main;
            }

            rows["program"] = string.IsNullOrEmpty(current) ? main : current;
        }

        var overrides = await InfoAsync(client, Lsv2Codec.Override, cancellationToken).ConfigureAwait(false);
        if (overrides is not null && Lsv2Codec.TryOverride(overrides, out var feed, out var spindle, out var rapid))
        {
            rows["feedOverride"] = feed;
            rows["spindleOverride"] = spindle;
            rows["rapidOverride"] = rapid;
        }

        var alarm = await InfoAsync(client, Lsv2Codec.FirstError, cancellationToken).ConfigureAwait(false);
        if (alarm is not null && Lsv2Codec.TryError(alarm, out var number, out var text))
        {
            rows["alarmNumber"] = number.ToString(CultureInfo.InvariantCulture);
            rows["alarm"] = string.IsNullOrEmpty(text) ? number.ToString(CultureInfo.InvariantCulture) : text;
        }
        else
        {
            rows["alarm"] = "0";
        }

        var tool = await InfoAsync(client, 51, cancellationToken).ConfigureAwait(false);
        if (tool is not null && Lsv2Codec.TryTool(tool, out var toolNumber))
        {
            rows["toolNumber"] = toolNumber.ToString(CultureInfo.InvariantCulture);
        }

        var axes = await InfoAsync(client, Lsv2Codec.AxisLocation, cancellationToken).ConfigureAwait(false);
        if (axes is not null && Lsv2Codec.TryAxes(axes, out var positions))
        {
            rows["machinePosition"] = string.Join(' ', positions.Select(pair =>
                pair.Key + pair.Value.ToString("0.###", CultureInfo.InvariantCulture)));
            foreach (var pair in positions)
            {
                rows["machinePosition" + pair.Key] = pair.Value;
            }
        }

        var version = await VersionAsync(client, Lsv2Codec.VersionNc, cancellationToken).ConfigureAwait(false)
            ?? await VersionAsync(client, Lsv2Codec.VersionControl, cancellationToken).ConfigureAwait(false);
        if (!string.IsNullOrWhiteSpace(version))
        {
            rows["softwareVersion"] = version;
        }

        SetOnline($"LSV2 {Settings.Host}:{Settings.Port}");
        return rows.Select(pair => PointValues.Make(DeviceId, pair.Key, pair.Value, ItemUnit(pair.Key))).ToList();
    }

    protected override Task OnDisconnectAsync() => DropAsync();

    private async Task<Lsv2Client> EnsureAsync(CancellationToken cancellationToken)
    {
        if (_client is not null)
        {
            return _client;
        }

        var client = new Lsv2Client();
        await client.ConnectAsync(Settings.Host, Settings.Port, Settings.TimeoutMs, cancellationToken).ConfigureAwait(false);
        var inspect = await client.ExchangeAsync(Lsv2Codec.Login, Lsv2Codec.LoginPayload("INSPECT", null), cancellationToken).ConfigureAwait(false);
        if (!inspect.Command.Equals(Lsv2Codec.Ok, StringComparison.Ordinal))
        {
            await client.DisposeAsync().ConfigureAwait(false);
            throw new InvalidOperationException($"LSV2 INSPECT 登录被拒绝（{inspect.Command}）");
        }

        var dnc = await client.ExchangeAsync(Lsv2Codec.Login, Lsv2Codec.LoginPayload("DNC", Settings.Password), cancellationToken).ConfigureAwait(false);
        if (!dnc.Command.Equals(Lsv2Codec.Ok, StringComparison.Ordinal))
        {
            Logger.LogInformation("LSV2 DNC login declined for {DeviceId} ({Reply})", DeviceId, dnc.Command);
        }

        _client = client;
        return client;
    }

    private async Task<byte[]?> InfoAsync(Lsv2Client client, ushort parameter, CancellationToken cancellationToken)
    {
        var frame = await client.ExchangeAsync(Lsv2Codec.ReadInfo, Lsv2Codec.UInt16(parameter), cancellationToken).ConfigureAwait(false);
        return frame.Command.Equals(Lsv2Codec.Info, StringComparison.Ordinal) ? frame.Payload : null;
    }

    private async Task<string?> VersionAsync(Lsv2Client client, byte parameter, CancellationToken cancellationToken)
    {
        var frame = await client.ExchangeAsync(Lsv2Codec.ReadVersion, Lsv2Codec.Byte(parameter), cancellationToken).ConfigureAwait(false);
        return frame.Command.Equals(Lsv2Codec.Version, StringComparison.Ordinal) ? Lsv2Codec.CString(frame.Payload) : null;
    }

    private async Task DropAsync()
    {
        if (_client is null)
        {
            return;
        }

        var client = _client;
        _client = null;
        await client.DisposeAsync().ConfigureAwait(false);
    }

    private static ConnectionReport Fail(bool reachable, TcpProbeResult tcp, int handshakeMs, string message, string? error) =>
        new()
        {
            Reachable = reachable,
            Handshake = false,
            ReachableMs = tcp.ElapsedMs,
            HandshakeMs = handshakeMs,
            Message = message,
            Error = error,
            SdkStatus = "none"
        };
}
