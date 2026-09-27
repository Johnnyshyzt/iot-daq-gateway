using System.Runtime.InteropServices;
using System.Text.Json;
using Gateway.Abstractions.Contracts;
using Gateway.Abstractions.Reliability;
using Gateway.Host;
using Microsoft.Extensions.Hosting;
using IotDaq.Host;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.Extensions.FileProviders;
using Studio.Contracts;
using Studio.Host;
using Studio.Host.Auth;
using Studio.Host.Config;
using Studio.Host.Endpoints;
using Studio.Host.Licensing;
using Studio.Host.Runtime;
using Studio.Host.Visualization;

var builder = WebApplication.CreateBuilder(args);
builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
    options.SerializerOptions.PropertyNameCaseInsensitive = true;
});
builder.Host.UseWindowsService(options =>
{
    options.ServiceName = HostInfo.WindowsServiceName;
});
builder.Logging.AddSimpleConsole(options =>
{
    options.SingleLine = true;
    options.TimestampFormat = "yyyy-MM-dd HH:mm:ss ";
});
builder.Logging.AddCollectorFileLog();

var dataDirectory = HostPaths.ResolveDataDirectory(builder.Environment, builder.Configuration);
builder.Configuration["Host:DataDirectory"] = dataDirectory;
var acquisitionOn = !string.Equals(builder.Configuration["Host:Acquisition"], "off", StringComparison.OrdinalIgnoreCase);
var importPath = CollectorHost.UsesPublishedDirectory(args)
    ? null
    : CollectorHost.ResolveConfigPath(args, Path.Combine(dataDirectory, "published"));
var demoRequested = args.Any(arg => string.Equals(arg, "--demo", StringComparison.OrdinalIgnoreCase))
    || string.Equals(builder.Configuration["Host:Demo"], "true", StringComparison.OrdinalIgnoreCase)
    || string.Equals(Environment.GetEnvironmentVariable("HOST_DEMO"), "1", StringComparison.Ordinal);
var store = new ConfigStore(dataDirectory);
var licensing = new LicenseService(store.Database, builder.Configuration);
store.LimitIncrease = licensing.RejectIncrease;
builder.Services.AddSingleton(licensing);
builder.Services.AddSingleton(store);
builder.Services.AddSingleton<IRuntimeConfigSource>(_ => new DatabaseRuntimeConfigSource(store));
builder.Services.AddGatewayData(store);
if (acquisitionOn)
{
    builder.Services.AddCollector();
}
else
{
    builder.Services.AddSingleton<MqttBufferStatus>();
    builder.Services.AddSingleton<IMqttBufferStatus>(sp => sp.GetRequiredService<MqttBufferStatus>());
}
AccountStore.AccountsChanged = path => store.Database.SyncUsers(path);
builder.Services.AddSingleton(sp => new AccountStore(
    dataDirectory,
    sp.GetRequiredService<IConfiguration>(),
    sp.GetRequiredService<ILogger<AccountStore>>()));
builder.Services.AddFocasConnectProbe();
builder.Services.AddDriverServices();
builder.Services.AddSingleton<OpcUaWorker>();
builder.Services.AddSingleton<IOpcUaControl>(sp => sp.GetRequiredService<OpcUaWorker>());
builder.Services.AddHostedService(sp => sp.GetRequiredService<OpcUaWorker>());
builder.Services.AddSingleton<TokenService>();
builder.Services.AddSingleton<RuntimeQueries>();
builder.Services.AddSingleton<GatewayReloadClient>();
builder.Services.AddCors(options =>
{
    options.AddPolicy("studio-dev", policy =>
    {
        policy.WithOrigins("http://127.0.0.1:5173", "http://localhost:5173")
            .AllowAnyHeader()
            .AllowAnyMethod();
    });
});

var app = builder.Build();
app.Services.GetRequiredService<ConfigStore>().EnsureInitialized(importPath);
if (demoRequested)
{
    try
    {
        var seeded = DemoMode.Seed(store, publish: true);
        app.Logger.LogInformation("Demo mode: {Message}", seeded.Message);
    }
    catch (ConfigStoreException ex)
    {
        app.Logger.LogWarning("Demo mode did not add devices: {Message}", ex.Message);
    }
}

UpgradeCoordinator.NoteBoot(dataDirectory, HostInfo.Version, app.Logger);
if (app.Services.GetService<ReliabilityOptions>() is { } reliability)
{
    reliability.OverlayJson(store.Database.GetSetting("reliability"));
}

DemoHistorySeeder.SeedIfEmpty(store.Database, store.ReadPublished());
var accounts = app.Services.GetRequiredService<AccountStore>();
accounts.EnsureInitialized();
store.Database.SyncUsers(Path.Combine(dataDirectory, "auth", "accounts.json"));

app.UseExceptionHandler(handler =>
{
    handler.Run(async context =>
    {
        var error = context.Features.Get<IExceptionHandlerFeature>()?.Error;
        if (error is ConfigStoreException storeError)
        {
            context.Response.StatusCode = storeError.StatusCode;
            await context.Response.WriteAsJsonAsync(
                new ApiError { Code = storeError.Code, Message = storeError.Message },
                StudioJson.Options);
            return;
        }

        var logger = context.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger("Host");
        logger.LogError(error, "Unhandled host error");
        context.Response.StatusCode = StatusCodes.Status500InternalServerError;
        await context.Response.WriteAsJsonAsync(
            new ApiError { Code = "internal_error", Message = "服务器内部错误" },
            StudioJson.Options);
    });
});

app.UseCors("studio-dev");

var webRoot = HostPaths.ResolveWebRoot(app.Environment);
if (webRoot is not null)
{
    var files = new PhysicalFileProvider(webRoot);
    app.UseDefaultFiles(new DefaultFilesOptions { FileProvider = files });
    app.UseStaticFiles(new StaticFileOptions { FileProvider = files });
}

app.UseMiddleware<Studio.Host.Northbound.ApiKeyMiddleware>();
app.UseMiddleware<StudioAuthMiddleware>();
var startedAt = DateTimeOffset.UtcNow;
app.MapGet("/healthz", (IEnumerable<IMqttBufferStatus> buffers) =>
{
    var buffer = buffers.FirstOrDefault();
    return Results.Json(new
    {
        status = "ok",
        version = HostInfo.Version,
        acquisition = acquisitionOn,
        database = store.Database.Provider,
        schemaVersion = store.Database.CurrentSchemaVersion,
        uptimeSeconds = Math.Max(0, (long)(DateTimeOffset.UtcNow - startedAt).TotalSeconds),
        mqttConnected = buffer?.Connected ?? false,
        mqttSpoolDepth = buffer?.Depth ?? 0,
        mqttSpoolDropped = buffer?.Dropped ?? 0,
        opcUaEnabled = app.Services.GetService<IOpcUaControl>()?.Current.Enabled ?? false,
        opcUaListening = app.Services.GetService<IOpcUaControl>()?.Current.Listening ?? false
    }, StudioJson.Options);
});
app.MapGet("/api/contract/v1", () =>
{
    var directory = ContractDirectory();
    var names = directory is null
        ? []
        : Directory.EnumerateFiles(directory, "*.schema.json").Select(Path.GetFileName).Order(StringComparer.Ordinal).ToList();
    return Results.Json(new { schema = "northbound/1.0", files = names }, StudioJson.Options);
});
app.MapGet("/api/contract/v1/{name}", (string name) =>
{
    if (name.Contains("..", StringComparison.Ordinal) || name.IndexOfAny(['/', '\\']) >= 0 || !name.EndsWith(".schema.json", StringComparison.Ordinal))
    {
        return Results.NotFound();
    }

    var directory = ContractDirectory();
    var path = directory is null ? null : Path.Combine(directory, name);
    return path is not null && File.Exists(path) ? Results.File(path, "application/schema+json") : Results.NotFound();
});
app.MapGet("/api/query/v1/openapi.json", () =>
{
    var directory = ContractDirectory();
    var path = directory is null ? null : Path.Combine(directory, "openapi.json");
    return path is not null && File.Exists(path) ? Results.File(path, "application/json") : Results.NotFound();
});
app.MapStudioApi();

if (webRoot is not null)
{
    var index = Path.Combine(webRoot, "index.html");
    app.MapFallback(async context =>
    {
        if (context.Request.Path.StartsWithSegments("/api"))
        {
            context.Response.StatusCode = StatusCodes.Status404NotFound;
            await context.Response.WriteAsJsonAsync(
                new ApiError { Code = "not_found", Message = "未知接口" },
                StudioJson.Options);
            return;
        }

        context.Response.ContentType = "text/html; charset=utf-8";
        await context.Response.SendFileAsync(index);
    });
}

var logger = app.Logger;
logger.LogInformation(
    "iot-daq-gateway {Version} os={OS} rid={Rid} process={Bitness}",
    HostInfo.Version,
    RuntimeInformation.OSDescription,
    RuntimeInformation.RuntimeIdentifier,
    Environment.Is64BitProcess ? "x64" : "x86");
logger.LogInformation("Config data directory {DataDirectory}", dataDirectory);
logger.LogInformation("Database provider {Provider}", store.Database.Provider);
if (!acquisitionOn)
{
    logger.LogInformation("Acquisition is off");
}
else
{
    logger.LogInformation("Acquisition reads the published configuration from the database");
    if (importPath is not null)
    {
        logger.LogInformation(
            "GATEWAY_CONFIG or --config {Path} is imported only when the database has no published configuration yet",
            importPath);
    }
}

if (webRoot is not null)
{
    logger.LogInformation("Serving Web from {WebRoot}", webRoot);
}

logger.LogInformation("Account mode {Mode}", accounts.Mode);
if (accounts.ModeMismatch)
{
    logger.LogWarning(
        "Account file mode does not match {Env} or Studio:AccountMode. Delete data/auth to recreate accounts.",
        AccountStore.AccountModeEnvironmentVariable);
}

if (File.Exists(accounts.BootstrapPasswordPath))
{
    logger.LogWarning("One-time login passwords are in {Path}", accounts.BootstrapPasswordPath);
}

if (app.Services.GetService<IProgramService>() is { } programs)
{
    logger.LogInformation("Program transfer feature flag: {Enabled}", programs.IsEnabled);
}

app.Run();

static string? ContractDirectory()
{
    var output = Path.Combine(AppContext.BaseDirectory, "contract");
    if (Directory.Exists(output) && Directory.EnumerateFiles(output, "*.schema.json").Any())
    {
        return output;
    }

    var dir = new DirectoryInfo(AppContext.BaseDirectory);
    while (dir is not null)
    {
        var candidate = Path.Combine(dir.FullName, "docs", "contract");
        if (Directory.Exists(candidate))
        {
            return candidate;
        }

        dir = dir.Parent;
    }

    return Directory.Exists(output) ? output : null;
}

public partial class Program;
