using System.Globalization;
using Adapters.Fanuc.Focas;
using Gateway.Abstractions.Configuration;
using Gateway.Abstractions.Contracts;
using Gateway.Abstractions.Models;
using Gateway.Host.Configuration;
using Gateway.Host.Logging;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Sinks.Mqtt;

namespace Gateway.Host.Acquisition;

internal sealed class LiveGateway : IHostedService, ICollectorControl
{
    private const int MaxObservations = 200;
    private const int MaxErrors = 8;

    private readonly GatewayConfigSource _source;
    private readonly GatewayConfigHolder _holder;
    private readonly IReadOnlyList<ISouthboundAdapterFactory> _factories;
    private readonly ILoggerFactory _loggerFactory;
    private readonly ILogger<LiveGateway> _logger;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly object _stateLock = new();
    private readonly Queue<Observation> _observations = new();
    private readonly List<string> _recentErrors = [];
    private Dictionary<string, DeviceHealth> _health = new(StringComparer.OrdinalIgnoreCase);
    private Session? _session;

    public LiveGateway(
        GatewayConfigSource source,
        GatewayConfigHolder holder,
        IEnumerable<ISouthboundAdapterFactory> factories,
        ILoggerFactory loggerFactory,
        ILogger<LiveGateway> logger)
    {
        _source = source;
        _holder = holder;
        _factories = factories.ToList();
        _loggerFactory = loggerFactory;
        _logger = logger;
    }

    public string? ActiveRevision
    {
        get
        {
            lock (_stateLock)
            {
                return _session?.Revision;
            }
        }
    }

    public TimeSpan SweepInterval
    {
        get
        {
            lock (_stateLock)
            {
                return _session?.Config.Pipeline.SweepInterval ?? TimeSpan.FromSeconds(2);
            }
        }
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var loaded = GatewayConfigLoader.Load(_source.Path);
        await ApplyAsync(loaded, cancellationToken).ConfigureAwait(false);
        _logger.LogInformation(
            "Gateway session ready source={Path} revision={Revision} devices={Count}",
            loaded.SourcePath,
            loaded.Revision ?? "(none)",
            loaded.Configuration.Devices.Count);
        var configuration = loaded.Configuration;
        _logger.LogInformation(
            "MQTT {Host}:{Port} clientId={ClientId} tls={Tls}",
            configuration.Mqtt.Host,
            configuration.Mqtt.Port,
            configuration.Mqtt.ClientId,
            configuration.Mqtt.Tls);
        if (configuration.Devices.Any(device =>
                device.Enabled && string.Equals(device.Adapter, FocasFanucAdapter.Kind, StringComparison.OrdinalIgnoreCase)))
        {
            if (FocasLibraryFiles.TryLoad(out var focasError))
            {
                _logger.LogInformation("Fwlib64.dll loaded (searched {Path})", FocasLibraryFiles.ExpectedPath);
            }
            else
            {
                _logger.LogWarning("Fwlib64.dll not loaded: {Reason}", focasError);
            }
        }
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            Session? session;
            lock (_stateLock)
            {
                session = _session;
                _session = null;
            }

            if (session is not null)
            {
                await session.DisposeAsync().ConfigureAwait(false);
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<bool> TryReloadAsync(CancellationToken cancellationToken)
    {
        LoadedGateway loaded;
        try
        {
            loaded = GatewayConfigLoader.Load(_source.Path);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Config reload failed; keeping the running session");
            NoteError(ex.Message);
            return false;
        }

        try
        {
            await ApplyAsync(loaded, cancellationToken).ConfigureAwait(false);
            _logger.LogInformation(
                "Reloaded config from {Path} revision={Revision}",
                loaded.SourcePath,
                loaded.Revision ?? "(none)");
            return true;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Config reload failed; keeping the running session");
            NoteError(ex.Message);
            return false;
        }
    }

    public async Task SweepAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var session = CurrentSession();
            foreach (var adapter in session.Adapters)
            {
                await SweepAdapterAsync(session, adapter, cancellationToken).ConfigureAwait(false);
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    public object StatusDocument()
    {
        Session? session;
        Dictionary<string, DeviceHealth> health;
        List<string> errors;
        lock (_stateLock)
        {
            session = _session;
            health = new Dictionary<string, DeviceHealth>(_health, StringComparer.OrdinalIgnoreCase);
            errors = _recentErrors.ToList();
        }

        var config = session?.Config ?? _holder.Current;
        var devices = config.Devices.Select(device =>
        {
            var displayName = OptionText(device.Options, "displayName") ?? device.Id;
            if (!device.Enabled)
            {
                return new
                {
                    id = device.Id,
                    displayName,
                    enabled = false,
                    adapter = device.Adapter,
                    status = "disabled",
                    lastSeen = (DateTimeOffset?)null,
                    message = "设备已禁用"
                };
            }

            if (!health.TryGetValue(device.Id, out var item))
            {
                return new
                {
                    id = device.Id,
                    displayName,
                    enabled = true,
                    adapter = device.Adapter,
                    status = "offline",
                    lastSeen = (DateTimeOffset?)null,
                    message = "尚未完成首轮扫描"
                };
            }

            return new
            {
                id = device.Id,
                displayName,
                enabled = true,
                adapter = device.Adapter,
                status = item.Status.ToString().ToLowerInvariant(),
                lastSeen = (DateTimeOffset?)item.Timestamp,
                message = item.Message ?? ""
            };
        }).ToList();

        return new
        {
            name = session?.DisplayName ?? config.Gateway.Id,
            siteId = config.Gateway.Site,
            state = "running",
            mode = "live",
            activeRevision = session?.Revision ?? "",
            utcNow = DateTimeOffset.UtcNow,
            devices,
            recentErrors = errors
        };
    }

    public object ObservationsDocument(string? deviceId, int limit)
    {
        limit = Math.Clamp(limit, 1, MaxObservations);
        List<Observation> snapshot;
        lock (_stateLock)
        {
            snapshot = _observations.ToList();
        }

        IEnumerable<Observation> rows = snapshot;
        if (!string.IsNullOrWhiteSpace(deviceId))
        {
            rows = rows.Where(item => string.Equals(item.DeviceId, deviceId, StringComparison.Ordinal));
        }

        var observations = rows
            .Reverse()
            .Take(limit)
            .Select(item => new
            {
                deviceId = item.DeviceId,
                point = item.Point,
                value = FormatValue(item.Value),
                quality = item.Quality,
                unit = item.Unit,
                timestamp = item.Timestamp
            })
            .ToList();

        return new { observations };
    }

    public IReadOnlyList<string> TailLogs(int lines) => GatewayLogFiles.Tail(lines);

    private async Task ApplyAsync(LoadedGateway loaded, CancellationToken cancellationToken)
    {
        var session = await Session.OpenAsync(loaded, _factories, _loggerFactory, _logger, cancellationToken)
            .ConfigureAwait(false);
        Session? previous = null;
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            lock (_stateLock)
            {
                previous = _session;
                _session = session;
                _holder.Current = session.Config;
                _health = new Dictionary<string, DeviceHealth>(StringComparer.OrdinalIgnoreCase);
                _observations.Clear();
            }
        }
        finally
        {
            _gate.Release();
        }

        if (previous is not null)
        {
            await previous.DisposeAsync().ConfigureAwait(false);
        }
    }

    private async Task SweepAdapterAsync(Session session, ISouthboundAdapter adapter, CancellationToken cancellationToken)
    {
        try
        {
            var observations = await adapter.CollectAsync(cancellationToken).ConfigureAwait(false);
            foreach (var observation in observations)
            {
                Remember(observation);
                if (!session.Filter.ShouldPublish(observation))
                {
                    continue;
                }

                await session.Sink.PublishObservationAsync(observation, cancellationToken).ConfigureAwait(false);
            }

            var health = await adapter.GetHealthAsync(cancellationToken).ConfigureAwait(false);
            Remember(health);
            await session.Sink.PublishStatusAsync(health, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Sweep failed for {DeviceId} ({Kind})", adapter.DeviceId, adapter.AdapterKind);
            NoteError($"{adapter.DeviceId}: {ex.Message}");
            var health = new DeviceHealth
            {
                DeviceId = adapter.DeviceId,
                Status = AdapterStatus.Offline,
                Message = ex.Message,
                Timestamp = DateTimeOffset.UtcNow
            };
            Remember(health);
            try
            {
                await session.Sink.PublishStatusAsync(health, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception publishEx) when (publishEx is not OperationCanceledException)
            {
                _logger.LogDebug(publishEx, "Status publish failed for {DeviceId}", adapter.DeviceId);
            }
        }
    }

    private Session CurrentSession()
    {
        lock (_stateLock)
        {
            return _session ?? throw new InvalidOperationException("Gateway session is not running.");
        }
    }

    private void Remember(Observation observation)
    {
        lock (_stateLock)
        {
            _observations.Enqueue(observation);
            while (_observations.Count > MaxObservations)
            {
                _observations.Dequeue();
            }
        }
    }

    private void Remember(DeviceHealth health)
    {
        lock (_stateLock)
        {
            _health[health.DeviceId] = health;
        }
    }

    private void NoteError(string message)
    {
        lock (_stateLock)
        {
            _recentErrors.Add(message);
            while (_recentErrors.Count > MaxErrors)
            {
                _recentErrors.RemoveAt(0);
            }
        }
    }

    private static string? OptionText(IReadOnlyDictionary<string, object?> options, string key)
    {
        if (!options.TryGetValue(key, out var value) || value is null)
        {
            return null;
        }

        return Convert.ToString(value, CultureInfo.InvariantCulture);
    }

    private static string? FormatValue(object? value) => value switch
    {
        null => null,
        string text => text,
        IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
        _ => value.ToString()
    };

    private sealed class Session : IAsyncDisposable
    {
        private Session(
            GatewayConfiguration config,
            string? revision,
            string displayName,
            INorthboundSink sink,
            IReadOnlyList<ISouthboundAdapter> adapters,
            ChangeOnlyFilter filter)
        {
            Config = config;
            Revision = revision;
            DisplayName = displayName;
            Sink = sink;
            Adapters = adapters;
            Filter = filter;
        }

        public GatewayConfiguration Config { get; }

        public string? Revision { get; }

        public string DisplayName { get; }

        public INorthboundSink Sink { get; }

        public IReadOnlyList<ISouthboundAdapter> Adapters { get; }

        public ChangeOnlyFilter Filter { get; }

        public static async Task<Session> OpenAsync(
            LoadedGateway loaded,
            IReadOnlyList<ISouthboundAdapterFactory> factories,
            ILoggerFactory loggerFactory,
            ILogger logger,
            CancellationToken cancellationToken)
        {
            var adapters = AdapterFactory.Create(loaded.Configuration, factories);
            var sink = new MqttObservationSink(
                loaded.Configuration,
                loggerFactory.CreateLogger<MqttObservationSink>());
            try
            {
                await sink.StartAsync(cancellationToken).ConfigureAwait(false);
                foreach (var adapter in adapters)
                {
                    try
                    {
                        await adapter.ConnectAsync(cancellationToken).ConfigureAwait(false);
                    }
                    catch (Exception ex) when (ex is not OperationCanceledException)
                    {
                        logger.LogError(ex, "Failed to connect adapter {DeviceId}", adapter.DeviceId);
                    }
                }

                return new Session(
                    loaded.Configuration,
                    loaded.Revision,
                    loaded.DisplayName,
                    sink,
                    adapters,
                    new ChangeOnlyFilter(loaded.Configuration.Pipeline.ChangeOnly));
            }
            catch
            {
                await sink.DisposeAsync().ConfigureAwait(false);
                foreach (var adapter in adapters)
                {
                    await adapter.DisposeAsync().ConfigureAwait(false);
                }

                throw;
            }
        }

        public async ValueTask DisposeAsync()
        {
            foreach (var adapter in Adapters)
            {
                await adapter.DisposeAsync().ConfigureAwait(false);
            }

            await Sink.DisposeAsync().ConfigureAwait(false);
        }
    }
}
