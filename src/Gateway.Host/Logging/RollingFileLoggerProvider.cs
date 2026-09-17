using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace Gateway.Host.Logging;

/// <summary>
/// Date-stamped log files next to the process (or GATEWAY_LOG_DIR) so operators can copy logs off a factory PC.
/// </summary>
internal sealed class RollingFileLoggerProvider : ILoggerProvider
{
    public const string FileNamePrefix = "gateway-";
    public const int RetentionDays = 14;

    private readonly string _directory;
    private readonly object _writeLock = new();
    private readonly ConcurrentDictionary<string, RollingFileLogger> _loggers = new(StringComparer.Ordinal);
    private StreamWriter? _writer;
    private string? _openDate;
    private bool _disposed;

    public RollingFileLoggerProvider(string directory)
    {
        _directory = directory;
        Directory.CreateDirectory(_directory);
        TrimOldFiles(_directory);
    }

    public string DirectoryPath => _directory;

    public ILogger CreateLogger(string categoryName) =>
        _loggers.GetOrAdd(categoryName, name => new RollingFileLogger(this, name));

    public void Dispose()
    {
        lock (_writeLock)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _writer?.Dispose();
            _writer = null;
        }
    }

    internal void Write(string category, LogLevel level, EventId eventId, string message, Exception? exception)
    {
        var line = FormatLine(category, level, eventId, message, exception);
        lock (_writeLock)
        {
            if (_disposed)
            {
                return;
            }

            try
            {
                EnsureWriter();
                _writer!.WriteLine(line);
            }
            catch (Exception)
            {
                // Logging must not take down the gateway.
            }
        }
    }

    internal static string FormatLine(string category, LogLevel level, EventId eventId, string message, Exception? exception)
    {
        var stamp = DateTimeOffset.Now.ToString("yyyy-MM-dd HH:mm:ss.fff zzz");
        var eventPart = eventId.Id == 0 ? string.Empty : $" {eventId.Id}";
        var line = $"{stamp} {level,-11} {category}{eventPart} {message}";
        if (exception is not null)
        {
            line += Environment.NewLine + exception;
        }

        return line;
    }

    internal static void TrimOldFiles(string directory)
    {
        try
        {
            if (!Directory.Exists(directory))
            {
                return;
            }

            var cutoff = DateTime.UtcNow.AddDays(-RetentionDays);
            foreach (var file in Directory.EnumerateFiles(directory, FileNamePrefix + "*.log"))
            {
                try
                {
                    if (File.GetLastWriteTimeUtc(file) < cutoff)
                    {
                        File.Delete(file);
                    }
                }
                catch (Exception)
                {
                    // ignore individual file failures
                }
            }
        }
        catch (Exception)
        {
            // ignore
        }
    }

    private void EnsureWriter()
    {
        var today = DateTime.Now.ToString("yyyyMMdd");
        if (_writer is not null && _openDate == today)
        {
            return;
        }

        _writer?.Dispose();
        _openDate = today;
        var path = Path.Combine(_directory, $"{FileNamePrefix}{today}.log");
        _writer = new StreamWriter(new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.ReadWrite))
        {
            AutoFlush = true
        };
    }

    private sealed class RollingFileLogger : ILogger
    {
        private readonly RollingFileLoggerProvider _provider;
        private readonly string _category;

        public RollingFileLogger(RollingFileLoggerProvider provider, string category)
        {
            _provider = provider;
            _category = category;
        }

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => logLevel != LogLevel.None;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel))
            {
                return;
            }

            _provider.Write(_category, logLevel, eventId, formatter(state, exception), exception);
        }
    }
}
