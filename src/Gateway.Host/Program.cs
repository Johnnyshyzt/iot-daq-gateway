using System.Runtime.InteropServices;
using Adapters.Fanuc;
using Adapters.Fanuc.Focas;
using Gateway.Abstractions.Contracts;
using Gateway.Host;
using Gateway.Host.Acquisition;
using Gateway.Host.Configuration;
using Gateway.Host.Logging;
using Gateway.Host.Programs;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Sinks.Mqtt;

var configPath = ConfigPath.Resolve(args);
var configuration = GatewayYamlLoader.Load(configPath);

try
{
    Directory.SetCurrentDirectory(AppContext.BaseDirectory);
}
catch (Exception)
{
    // Service accounts may not be able to chdir; YAML is already resolved to a full path.
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

builder.Services.AddSingleton(configuration);
builder.Services.AddFanucAdapters();
builder.Services.AddMqttSink();
builder.Services.AddSingleton<IProgramService, FeatureGatedProgramService>();
builder.Services.AddSingleton<IReadOnlyList<ISouthboundAdapter>>(sp =>
    AdapterFactory.Create(configuration, sp.GetServices<ISouthboundAdapterFactory>()));
builder.Services.AddHostedService<AcquisitionWorker>();

var host = builder.Build();
var logger = host.Services.GetRequiredService<ILoggerFactory>().CreateLogger("Gateway.Host");
logger.LogInformation(
    "iot-daq-gateway {Version} os={OS} rid={Rid} process={Bitness} baseDir={BaseDir}",
    HostInfo.Version,
    RuntimeInformation.OSDescription,
    RuntimeInformation.RuntimeIdentifier,
    Environment.Is64BitProcess ? "x64" : "x86",
    AppContext.BaseDirectory);
logger.LogInformation("Loaded YAML config from {Path}", configPath);
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

if (configuration.Devices.Any(d =>
        d.Enabled && string.Equals(d.Adapter, FocasFanucAdapter.Kind, StringComparison.OrdinalIgnoreCase)))
{
    var loaded = FocasLibraryFiles.TryLoad(out var focasError);
    if (loaded)
    {
        logger.LogInformation("Fwlib64.dll loaded (searched {Path})", FocasLibraryFiles.ExpectedPath);
    }
    else
    {
        logger.LogWarning(
            "Fwlib64.dll not loaded: {Reason}. Place the licensed 64-bit DLL next to Gateway.Host.exe. Linux Docker is not a production FOCAS path.",
            focasError);
    }
}

await host.RunAsync().ConfigureAwait(false);
