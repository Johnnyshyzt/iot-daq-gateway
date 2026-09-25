namespace Studio.Host.Runtime;

public sealed class GatewayReloadClient(IConfiguration configuration, ILogger<GatewayReloadClient> logger)
{
    public async Task NotifyAsync(CancellationToken cancellationToken)
    {
        var baseUrl = configuration["Studio:GatewayLoopback"];
        if (string.IsNullOrWhiteSpace(baseUrl))
        {
            baseUrl = "http://127.0.0.1:5081";
        }

        try
        {
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(2) };
            using var response = await client.PostAsync(
                baseUrl.TrimEnd('/') + "/api/v1/runtime/reload",
                content: null,
                cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                logger.LogDebug("Gateway reload returned {StatusCode}", (int)response.StatusCode);
            }
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            logger.LogDebug(ex, "Gateway reload notify skipped");
        }
    }
}
