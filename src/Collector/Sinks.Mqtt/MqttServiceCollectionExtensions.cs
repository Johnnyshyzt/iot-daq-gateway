using Gateway.Abstractions.Contracts;
using Microsoft.Extensions.DependencyInjection;

namespace Sinks.Mqtt;

public static class MqttServiceCollectionExtensions
{
    public static IServiceCollection AddMqttSink(this IServiceCollection services)
    {
        services.AddSingleton<INorthboundSink, MqttObservationSink>();
        return services;
    }
}
