using Microsoft.Extensions.Hosting;

namespace Studio.Host.Central;

public sealed class CentralWorker(CentralCoordinator central, HostMode mode, IConfiguration configuration, ILogger<CentralWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var seconds = 15;
        if (int.TryParse(configuration["Central:IntervalSeconds"], System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var configured))
        {
            seconds = Math.Clamp(configured, 2, 300);
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                if (mode.Central && !string.Equals(configuration["Central:AutoScan"], "false", StringComparison.OrdinalIgnoreCase))
                {
                    central.ScanOffline();
                }
                else if (!mode.Central
                    && !string.Equals(configuration["Central:AutoHeartbeat"], "false", StringComparison.OrdinalIgnoreCase)
                    && !string.IsNullOrWhiteSpace(configuration["Central:Url"]))
                {
                    await central.PulseAsync(stoppingToken).ConfigureAwait(false);
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogInformation("Central worker skipped a cycle: {Message}", ex.Message);
            }

            try
            {
                await Task.Delay(TimeSpan.FromSeconds(seconds), stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }
}
