using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Gateway.Host.Logging;

internal static class FileLoggingExtensions
{
    public const string LogDirectoryEnvironmentVariable = "GATEWAY_LOG_DIR";

    public static ILoggingBuilder AddGatewayRollingFile(this ILoggingBuilder builder)
    {
        builder.Services.AddSingleton<ILoggerProvider>(_ =>
            new RollingFileLoggerProvider(ResolveDirectory()));
        return builder;
    }

    public static string ResolveDirectory()
    {
        var configured = Environment.GetEnvironmentVariable(LogDirectoryEnvironmentVariable);
        if (!string.IsNullOrWhiteSpace(configured))
        {
            return Path.GetFullPath(configured);
        }

        return Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "logs"));
    }
}
