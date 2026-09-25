using System.Runtime.InteropServices;
using System.Text.Json;
using Gateway.Abstractions.Contracts;
using Gateway.Host;
using IotDaq.Host;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.Extensions.FileProviders;
using Studio.Contracts;
using Studio.Host;
using Studio.Host.Auth;
using Studio.Host.Config;
using Studio.Host.Endpoints;
using Studio.Host.Runtime;

var builder = WebApplication.CreateBuilder(args);
builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
    options.SerializerOptions.PropertyNameCaseInsensitive = true;
});
builder.Services.AddWindowsService(options =>
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
var acquisitionOn = !string.Equals(builder.Configuration["Host:Acquisition"], "off", StringComparison.OrdinalIgnoreCase);
string? collectorPath = null;
if (acquisitionOn)
{
    var published = Path.Combine(dataDirectory, "published");
    collectorPath = CollectorHost.ResolveConfigPath(args, published);
    builder.Services.AddCollector(collectorPath);
}

builder.Services.AddSingleton(new ConfigStore(dataDirectory));
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
app.Services.GetRequiredService<ConfigStore>().EnsureInitialized();

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

app.UseMiddleware<StudioAuthMiddleware>();
app.MapGet("/healthz", () => Results.Json(new { status = "ok", acquisition = acquisitionOn }, StudioJson.Options));
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
if (collectorPath is null)
{
    logger.LogInformation("Acquisition is off");
}
else
{
    logger.LogInformation("Acquisition config {Path}", collectorPath);
    if (!CollectorHost.UsesPublishedDirectory(args))
    {
        logger.LogWarning(
            "GATEWAY_CONFIG or --config overrides data/published. UI publish still writes {Published} and reloads the override path.",
            Path.Combine(dataDirectory, "published"));
    }
}

if (webRoot is not null)
{
    logger.LogInformation("Serving Web from {WebRoot}", webRoot);
}

if (app.Services.GetService<IProgramService>() is { } programs)
{
    logger.LogInformation("Program transfer feature flag: {Enabled}", programs.IsEnabled);
}

app.Run();

public partial class Program;
