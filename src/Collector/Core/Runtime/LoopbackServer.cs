using System.Net;
using System.Text.Json;
using Gateway.Host.Acquisition;
using Gateway.Host.Logging;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Gateway.Host.Runtime;

internal sealed class LoopbackServer(
    LiveGateway gateway,
    IConfiguration configuration,
    ILogger<LoopbackServer> logger) : BackgroundService
{
    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public static string? ResolveUrl(IConfiguration configuration)
    {
        var raw = Environment.GetEnvironmentVariable("GATEWAY_LOOPBACK")
            ?? configuration["Gateway:Loopback"]
            ?? "http://127.0.0.1:5081";
        if (string.IsNullOrWhiteSpace(raw) || raw.Equals("off", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return raw.TrimEnd('/');
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var url = ResolveUrl(configuration);
        if (url is null)
        {
            logger.LogInformation("Gateway loopback API disabled");
            return;
        }

        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)
            || uri.Host is not ("127.0.0.1" or "localhost"))
        {
            logger.LogError("Gateway loopback must bind to 127.0.0.1 or localhost. Ignoring {Url}", url);
            return;
        }

        using var listener = new HttpListener();
        listener.Prefixes.Add(url + "/");
        try
        {
            listener.Start();
        }
        catch (Exception ex) when (ex is HttpListenerException or InvalidOperationException)
        {
            logger.LogWarning(ex, "Gateway loopback did not bind {Url}; acquisition continues", url);
            return;
        }

        logger.LogInformation("Gateway loopback API {Url}", url);
        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                var context = await listener.GetContextAsync().WaitAsync(stoppingToken).ConfigureAwait(false);
                _ = Task.Run(() => HandleAsync(context), CancellationToken.None);
            }
        }
        catch (OperationCanceledException)
        {
            // Host is stopping.
        }
        finally
        {
            listener.Stop();
        }
    }

    private async Task HandleAsync(HttpListenerContext context)
    {
        try
        {
            var path = context.Request.Url?.AbsolutePath ?? "";
            var method = context.Request.HttpMethod;
            if (method == "GET" && path == "/api/v1/runtime/status")
            {
                await WriteAsync(context, 200, gateway.StatusDocument()).ConfigureAwait(false);
                return;
            }

            if (method == "GET" && path == "/api/v1/runtime/observations")
            {
                var query = context.Request.QueryString;
                var limit = int.TryParse(query["limit"], out var parsed) ? parsed : 50;
                await WriteAsync(context, 200, gateway.ObservationsDocument(query["deviceId"], limit)).ConfigureAwait(false);
                return;
            }

            if (method == "GET" && path == "/api/v1/runtime/logs/tail")
            {
                var lines = int.TryParse(context.Request.QueryString["lines"], out var parsed) ? parsed : 200;
                lines = Math.Clamp(lines, 1, 2000);
                await WriteAsync(context, 200, new { lines = TailLogs(lines) }).ConfigureAwait(false);
                return;
            }

            if (method == "POST" && path == "/api/v1/runtime/reload")
            {
                var reloaded = await gateway.TryReloadAsync(CancellationToken.None).ConfigureAwait(false);
                await WriteAsync(context, 200, new { reloaded }).ConfigureAwait(false);
                return;
            }

            await WriteAsync(context, 404, new { code = "not_found", message = "未知接口" }).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "Loopback request failed");
            try
            {
                context.Response.StatusCode = 500;
                context.Response.Close();
            }
            catch (Exception closeEx)
            {
                logger.LogDebug(closeEx, "Loopback response close failed");
            }
        }
    }

    private static async Task WriteAsync(HttpListenerContext context, int status, object body)
    {
        var json = JsonSerializer.SerializeToUtf8Bytes(body, Json);
        context.Response.StatusCode = status;
        context.Response.ContentType = "application/json; charset=utf-8";
        context.Response.ContentLength64 = json.Length;
        await context.Response.OutputStream.WriteAsync(json).ConfigureAwait(false);
        context.Response.Close();
    }

    private static List<string> TailLogs(int lines) => GatewayLogFiles.Tail(lines);
}
