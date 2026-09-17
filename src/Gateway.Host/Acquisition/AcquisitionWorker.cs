using Gateway.Abstractions.Configuration;
using Gateway.Abstractions.Contracts;
using Gateway.Abstractions.Models;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Gateway.Host.Acquisition;

internal sealed class AcquisitionWorker : BackgroundService
{
    private readonly PipelineManager _pipeline;
    private readonly ILogger<AcquisitionWorker> _logger;

    public AcquisitionWorker(PipelineManager pipeline, ILogger<AcquisitionWorker> logger)
    {
        _pipeline = pipeline;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            var generation = _pipeline.Generation;
            var config = _pipeline.Current;
            using var generationCts = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
            _pipeline.AttachGeneration(generationCts);
            if (_pipeline.Generation != generation)
            {
                continue;
            }

            try
            {
                await RunGenerationAsync(config, generationCts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!stoppingToken.IsCancellationRequested)
            {
                _logger.LogInformation("Configuration applied; restarting collection");
            }
        }
    }

    private async Task RunGenerationAsync(GatewayConfiguration config, CancellationToken stoppingToken)
    {
        var adapters = _pipeline.CreateAdapters(config);
        await using var sink = _pipeline.CreateSink(config);
        var changeFilter = new ChangeOnlyFilter(config.Pipeline.ChangeOnly);

        _logger.LogInformation(
            "Gateway {GatewayId} site={Site} adapters={Count} sweep={Sweep} changeOnly={ChangeOnly}",
            config.Gateway.Id,
            config.Gateway.Site,
            adapters.Count,
            config.Pipeline.SweepInterval,
            config.Pipeline.ChangeOnly);

        if (adapters.Count == 0)
        {
            _logger.LogWarning("No enabled devices; collection is idle until the web console adds one.");
        }

        await sink.StartAsync(stoppingToken).ConfigureAwait(false);

        foreach (var adapter in adapters)
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
                foreach (var adapter in adapters)
                {
                    await SweepAsync(adapter, sink, changeFilter, stoppingToken).ConfigureAwait(false);
                }

                try
                {
                    await Task.Delay(config.Pipeline.SweepInterval, stoppingToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }
        finally
        {
            foreach (var adapter in adapters)
            {
                await adapter.DisposeAsync().ConfigureAwait(false);
            }
        }
    }

    private async Task SweepAsync(
        ISouthboundAdapter adapter,
        INorthboundSink sink,
        ChangeOnlyFilter changeFilter,
        CancellationToken cancellationToken)
    {
        try
        {
            var observations = await adapter.CollectAsync(cancellationToken).ConfigureAwait(false);
            foreach (var observation in observations)
            {
                if (!changeFilter.ShouldPublish(observation))
                {
                    continue;
                }

                await sink.PublishObservationAsync(observation, cancellationToken).ConfigureAwait(false);
            }

            var health = await adapter.GetHealthAsync(cancellationToken).ConfigureAwait(false);
            await sink.PublishStatusAsync(health, cancellationToken).ConfigureAwait(false);
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
                await sink.PublishStatusAsync(health, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception publishEx) when (publishEx is not OperationCanceledException)
            {
                _logger.LogDebug(publishEx, "Status publish failed for {DeviceId}", adapter.DeviceId);
            }
        }
    }
}
