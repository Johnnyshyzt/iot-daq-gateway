using Gateway.Abstractions.Contracts;

namespace Studio.Host.Runtime;

public sealed class GatewayReloadClient
{
    private readonly ICollectorControl? _collector;
    private readonly IConfiguration _configuration;
    private readonly ILogger<GatewayReloadClient> _logger;

    public GatewayReloadClient(
        IConfiguration configuration,
        ILogger<GatewayReloadClient> logger,
        IEnumerable<ICollectorControl> collectors)
    {
        _configuration = configuration;
        _logger = logger;
        _collector = collectors.FirstOrDefault();
    }

    public async Task NotifyAsync(CancellationToken cancellationToken)
    {
        if (_collector is not null)
        {
            var reloaded = await _collector.TryReloadAsync(cancellationToken).ConfigureAwait(false);
            if (!reloaded)
            {
                _logger.LogWarning("In-process acquisition reload failed; the previous session is still running");
            }

            return;
        }

        var baseUrl = _configuration["Studio:GatewayLoopback"];
        if (string.IsNullOrWhiteSpace(baseUrl) || baseUrl.Equals("off", StringComparison.OrdinalIgnoreCase))
        {
            return;
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
                _logger.LogDebug("Gateway reload returned {StatusCode}", (int)response.StatusCode);
            }
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            _logger.LogDebug(ex, "Gateway reload notify skipped");
        }
    }
}
