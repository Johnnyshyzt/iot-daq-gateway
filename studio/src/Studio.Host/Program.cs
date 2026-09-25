using System.Text.Json;
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

var dataDirectory = ResolveDataDirectory(builder.Environment, builder.Configuration);
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

        var logger = context.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger("Studio.Host");
        logger.LogError(error, "Unhandled studio error");
        context.Response.StatusCode = StatusCodes.Status500InternalServerError;
        await context.Response.WriteAsJsonAsync(
            new ApiError { Code = "internal_error", Message = "服务器内部错误" },
            StudioJson.Options);
    });
});

app.UseCors("studio-dev");

var webDist = Path.GetFullPath(Path.Combine(app.Environment.ContentRootPath, "..", "..", "web", "dist"));
if (Directory.Exists(webDist))
{
    var files = new PhysicalFileProvider(webDist);
    app.UseDefaultFiles(new DefaultFilesOptions { FileProvider = files });
    app.UseStaticFiles(new StaticFileOptions { FileProvider = files });
}

app.UseMiddleware<StudioAuthMiddleware>();
app.MapGet("/healthz", () => Results.Json(new { status = "ok" }, StudioJson.Options));
app.MapStudioApi();

if (Directory.Exists(webDist))
{
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
        await context.Response.SendFileAsync(Path.Combine(webDist, "index.html"));
    });
}

app.Logger.LogInformation("Config Studio data directory {DataDirectory}", dataDirectory);
if (Directory.Exists(webDist))
{
    app.Logger.LogInformation("Serving Studio SPA from {WebDist}", webDist);
}

app.Run();

static string ResolveDataDirectory(IHostEnvironment environment, IConfiguration configuration)
{
    var fromEnv = Environment.GetEnvironmentVariable("STUDIO_DATA");
    if (!string.IsNullOrWhiteSpace(fromEnv))
    {
        return Path.GetFullPath(fromEnv);
    }

    var configured = configuration["Studio:DataDirectory"];
    if (string.IsNullOrWhiteSpace(configured))
    {
        configured = Path.Combine("..", "..", "data");
    }

    return Path.IsPathRooted(configured)
        ? configured
        : Path.GetFullPath(Path.Combine(environment.ContentRootPath, configured));
}

public partial class Program;
