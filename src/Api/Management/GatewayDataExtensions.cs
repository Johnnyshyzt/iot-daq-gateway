using Gateway.Abstractions.Contracts;
using IotDaq.Persistence;
using Studio.Host.Config;

namespace Studio.Host;

public static class GatewayDataExtensions
{
    public static IServiceCollection AddGatewayData(this IServiceCollection services, ConfigStore store)
    {
        services.AddSingleton(store.Database);
        services.AddSingleton<ISampleWriter>(store.Database);
        services.AddHostedService<SampleRetentionService>();
        return services;
    }
}
