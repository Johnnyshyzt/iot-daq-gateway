using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Gateway.Host.Acquisition;

internal sealed class AcquisitionWorker(LiveGateway gateway, ILogger<AcquisitionWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("Acquisition loop sweep={Sweep}", gateway.SweepInterval);
        while (!stoppingToken.IsCancellationRequested)
        {
            await gateway.SweepAsync(stoppingToken).ConfigureAwait(false);
            try
            {
                await Task.Delay(gateway.SweepInterval, stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }
}
