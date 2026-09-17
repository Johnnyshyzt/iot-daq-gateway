using Gateway.Abstractions.Configuration;
using Gateway.Abstractions.Contracts;
using Gateway.Abstractions.Models;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Gateway.Host.Acquisition;

internal sealed class AcquisitionWorker : BackgroundService
{
    private readonly GatewayConfiguration _config;
    private readonly INorthboundSink _sink;
    private readonly IReadOnlyList<ISouthboundAdapter> _adapters;
    private readonly ChangeOnlyFilter _changeFilter;
    private readonly ILogger<AcquisitionWorker> _logger;

    public AcquisitionWorker(
        GatewayConfiguration config,
        INorthboundSink sink,
        IReadOnlyList<ISouthboundAdapter> adapters,
        ILogger<AcquisitionWorker> logger)
    {
        _config = config;
        _sink = sink;
        _adapters = adapters;
        _changeFilter = new ChangeOnlyFilter(config.Pipeline.ChangeOnly);
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation(
            "Gateway {GatewayId} site={Site} adapters={Count} sweep={Sweep} changeOnly={ChangeOnly}",
            _config.Gateway.Id,
            _config.Gateway.Site,
            _adapters.Count,
            _config.Pipeline.SweepInterval,
            _config.Pipeline.ChangeOnly);

        await _sink.StartAsync(stoppingToken).ConfigureAwait(false);

        foreach (var adapter in _adapters)
        {
            try
            {
                await adapter.ConnectAsync(stoppingToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "Failed to connect adapter {DeviceId}", adapter.DeviceId);
            }
        }

        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                foreach (var adapter in _adapters)
                {
                    await SweepAsync(adapter, stoppingToken).ConfigureAwait(false);
                }

                try
                {
                    await Task.Delay(_config.Pipeline.SweepInterval, stoppingToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }
        finally
        {
            foreach (var adapter in _adapters)
            {
                await adapter.DisposeAsync().ConfigureAwait(false);
            }
        }
    }

    private async Task SweepAsync(ISouthboundAdapter adapter, CancellationToken cancellationToken)
    {
        try
        {
            var observations = await adapter.CollectAsync(cancellationToken).ConfigureAwait(false);
            foreach (var observation in observations)
            {
                if (!_changeFilter.ShouldPublish(observation))
                {
                    continue;
                }

                await _sink.PublishObservationAsync(observation, cancellationToken).ConfigureAwait(false);
            }

            var health = await adapter.GetHealthAsync(cancellationToken).ConfigureAwait(false);
            await _sink.PublishStatusAsync(health, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Sweep failed for {DeviceId} ({Kind})", adapter.DeviceId, adapter.AdapterKind);
            var health = new DeviceHealth
            {
                DeviceId = adapter.DeviceId,
                Status = AdapterStatus.Offline,
                Message = ex.Message,
                Timestamp = DateTimeOffset.UtcNow
            };
            try
            {
                await _sink.PublishStatusAsync(health, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception publishEx) when (publishEx is not OperationCanceledException)
            {
                _logger.LogDebug(publishEx, "Status publish failed for {DeviceId}", adapter.DeviceId);
            }
        }
    }
}
