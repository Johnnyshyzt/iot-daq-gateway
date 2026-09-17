using Gateway.Abstractions.Configuration;
using Gateway.Abstractions.Contracts;
using Gateway.Host.Configuration;
using Microsoft.Extensions.Logging;
using Sinks.Mqtt;

namespace Gateway.Host.Acquisition;

internal sealed class PipelineManager
{
    private readonly string _configPath;
    private readonly ISouthboundAdapterFactory[] _factories;
    private readonly ILoggerFactory _loggerFactory;
    private readonly ILogger<PipelineManager> _logger;
    private readonly object _gate = new();
    private GatewayConfiguration _config;
    private int _generation;
    private CancellationTokenSource? _generationCts;

    public PipelineManager(
        string configPath,
        GatewayConfiguration initial,
        IEnumerable<ISouthboundAdapterFactory> factories,
        ILoggerFactory loggerFactory,
        ILogger<PipelineManager> logger)
    {
        _configPath = Path.GetFullPath(configPath);
        _config = initial;
        _factories = factories.ToArray();
        _loggerFactory = loggerFactory;
        _logger = logger;
        AdapterFactory.EnsureKnownKinds(initial, _factories);
    }

    public string ConfigPath => _configPath;

    public GatewayConfiguration Current
    {
        get
        {
            lock (_gate)
            {
                return _config;
            }
        }
    }

    public int Generation
    {
        get
        {
            lock (_gate)
            {
                return _generation;
            }
        }
    }

    public void AttachGeneration(CancellationTokenSource generationCts)
    {
        lock (_gate)
        {
            _generationCts = generationCts;
        }
    }

    public GatewayConfiguration ApplyFromConsole(ConsoleConfigDocument document)
    {
        var baseline = File.Exists(_configPath) ? GatewayYamlLoader.Load(_configPath) : Current;
        var next = ConsoleConfigMapper.Merge(baseline, document);
        AdapterFactory.EnsureKnownKinds(next, _factories);
        GatewayYamlLoader.Save(_configPath, next);
        Replace(next);
        _logger.LogInformation("Wrote YAML {Path} and reloading collection", _configPath);
        return next;
    }

    internal GatewayConfiguration ApplyConfiguration(GatewayConfiguration next)
    {
        AdapterFactory.EnsureKnownKinds(next, _factories);
        GatewayYamlLoader.Save(_configPath, next);
        Replace(next);
        return next;
    }

    public INorthboundSink CreateSink(GatewayConfiguration config)
    {
        return new MqttObservationSink(config, _loggerFactory.CreateLogger<MqttObservationSink>());
    }

    public IReadOnlyList<ISouthboundAdapter> CreateAdapters(GatewayConfiguration config)
    {
        return AdapterFactory.Create(config, _factories);
    }

    private void Replace(GatewayConfiguration next)
    {
        lock (_gate)
        {
            _config = next;
            _generation++;
            try
            {
                _generationCts?.Cancel();
            }
            catch (ObjectDisposedException)
            {
                // generation already cycling
            }
        }
    }
}
