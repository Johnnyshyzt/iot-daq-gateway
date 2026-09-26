using Adapters.Cnc;
using Adapters.Fanuc;
using Adapters.Fanuc.Focas;
using Gateway.Abstractions.Contracts;
using Gateway.Host.Acquisition;
using Gateway.Host.Configuration;
using Gateway.Host.Logging;
using Gateway.Host.Programs;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Gateway.Host;

public static class CollectorHost
{
    public static bool UsesPublishedDirectory(string[] args) => ExplicitPath(args) is null;

    public static string ResolveConfigPath(string[] args, string publishedDirectory)
    {
        var specified = ExplicitPath(args);
        if (specified is null)
        {
            return Path.GetFullPath(publishedDirectory);
        }

        return ConfigPath.Resolve(["--config", specified]);
    }

    public static IServiceCollection AddFocasConnectProbe(this IServiceCollection services)
    {
        services.AddSingleton<IFocasConnectProbe, FocasConnectProbe>();
        return services;
    }

    public static IServiceCollection AddDriverServices(this IServiceCollection services)
    {
        services.AddSingleton<IDeviceConnectionTester, CncConnectionTester>();
        return services;
    }

    public static IServiceCollection AddCollector(this IServiceCollection services)
    {
        services.AddSingleton(sp => new GatewayConfigHolder(sp.GetRequiredService<IRuntimeConfigSource>().Load().Configuration));
        services.AddFanucAdapters();
        services.AddCncAdapters();
        services.AddSingleton<LiveGateway>();
        services.AddSingleton<ICollectorControl>(sp => sp.GetRequiredService<LiveGateway>());
        services.AddHostedService(sp => sp.GetRequiredService<LiveGateway>());
        services.AddSingleton<IProgramService, FeatureGatedProgramService>();
        services.AddHostedService<AcquisitionWorker>();
        return services;
    }

    public static ILoggingBuilder AddCollectorFileLog(this ILoggingBuilder builder) =>
        builder.AddGatewayRollingFile();

    private static string? ExplicitPath(string[] args)
    {
        for (var i = 0; i < args.Length - 1; i++)
        {
            if (string.Equals(args[i], "--config", StringComparison.OrdinalIgnoreCase))
            {
                return args[i + 1];
            }
        }

        var env = Environment.GetEnvironmentVariable("GATEWAY_CONFIG");
        return string.IsNullOrWhiteSpace(env) ? null : env;
    }
}
