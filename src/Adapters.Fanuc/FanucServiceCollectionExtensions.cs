using Adapters.Fanuc.Fake;
using Adapters.Fanuc.Focas;
using Gateway.Abstractions.Contracts;
using Microsoft.Extensions.DependencyInjection;

namespace Adapters.Fanuc;

public static class FanucServiceCollectionExtensions
{
    public static IServiceCollection AddFanucAdapters(this IServiceCollection services)
    {
        services.AddSingleton<ISouthboundAdapterFactory, FakeFanucAdapterFactory>();
        services.AddSingleton<ISouthboundAdapterFactory, FocasFanucAdapterFactory>();
        return services;
    }
}
