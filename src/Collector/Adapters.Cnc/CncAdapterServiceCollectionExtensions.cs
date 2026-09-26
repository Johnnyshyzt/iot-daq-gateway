using Cnc.Catalog;
using Gateway.Abstractions.Contracts;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Adapters.Cnc;

public static class CncAdapterServiceCollectionExtensions
{
    public static IServiceCollection AddCncAdapters(this IServiceCollection services)
    {
        foreach (var adapter in CncCatalog.Current.AllAdapters)
        {
            if (adapter.Id is "fanuc.fake" or "fanuc.focas")
            {
                continue;
            }

            var captured = adapter;
            services.AddSingleton<ISouthboundAdapterFactory>(sp =>
                new CatalogAdapterFactory(captured, sp.GetRequiredService<ILoggerFactory>()));
        }

        return services;
    }
}
