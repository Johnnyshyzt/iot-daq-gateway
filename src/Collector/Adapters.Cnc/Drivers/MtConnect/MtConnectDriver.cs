using System.Diagnostics;
using System.Net;
using Cnc.Catalog;
using Gateway.Abstractions.Configuration;
using Gateway.Abstractions.Models;
using Microsoft.Extensions.Logging;

namespace Adapters.Cnc.Drivers;

public sealed class MtConnectDriver : CatalogProtocolDriver
{
    private readonly HttpClient _http;

    public MtConnectDriver(DeviceBinding binding, CatalogAdapter adapter, ILogger logger)
        : this(binding, adapter, logger, CreateClient(ConnectionSettings.Read(binding).TimeoutMs))
    {
    }

    internal MtConnectDriver(DeviceBinding binding, CatalogAdapter adapter, ILogger logger, HttpClient http)
        : base(binding, adapter, logger)
    {
        _http = http;
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
                Message = $"MTConnect 网络不可达：{tcp.Error}",
                Error = tcp.Error,
                SdkStatus = "none"
            };
        }

        var watch = Stopwatch.StartNew();
        try
        {
            var samples = await ReadAsync(cancellationToken).ConfigureAwait(false);
            watch.Stop();
            var handshake = samples.Count > 0;
            return new ConnectionReport
            {
                Reachable = true,
                Handshake = handshake,
                ReachableMs = tcp.ElapsedMs,
                HandshakeMs = (int)watch.ElapsedMilliseconds,
                Message = handshake
                    ? $"MTConnect 握手成功，读到 {samples.Count} 个目录项。"
                    : "MTConnect 已连通，但 /current 没有可映射的 DataItem。",
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
                Message = "MTConnect 已连通，协议读取失败。",
                Error = ex.Message,
                SdkStatus = "none"
            };
        }
    }

    protected override async Task<IReadOnlyList<Observation>> ReadAsync(CancellationToken cancellationToken)
    {
        var path = string.IsNullOrWhiteSpace(Settings.Path) ? "/current" : Settings.Path;
        if (!path.StartsWith('/'))
        {
            path = "/" + path;
        }

        var uri = new UriBuilder("http", Settings.Host, Settings.Port, path).Uri;
        using var response = await _http.GetAsync(uri, cancellationToken).ConfigureAwait(false);
        var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"MTConnect {(int)response.StatusCode} {response.ReasonPhrase}");
        }

        var parsed = MtConnectXml.Parse(body);
        var error = parsed.FirstOrDefault(sample => sample.Type.Equals("Error", StringComparison.OrdinalIgnoreCase));
        if (error is not null && !string.IsNullOrWhiteSpace(error.Value))
        {
            throw new InvalidOperationException(error.Value);
        }

        var mapped = MtConnectXml.ToCatalog(parsed);
        var rows = new List<Observation>();
        foreach (var pair in mapped)
        {
            rows.Add(PointValues.Make(DeviceId, pair.Key, pair.Value, ItemUnit(pair.Key)));
        }

        SetOnline($"MTConnect {uri}");
        return rows;
    }

    protected override Task OnDisconnectAsync()
    {
        _http.Dispose();
        return Task.CompletedTask;
    }

    private static HttpClient CreateClient(int timeoutMs)
    {
        var client = new HttpClient(new SocketsHttpHandler
        {
            AutomaticDecompression = DecompressionMethods.All,
            ConnectTimeout = TimeSpan.FromMilliseconds(Math.Clamp(timeoutMs, 100, 120_000))
        })
        {
            Timeout = TimeSpan.FromMilliseconds(Math.Clamp(timeoutMs, 100, 120_000))
        };
        return client;
    }
}
