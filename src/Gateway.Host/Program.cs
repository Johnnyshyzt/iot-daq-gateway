using Adapters.Fanuc;
using Gateway.Abstractions.Contracts;
using Gateway.Host.Acquisition;
using Gateway.Host.Configuration;
using Gateway.Host.Programs;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Sinks.Mqtt;

var configPath = ConfigPath.Resolve(args);
var configuration = GatewayYamlLoader.Load(configPath);

var builder = Host.CreateApplicationBuilder(args);
builder.Logging.AddSimpleConsole(options =>
{
    options.SingleLine = true;
    options.TimestampFormat = "HH:mm:ss ";
});

builder.Services.AddSingleton(configuration);
builder.Services.AddFanucAdapters();
builder.Services.AddMqttSink();
builder.Services.AddSingleton<IProgramService, FeatureGatedProgramService>();
builder.Services.AddSingleton<IReadOnlyList<ISouthboundAdapter>>(sp =>
    AdapterFactory.Create(configuration, sp.GetServices<ISouthboundAdapterFactory>()));
builder.Services.AddHostedService<AcquisitionWorker>();

var host = builder.Build();
var logger = host.Services.GetRequiredService<ILoggerFactory>().CreateLogger("Gateway.Host");
logger.LogInformation("Loaded YAML config from {Path}", configPath);
logger.LogInformation(
    "Program transfer feature flag: {Enabled}",
    host.Services.GetRequiredService<IProgramService>().IsEnabled);

await host.RunAsync().ConfigureAwait(false);
