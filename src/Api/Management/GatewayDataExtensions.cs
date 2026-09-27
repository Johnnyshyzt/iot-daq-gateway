using Gateway.Abstractions.Contracts;
using IotDaq.Persistence;
using Studio.Host.Config;
using Studio.Host.Northbound;
using Studio.Host.Notifications;
using Studio.Host.Oee;
using Studio.Host.Rules;
using Studio.Host.Shop;
using Studio.Host.Visualization;

namespace Studio.Host;

public static class GatewayDataExtensions
{
    public static IServiceCollection AddGatewayData(this IServiceCollection services, ConfigStore store)
    {
        services.AddSingleton(store.Database);
        services.AddSingleton<ISampleWriter>(store.Database);
        services.AddSingleton<ILinkStatusWriter>(store.Database);
        services.AddSingleton<VisualizationService>();
        services.AddSingleton<OeeService>();
        services.AddSingleton<ToolLifeService>();
        services.AddSingleton<EdgePipeline>();
        services.AddSingleton<IObservationExpander>(sp => sp.GetRequiredService<EdgePipeline>());
        services.AddSingleton<NotificationDispatcher>();
        services.AddSingleton<HttpPushDispatcher>();
        services.AddHostedService<NotificationWorker>();
        services.AddHostedService<HttpPushWorker>();
        services.AddHostedService<ContractMqttWorker>();
        services.AddHostedService<SampleRetentionService>();
        services.AddHostedService<ToolLifeWorker>();
        return services;
    }
}
