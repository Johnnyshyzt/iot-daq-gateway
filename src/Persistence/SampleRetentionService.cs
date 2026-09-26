using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace IotDaq.Persistence;

public sealed class SampleRetentionService(GatewayPersistence database, ILogger<SampleRetentionService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromHours(1));
        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false))
            {
                var cutoff = DateTimeOffset.UtcNow.AddDays(-database.HistoryRetentionDays);
                var removed = database.PurgeOlderThan(cutoff);
                if (removed > 0)
                {
                    logger.LogInformation("Removed {Count} expired samples and alarms older than {Cutoff}", removed, cutoff);
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
    }
}
