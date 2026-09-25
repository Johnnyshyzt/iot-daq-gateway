using System.Runtime.InteropServices;
using Adapters.Fanuc;
using Adapters.Fanuc.Focas;
using Gateway.Abstractions.Contracts;
using Gateway.Host;
using Gateway.Host.Acquisition;
using Gateway.Host.Configuration;
using Gateway.Host.Logging;
using Gateway.Host.Programs;
using Gateway.Host.Runtime;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

var configPath = ConfigPath.Resolve(args);
var loaded = GatewayConfigLoader.Load(configPath);

try
{
    Directory.SetCurrentDirectory(AppContext.BaseDirectory);
}
catch (Exception)
{
    // Service accounts may not be able to chdir; the config path is already absolute.
}

var builder = Host.CreateApplicationBuilder(args);
builder.Services.AddWindowsService(options =>
{
    options.ServiceName = HostInfo.WindowsServiceName;
});

builder.Logging.AddSimpleConsole(options =>
{
    options.SingleLine = true;
    options.TimestampFormat = "yyyy-MM-dd HH:mm:ss ";
});
builder.Logging.AddGatewayRollingFile();

builder.Services.AddSingleton(new GatewayConfigSource(configPath));
builder.Services.AddSingleton(new GatewayConfigHolder(loaded.Configuration));
builder.Services.AddFanucAdapters();
builder.Services.AddSingleton<LiveGateway>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<LiveGateway>());
builder.Services.AddSingleton<IProgramService, FeatureGatedProgramService>();
builder.Services.AddHostedService<ConfigReloadWatcher>();
builder.Services.AddHostedService<LoopbackServer>();
builder.Services.AddHostedService<AcquisitionWorker>();

var host = builder.Build();
var logger = host.Services.GetRequiredService<ILoggerFactory>().CreateLogger("Gateway.Host");
var configuration = loaded.Configuration;
logger.LogInformation(
    "iot-daq-gateway {Version} os={OS} rid={Rid} process={Bitness} baseDir={BaseDir}",
    HostInfo.Version,
    RuntimeInformation.OSDescription,
    RuntimeInformation.RuntimeIdentifier,
    Environment.Is64BitProcess ? "x64" : "x86",
    AppContext.BaseDirectory);
logger.LogInformation("Loaded config from {Path} bundle={Bundle}", loaded.SourcePath, loaded.IsBundle);
logger.LogInformation(
    "Logs directory {LogDir} (set {Env} to override)",
    FileLoggingExtensions.ResolveDirectory(),
    FileLoggingExtensions.LogDirectoryEnvironmentVariable);
logger.LogInformation(
    "MQTT {Host}:{Port} clientId={ClientId} tls={Tls}",
    configuration.Mqtt.Host,
    configuration.Mqtt.Port,
    configuration.Mqtt.ClientId,
    configuration.Mqtt.Tls);
logger.LogInformation(
    "Program transfer feature flag: {Enabled}",
    host.Services.GetRequiredService<IProgramService>().IsEnabled);
logger.LogInformation("Loopback {Url}", LoopbackServer.ResolveUrl(host.Services.GetRequiredService<IConfiguration>()) ?? "off");

if (configuration.Devices.Any(d =>
        d.Enabled && string.Equals(d.Adapter, FocasFanucAdapter.Kind, StringComparison.OrdinalIgnoreCase)))
{
    var focasLoaded = FocasLibraryFiles.TryLoad(out var focasError);
    if (focasLoaded)
    {
        logger.LogInformation("Fwlib64.dll loaded (searched {Path})", FocasLibraryFiles.ExpectedPath);
    }
    else
    {
        logger.LogWarning("Fwlib64.dll not loaded: {Reason}", focasError);
    }
}

await host.RunAsync().ConfigureAwait(false);
