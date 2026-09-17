using System.Net;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Gateway.Host.Acquisition;
using Gateway.Host.Configuration;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Gateway.Host.Web;

internal sealed class WebConsoleHostedService : BackgroundService
{
    public const string CookieName = "iot_daq_console";

    private readonly PipelineManager _pipeline;
    private readonly ILogger<WebConsoleHostedService> _logger;

    public WebConsoleHostedService(PipelineManager pipeline, ILogger<WebConsoleHostedService> logger)
    {
        _pipeline = pipeline;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var resolved = ConsoleListenPolicy.Resolve(_pipeline.Current.Console);
        if (!resolved.Listen)
        {
            _logger.LogWarning("Web console not started: {Reason}", resolved.SkipReason);
            return;
        }

        WebApplication? app = null;
        try
        {
            var builder = WebApplication.CreateSlimBuilder();
            builder.Logging.ClearProviders();
            builder.Services.ConfigureHttpJsonOptions(options =>
            {
                options.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
                options.SerializerOptions.PropertyNameCaseInsensitive = true;
            });
            builder.WebHost.ConfigureKestrel(options =>
            {
                options.AddServerHeader = false;
                Listen(options, resolved.Bind, resolved.Port);
            });

            app = builder.Build();
            Map(app, resolved);
            await app.StartAsync(stoppingToken).ConfigureAwait(false);
            _logger.LogInformation(
                "Web console listening on http://{Bind}:{Port}/ (authRequired={Auth})",
                resolved.Bind,
                resolved.Port,
                resolved.AuthRequired);
            await Task.Delay(Timeout.Infinite, stoppingToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // host shutting down
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Web console failed to start on {Bind}:{Port}", resolved.Bind, resolved.Port);
        }
        finally
        {
            if (app is not null)
            {
                await app.StopAsync(CancellationToken.None).ConfigureAwait(false);
                await app.DisposeAsync().ConfigureAwait(false);
            }
        }
    }

    private void Map(WebApplication app, ResolvedConsole resolved)
    {
        var html = LoadHtml();

        app.Use(async (ctx, next) =>
        {
            if (IsAnonymous(ctx.Request.Path))
            {
                await next().ConfigureAwait(false);
                return;
            }

            if (!resolved.AuthRequired || TokenMatches(ctx, resolved.Token))
            {
                await next().ConfigureAwait(false);
                return;
            }

            ctx.Response.StatusCode = StatusCodes.Status401Unauthorized;
            await ctx.Response.WriteAsJsonAsync(new { error = "未登录" }).ConfigureAwait(false);
        });

        app.MapGet("/", () => Results.Content(html, "text/html; charset=utf-8"));

        app.MapPost("/api/login", async (HttpContext ctx) =>
        {
            if (!resolved.AuthRequired)
            {
                return Results.Ok(new { ok = true });
            }

            var body = await ctx.Request.ReadFromJsonAsync<LoginRequest>().ConfigureAwait(false);
            if (body is null || !FixedEquals(body.Token, resolved.Token))
            {
                return Results.Json(new { error = "口令不正确" }, statusCode: StatusCodes.Status401Unauthorized);
            }

            ctx.Response.Cookies.Append(CookieName, resolved.Token!, new CookieOptions
            {
                HttpOnly = true,
                SameSite = SameSiteMode.Strict,
                Path = "/",
                MaxAge = TimeSpan.FromHours(12),
                Secure = ctx.Request.IsHttps
            });
            return Results.Ok(new { ok = true });
        });

        app.MapPost("/api/logout", (HttpContext ctx) =>
        {
            ctx.Response.Cookies.Delete(CookieName);
            return Results.Ok(new { ok = true });
        });

        app.MapGet("/api/config", () =>
        {
            var config = _pipeline.Current;
            var document = ConsoleConfigMapper.ToDocument(config);
            return Results.Ok(new ConsoleConfigResponse
            {
                Version = HostInfo.Version,
                ConfigPath = _pipeline.ConfigPath,
                ConsoleBind = resolved.Bind,
                ConsolePort = resolved.Port,
                GatewayId = document.GatewayId,
                Site = document.Site,
                MqttHost = document.MqttHost,
                MqttPort = document.MqttPort,
                MqttClientId = document.MqttClientId,
                SweepIntervalSeconds = document.SweepIntervalSeconds,
                Devices = document.Devices
            });
        });

        app.MapPut("/api/config", (ConsoleConfigDocument document) =>
        {
            try
            {
                var saved = _pipeline.ApplyFromConsole(document);
                return Results.Ok(new { ok = true, deviceCount = saved.Devices.Count });
            }
            catch (Exception ex) when (ex is InvalidOperationException or ArgumentException)
            {
                _logger.LogWarning(ex, "Web console rejected config save");
                return Results.Json(new { error = ex.Message }, statusCode: StatusCodes.Status400BadRequest);
            }
        });
    }

    private static void Listen(Microsoft.AspNetCore.Server.Kestrel.Core.KestrelServerOptions options, string bind, int port)
    {
        if (bind is "*" or "+" or "0.0.0.0")
        {
            options.Listen(IPAddress.Any, port);
            return;
        }

        if (bind is "::" or "[::]")
        {
            options.Listen(IPAddress.IPv6Any, port);
            return;
        }

        if (!IPAddress.TryParse(bind, out var address))
        {
            throw new InvalidOperationException($"Invalid console.bind '{bind}'.");
        }

        options.Listen(address, port);
    }

    private static bool IsAnonymous(PathString path)
    {
        return path == "/" || path == "/api/login" || path == "/favicon.ico";
    }

    private static bool TokenMatches(HttpContext ctx, string? expected)
    {
        if (string.IsNullOrEmpty(expected))
        {
            return true;
        }

        if (ctx.Request.Cookies.TryGetValue(CookieName, out var cookie) && FixedEquals(cookie, expected))
        {
            return true;
        }

        var header = ctx.Request.Headers.Authorization.ToString();
        if (header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)
            && FixedEquals(header["Bearer ".Length..].Trim(), expected))
        {
            return true;
        }

        if (ctx.Request.Headers.TryGetValue("X-Gateway-Token", out var raw)
            && FixedEquals(raw.ToString(), expected))
        {
            return true;
        }

        return false;
    }

    internal static bool FixedEquals(string? left, string? right)
    {
        left ??= string.Empty;
        right ??= string.Empty;
        var a = Encoding.UTF8.GetBytes(left);
        var b = Encoding.UTF8.GetBytes(right);
        if (a.Length != b.Length)
        {
            CryptographicOperations.FixedTimeEquals(a, a);
            return false;
        }

        return CryptographicOperations.FixedTimeEquals(a, b);
    }

    private static string LoadHtml()
    {
        var assembly = typeof(WebConsoleHostedService).Assembly;
        var name = assembly.GetManifestResourceNames()
            .FirstOrDefault(n => n.EndsWith("console.html", StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException("Embedded web console HTML is missing.");

        using var stream = assembly.GetManifestResourceStream(name)
            ?? throw new InvalidOperationException("Embedded web console HTML stream is missing.");
        using var reader = new StreamReader(stream, Encoding.UTF8);
        return reader.ReadToEnd();
    }

    private sealed class LoginRequest
    {
        public string? Token { get; set; }
    }

    private sealed class ConsoleConfigResponse
    {
        public string Version { get; set; } = string.Empty;

        public string ConfigPath { get; set; } = string.Empty;

        public string ConsoleBind { get; set; } = string.Empty;

        public int ConsolePort { get; set; }

        public string GatewayId { get; set; } = string.Empty;

        public string Site { get; set; } = string.Empty;

        public string MqttHost { get; set; } = string.Empty;

        public int MqttPort { get; set; }

        public string MqttClientId { get; set; } = string.Empty;

        public int SweepIntervalSeconds { get; set; }

        public List<ConsoleDeviceDocument> Devices { get; set; } = [];
    }
}
