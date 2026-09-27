using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Gateway.Host.Acquisition;

/// <summary>
/// Cancels a device collect that has not made progress within the stall timeout
/// so the acquisition loop can dispose that adapter and start a new one.
/// </summary>
internal sealed class AcquisitionWatchdog(LiveGateway gateway, ILogger<AcquisitionWatchdog> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var restarted = gateway.HealStalled(DateTimeOffset.UtcNow);
                if (restarted > 0)
                {
                    logger.LogWarning("Watchdog cancelled {Count} stalled device collect(s)", restarted);
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogDebug(ex, "Watchdog tick failed");
            }

            try
            {
                await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }
}
