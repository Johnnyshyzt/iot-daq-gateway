using Gateway.Abstractions.Contracts;
using IotDaq.Persistence;
using Microsoft.Extensions.Hosting;
using Sinks.OpcUa;
using Studio.Host.Config;
using Studio.Host.Northbound;

namespace IotDaq.Host;

public sealed class OpcUaWorker : BackgroundService, IOpcUaControl
{
    private readonly GatewayPersistence _database;
    private readonly ConfigStore _store;
    private readonly ILogger<OpcUaWorker> _logger;
    private readonly object _gate = new();
    private OpcUaRuntimeInfo _info = new();
    private int _reload;

    public OpcUaWorker(GatewayPersistence database, ConfigStore store, ILogger<OpcUaWorker> logger)
    {
        _database = database;
        _store = store;
        _logger = logger;
    }

    public OpcUaRuntimeInfo Current
    {
        get
        {
            lock (_gate)
            {
                return new OpcUaRuntimeInfo
                {
                    Enabled = _info.Enabled,
                    Listening = _info.Listening,
                    Port = _info.Port,
                    Endpoint = _info.Endpoint,
                    LastError = _info.LastError
                };
            }
        }
    }

    public void Reload() => Interlocked.Increment(ref _reload);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var seen = -1;
        while (!stoppingToken.IsCancellationRequested)
        {
            var ticket = Volatile.Read(ref _reload);
            var settings = OpcUaEndpoints.Read(_database);
            if (ticket != seen || !Same(settings))
            {
                seen = ticket;
                await RestartAsync(settings, stoppingToken).ConfigureAwait(false);
            }
            else if (_server is not null)
            {
                try
                {
                    _server.Update(BuildSnapshot());
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "OPC UA value update failed");
                }
            }

            try
            {
                await Task.Delay(TimeSpan.FromSeconds(1), stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        await base.StopAsync(cancellationToken).ConfigureAwait(false);
        await DisposeServerAsync().ConfigureAwait(false);
    }

    private OpcUaStored? _applied;
    private OpcUaNorthbound? _server;

    private bool Same(OpcUaStored settings) =>
        _applied is not null
        && _applied.Enabled == settings.Enabled
        && _applied.Port == settings.Port
        && _applied.AllowAnonymous == settings.AllowAnonymous
        && _applied.AllowNone == settings.AllowNone
        && _applied.AllowSignAndEncrypt == settings.AllowSignAndEncrypt
        && _applied.Username == settings.Username
        && _applied.PasswordHash == settings.PasswordHash;

    private async Task RestartAsync(OpcUaStored settings, CancellationToken cancellationToken)
    {
        await DisposeServerAsync().ConfigureAwait(false);
        _applied = settings;
        lock (_gate)
        {
            _info = new OpcUaRuntimeInfo
            {
                Enabled = settings.Enabled,
                Port = settings.Port,
                Listening = false
            };
        }

        if (!settings.Enabled)
        {
            return;
        }

        try
        {
            var server = await OpcUaNorthbound.StartAsync(new OpcUaServerOptions
            {
                Port = settings.Port,
                AllowAnonymous = settings.AllowAnonymous,
                AllowNone = settings.AllowNone,
                AllowSignAndEncrypt = settings.AllowSignAndEncrypt,
                Username = settings.Username,
                PasswordHash = settings.PasswordHash,
                PkiDirectory = Path.Combine(_store.DataDirectory, "opcua", "pki")
            }, cancellationToken).ConfigureAwait(false);
            server.Update(BuildSnapshot());
            _server = server;
            lock (_gate)
            {
                _info = new OpcUaRuntimeInfo
                {
                    Enabled = true,
                    Listening = true,
                    Port = settings.Port,
                    Endpoint = server.EndpointUrl
                };
            }

            _logger.LogInformation("OPC UA server listening on {Endpoint}", server.EndpointUrl);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            lock (_gate)
            {
                _info = new OpcUaRuntimeInfo
                {
                    Enabled = true,
                    Listening = false,
                    Port = settings.Port,
                    LastError = ex.Message
                };
            }

            _logger.LogWarning(ex, "OPC UA server did not start");
        }
    }

    private async Task DisposeServerAsync()
    {
        var server = _server;
        _server = null;
        if (server is not null)
        {
            await server.DisposeAsync().ConfigureAwait(false);
        }
    }

    private OpcUaSnapshot BuildSnapshot()
    {
        var bundle = _store.ReadPublished();
        var ids = bundle.Devices.Select(device => device.Metadata.Id).ToList();
        var latest = ids.Count == 0 ? [] : _database.Latest(ids, []);
        var alarms = _database.QueryAlarms(null, true, null, null, null, null, 2000);
        var links = _database.ListLinkStatus().ToDictionary(row => row.DeviceId, StringComparer.OrdinalIgnoreCase);
        var pointsByDevice = latest.GroupBy(row => row.DeviceId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.ToList(), StringComparer.OrdinalIgnoreCase);
        var snapshot = new OpcUaSnapshot();
        foreach (var device in bundle.Devices)
        {
            links.TryGetValue(device.Metadata.Id, out var link);
            pointsByDevice.TryGetValue(device.Metadata.Id, out var points);
            var template = bundle.PointTemplates.FirstOrDefault(item =>
                string.Equals(item.Metadata.Id, device.Spec.PointTemplateId, StringComparison.OrdinalIgnoreCase));
            var types = template?.Spec.Points.ToDictionary(point => point.Id, point => point.DataType, StringComparer.OrdinalIgnoreCase)
                ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            snapshot.Devices.Add(new OpcUaDeviceSnapshot
            {
                Id = device.Metadata.Id,
                DisplayName = string.IsNullOrWhiteSpace(device.Metadata.DisplayName) ? device.Metadata.Id : device.Metadata.DisplayName,
                Workshop = device.Spec.Workshop ?? "",
                Line = device.Spec.Line ?? "",
                Status = link?.Status ?? "",
                StatusMessage = link?.Message ?? "",
                AlarmActive = alarms.Any(alarm => string.Equals(alarm.DeviceId, device.Metadata.Id, StringComparison.OrdinalIgnoreCase)),
                Points = (points ?? []).Select(point => new OpcUaPointSnapshot
                {
                    Id = point.PointId,
                    Value = point.Value,
                    Numeric = point.NumericValue,
                    Quality = point.Quality,
                    TimestampUnixMs = point.TimestampUnixMs,
                    DataType = types.TryGetValue(point.PointId, out var type) ? type : ""
                }).ToList()
            });
        }

        return snapshot;
    }
}
