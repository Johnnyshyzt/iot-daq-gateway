using System.Diagnostics;
using System.Net.Sockets;
using Cnc.Catalog;
using Gateway.Abstractions.Configuration;
using Gateway.Abstractions.Models;
using Microsoft.Extensions.Logging;

namespace Adapters.Cnc.Drivers;

public sealed class ModbusDriver : CatalogProtocolDriver
{
    public ModbusDriver(DeviceBinding binding, CatalogAdapter adapter, ILogger logger)
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
                Message = $"Modbus 网络不可达：{tcp.Error}",
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
                Message = $"Modbus 功能码 03 握手成功，读到 {samples.Count} 个目录项。默认寄存器表需要现场按 PLC 核对。",
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
                Message = "端口已连通，Modbus 读寄存器失败。",
                Error = ex.Message,
                SdkStatus = "none"
            };
        }
    }

    protected override async Task<IReadOnlyList<Observation>> ReadAsync(CancellationToken cancellationToken)
    {
        var brand = string.IsNullOrWhiteSpace(Settings.BrandId) ? Adapter.BrandId : Settings.BrandId;
        var items = Wanted(brand);
        using var client = new TcpClient();
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        linked.CancelAfter(Settings.TimeoutMs);
        await client.ConnectAsync(Settings.Host, Settings.Port, linked.Token).ConfigureAwait(false);
        await using var stream = client.GetStream();
        var holding = await ReadBlockAsync(stream, input: false, 0, 16, linked.Token).ConfigureAwait(false);
        ushort[]? inputs = null;
        var rows = new List<Observation>();
        var axes = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in items)
        {
            var address = Settings.AddressFor(item);
            var input = false;
            ushort register;
            if (!ModbusCodec.TryAddress(address, out input, out register) && !ModbusCodec.TryDefault(item, out register))
            {
                continue;
            }

            int raw;
            if (!input && register < holding.Length)
            {
                raw = holding[register];
            }
            else if (input)
            {
                inputs ??= await ReadBlockAsync(stream, input: true, 0, 16, linked.Token).ConfigureAwait(false);
                if (register >= inputs.Length)
                {
                    continue;
                }

                raw = inputs[register];
            }
            else
            {
                var block = await ReadBlockAsync(stream, input, register, 1, linked.Token).ConfigureAwait(false);
                raw = block[0];
            }

            if (item.EndsWith("X", StringComparison.OrdinalIgnoreCase) && item.Contains("osition", StringComparison.OrdinalIgnoreCase))
            {
                axes["X"] = raw;
            }
            else if (item.EndsWith("Y", StringComparison.OrdinalIgnoreCase) && item.Contains("osition", StringComparison.OrdinalIgnoreCase))
            {
                axes["Y"] = raw;
            }
            else if (item.EndsWith("Z", StringComparison.OrdinalIgnoreCase) && item.Contains("osition", StringComparison.OrdinalIgnoreCase))
            {
                axes["Z"] = raw;
            }

            rows.Add(PointValues.Make(DeviceId, item, ModbusCodec.ValueFor(item, (ushort)raw), ItemUnit(item)));
        }

        if (axes.Count > 0 && rows.All(row => !row.Point.Equals("machinePosition", StringComparison.OrdinalIgnoreCase)))
        {
            var text = string.Join(' ', new[] { "X", "Y", "Z" }.Where(axes.ContainsKey).Select(axis => axis + axes[axis].ToString(System.Globalization.CultureInfo.InvariantCulture)));
            rows.Add(PointValues.Make(DeviceId, "machinePosition", text, ItemUnit("machinePosition")));
        }

        SetOnline($"Modbus {Settings.Host}:{Settings.Port} unit {Settings.UnitId}");
        return rows;
    }

    private IEnumerable<string> Wanted(string? brandId)
    {
        var brand = CncCatalog.Current.FindBrand(brandId);
        IEnumerable<string> items = brand?.ItemIds ?? ["state", "alarm", "program", "partCount", "spindleSpeed", "feedRate", "toolNumber"];
        return items.Where(Settings.Wants);
    }

    private async Task<ushort[]> ReadBlockAsync(NetworkStream stream, bool input, ushort address, ushort count, CancellationToken cancellationToken)
    {
        var request = input
            ? ModbusCodec.ReadInput(1, (byte)Settings.UnitId, address, count)
            : ModbusCodec.ReadHolding(1, (byte)Settings.UnitId, address, count);
        await stream.WriteAsync(request, cancellationToken).ConfigureAwait(false);
        var frame = await ReadResponseAsync(stream, cancellationToken).ConfigureAwait(false);
        if (!ModbusCodec.TryRegisters(frame, out var registers))
        {
            throw new InvalidOperationException("Modbus 应答不是功能码 03/04 的寄存器数据");
        }

        return registers;
    }

    private static async Task<byte[]> ReadResponseAsync(NetworkStream stream, CancellationToken cancellationToken)
    {
        var header = new byte[8];
        await ReadExactAsync(stream, header, cancellationToken).ConfigureAwait(false);
        var rest = new List<byte>(header);
        if (header[7] is 0x83 or 0x84)
        {
            var error = new byte[1];
            await ReadExactAsync(stream, error, cancellationToken).ConfigureAwait(false);
            throw new InvalidOperationException($"Modbus 异常码 {error[0]}");
        }

        var count = new byte[1];
        await ReadExactAsync(stream, count, cancellationToken).ConfigureAwait(false);
        rest.Add(count[0]);
        var data = new byte[count[0]];
        await ReadExactAsync(stream, data, cancellationToken).ConfigureAwait(false);
        rest.AddRange(data);
        return rest.ToArray();
    }

    private static async Task ReadExactAsync(Stream stream, byte[] buffer, CancellationToken cancellationToken)
    {
        var offset = 0;
        while (offset < buffer.Length)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(offset), cancellationToken).ConfigureAwait(false);
            if (read == 0)
            {
                throw new IOException("Modbus 连接已关闭");
            }

            offset += read;
        }
    }
}
