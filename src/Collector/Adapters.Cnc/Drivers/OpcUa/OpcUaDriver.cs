using System.Diagnostics;
using System.Globalization;
using Cnc.Catalog;
using Gateway.Abstractions.Configuration;
using Gateway.Abstractions.Models;
using Microsoft.Extensions.Logging;

namespace Adapters.Cnc.Drivers;

public sealed class OpcUaDriver : CatalogProtocolDriver
{
    private readonly Func<IOpcUaSession> _sessions;
    private IOpcUaSession? _session;
    private IReadOnlyDictionary<string, string> _browse = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    public OpcUaDriver(DeviceBinding binding, CatalogAdapter adapter, ILogger logger)
        : this(binding, adapter, logger, static () => new OpcUaNetSession())
    {
    }

    internal OpcUaDriver(DeviceBinding binding, CatalogAdapter adapter, ILogger logger, Func<IOpcUaSession> sessions)
        : base(binding, adapter, logger)
    {
        _sessions = sessions;
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
                Message = $"OPC UA 网络不可达：{tcp.Error}",
                Error = tcp.Error,
                SdkStatus = "none"
            };
        }

        var watch = Stopwatch.StartNew();
        try
        {
            var samples = await ReadAsync(cancellationToken).ConfigureAwait(false);
            watch.Stop();
            await DropAsync().ConfigureAwait(false);
            return new ConnectionReport
            {
                Reachable = true,
                Handshake = true,
                ReachableMs = tcp.ElapsedMs,
                HandshakeMs = (int)watch.ElapsedMilliseconds,
                Message = samples.Count > 0
                    ? $"OPC UA 会话已建立，读到 {samples.Count} 个目录项。"
                    : "OPC UA 会话已建立，但没有匹配到目录项。可在点位地址填写 ns=… 节点。",
                SdkStatus = "none",
                Samples = samples
            };
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            watch.Stop();
            await DropAsync().ConfigureAwait(false);
            return new ConnectionReport
            {
                Reachable = true,
                Handshake = false,
                ReachableMs = tcp.ElapsedMs,
                HandshakeMs = (int)watch.ElapsedMilliseconds,
                Message = "端口已连通，OPC UA 会话失败。请确认控制器已打开 OPC UA 选项，且允许无安全策略或所填账号。",
                Error = ex.Message,
                SdkStatus = "none"
            };
        }
    }

    protected override Task OnDisconnectAsync() => DropAsync();

    protected override async Task<IReadOnlyList<Observation>> ReadAsync(CancellationToken cancellationToken)
    {
        var session = await EnsureAsync(cancellationToken).ConfigureAwait(false);
        var brand = string.IsNullOrWhiteSpace(Settings.BrandId) ? Adapter.BrandId : Settings.BrandId;
        var siemens = OpcUaMaps.UsesSiemensMap(Adapter.Id, brand);
        var index = session.ResolveNamespace(Settings.NamespaceUri, siemens ? OpcUaMaps.SiemensNamespaceHint : Settings.NamespaceUri);
        var nodes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in Items(brand))
        {
            if (TryNode(item, siemens, index, out var nodeId))
            {
                nodes[item] = nodeId;
            }
        }

        if (siemens)
        {
            foreach (var axis in OpcUaMaps.SiemensAxes)
            {
                nodes[axis.ItemId] = OpcUaMaps.Node(index, axis.Path);
            }
        }

        if (nodes.Count == 0)
        {
            SetDegraded("OPC UA 已连接，没有可读取的节点。请把 NodeId 写到点位地址。");
            return [];
        }

        var readings = await session.ReadAsync(nodes.Values.Distinct(StringComparer.OrdinalIgnoreCase).ToList(), cancellationToken).ConfigureAwait(false);
        var rows = new List<Observation>();
        object? status = null;
        object? alarm = null;
        var axes = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        foreach (var pair in nodes)
        {
            if (!readings.TryGetValue(pair.Value, out var reading) || reading.Error is not null || reading.Value is null)
            {
                continue;
            }

            if (pair.Key.Equals("state", StringComparison.OrdinalIgnoreCase))
            {
                status = reading.Value;
                continue;
            }

            if (pair.Key.Equals("alarm", StringComparison.OrdinalIgnoreCase))
            {
                alarm = reading.Value;
            }

            if (pair.Key.StartsWith("machinePosition", StringComparison.OrdinalIgnoreCase) && pair.Key.Length == "machinePosition".Length + 1)
            {
                axes[pair.Key[^1..]] = reading.Value;
            }

            var value = pair.Key.Equals("workMode", StringComparison.OrdinalIgnoreCase) && siemens
                ? "mode:" + (OpcUaMaps.ToLong(reading.Value)?.ToString(CultureInfo.InvariantCulture) ?? PointValues.Format(reading.Value))
                : reading.Value;
            if (!pair.Key.Equals("state", StringComparison.OrdinalIgnoreCase))
            {
                rows.Add(PointValues.Make(DeviceId, pair.Key, value, ItemUnit(pair.Key)));
            }
        }

        if (status is not null || alarm is not null)
        {
            rows.Add(PointValues.Make(DeviceId, "state", OpcUaMaps.MapState(status, alarm), ItemUnit("state")));
        }

        if (axes.Count > 0 && rows.All(row => !row.Point.Equals("machinePosition", StringComparison.OrdinalIgnoreCase)))
        {
            var text = string.Join(' ', new[] { "X", "Y", "Z" }.Where(axes.ContainsKey).Select(axis =>
                axis + PointValues.Format(axes[axis])));
            rows.Add(PointValues.Make(DeviceId, "machinePosition", text, ItemUnit("machinePosition")));
        }

        SetOnline($"OPC UA {Endpoint()}");
        return rows;
    }

    private bool TryNode(string itemId, bool siemens, int index, out string nodeId)
    {
        var address = Settings.AddressFor(itemId);
        if (OpcUaMaps.IsExplicitNode(address))
        {
            nodeId = OpcUaMaps.ExplicitNode(address);
            return true;
        }

        if (siemens && OpcUaMaps.Siemens.TryGetValue(itemId, out var path))
        {
            nodeId = OpcUaMaps.Node(index, path);
            return true;
        }

        if (_browse.TryGetValue(itemId, out var browsed))
        {
            nodeId = browsed;
            return true;
        }

        nodeId = "";
        return false;
    }

    private IEnumerable<string> Items(string? brandId)
    {
        var brand = CncCatalog.Current.FindBrand(brandId);
        IEnumerable<string> items = brand?.ItemIds ?? OpcUaMaps.Siemens.Keys;
        return items.Where(Settings.Wants);
    }

    private async Task<IOpcUaSession> EnsureAsync(CancellationToken cancellationToken)
    {
        if (_session is not null)
        {
            return _session;
        }

        var session = _sessions();
        try
        {
            await session.ConnectAsync(new OpcConnectInfo(Endpoint(), Settings.TimeoutMs, Settings.Username, Settings.Password), cancellationToken)
                .ConfigureAwait(false);
            _browse = await session.BrowseNamesAsync(400, cancellationToken).ConfigureAwait(false);
            _session = session;
            return session;
        }
        catch
        {
            await session.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    private async Task DropAsync()
    {
        if (_session is null)
        {
            return;
        }

        var session = _session;
        _session = null;
        _browse = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        await session.DisposeAsync().ConfigureAwait(false);
    }

    private string Endpoint()
    {
        var path = Settings.Path.Trim();
        if (path.Length > 0 && !path.StartsWith('/'))
        {
            path = "/" + path;
        }

        return "opc.tcp://" + Settings.Host.Trim() + ":" + Settings.Port.ToString(CultureInfo.InvariantCulture) + path;
    }
}
