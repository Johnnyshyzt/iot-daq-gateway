using Gateway.Host.Acquisition;
using Gateway.Host.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Gateway.Host.Runtime;

internal sealed class ConfigReloadWatcher(GatewayConfigSource source, LiveGateway gateway, ILogger<ConfigReloadWatcher> logger)
    : BackgroundService
{
    private CancellationTokenSource _pending = new();

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var fullPath = Path.GetFullPath(source.Path);
        var directory = Directory.Exists(fullPath) ? fullPath : Path.GetDirectoryName(fullPath);
        if (string.IsNullOrEmpty(directory) || !Directory.Exists(directory))
        {
            logger.LogWarning("Config watcher has no directory for {Path}", source.Path);
            return;
        }

        var fileFilter = Directory.Exists(fullPath) ? null : Path.GetFileName(fullPath);
        using var watcher = new FileSystemWatcher(directory)
        {
            IncludeSubdirectories = fileFilter is null,
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.CreationTime
        };

        void Schedule(object _, FileSystemEventArgs args)
        {
            if (fileFilter is not null && !string.Equals(args.Name, fileFilter, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            var next = new CancellationTokenSource();
            var previous = Interlocked.Exchange(ref _pending, next);
            previous.Cancel();
            previous.Dispose();
            _ = DebounceAsync(next.Token);
        }

        watcher.Changed += Schedule;
        watcher.Created += Schedule;
        watcher.Deleted += Schedule;
        watcher.Renamed += Schedule;
        watcher.EnableRaisingEvents = true;
        logger.LogInformation("Watching {Directory} for config reload", directory);

        try
        {
            await Task.Delay(Timeout.Infinite, stoppingToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Host is stopping.
        }
        finally
        {
            _pending.Cancel();
        }
    }

    private async Task DebounceAsync(CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(TimeSpan.FromMilliseconds(450), cancellationToken).ConfigureAwait(false);
            await gateway.TryReloadAsync(CancellationToken.None).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // A newer change replaced this debounce.
        }
    }
}
