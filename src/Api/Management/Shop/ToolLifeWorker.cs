using IotDaq.Licensing;
using Microsoft.Extensions.Hosting;
using Studio.Host.Licensing;

namespace Studio.Host.Shop;

public sealed class ToolLifeWorker(ToolLifeService tools, LicenseService licensing, ILogger<ToolLifeWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                if (licensing.Allows(LicenseFeatures.ToolLife))
                {
                    tools.Tick(null);
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogDebug(ex, "Tool life tick skipped");
            }

            try
            {
                await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }
}
