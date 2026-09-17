using Gateway.Abstractions.Contracts;
using Gateway.Abstractions.Models;
using Microsoft.Extensions.Logging;

namespace Adapters.Fanuc.Focas;

/// <summary>
/// Real FOCAS adapter skeleton. Collect is stubbed until user-supplied
/// native libraries are wired through <see cref="FocasNative"/>.
/// </summary>
public sealed class FocasFanucAdapter : ISouthboundAdapter
{
    public const string Kind = "fanuc.focas";

    private readonly ILogger<FocasFanucAdapter> _logger;
    private readonly string _host;
    private readonly int _port;
    private readonly int _timeoutMs;
    private string _lastError;

    public FocasFanucAdapter(
        string deviceId,
        IReadOnlyDictionary<string, object?> options,
        ILogger<FocasFanucAdapter> logger)
    {
        DeviceId = deviceId;
        _logger = logger;
        _host = OptionReader.GetString(options, "host", "192.168.1.10");
        _port = OptionReader.GetInt(options, "port", 8193);
        _timeoutMs = OptionReader.GetInt(options, "timeoutMs", 3000);
        _lastError = "not connected";
    }

    public string AdapterKind => Kind;

    public string DeviceId { get; }

    public Task ConnectAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!FocasNative.TryAllocateHandle(_host, (ushort)_port, _timeoutMs, out _, out var error))
        {
            _lastError = error;
            _logger.LogWarning(
                "Fanuc FOCAS adapter {DeviceId} is a V1 stub ({Host}:{Port}): {Error}",
                DeviceId,
                _host,
                _port,
                error);
            return Task.CompletedTask;
        }

        _lastError = string.Empty;
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<Observation>> CollectAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        // TODO: call cnc_statinfo / cnc_rdalmmsg / cnc_rdprgnum via FocasNative
        // and map results onto Observation points (state, alarm, program).
        _logger.LogDebug(
            "FOCAS collect skipped for {DeviceId}: native library not wired",
            DeviceId);

        return Task.FromResult<IReadOnlyList<Observation>>([]);
    }

    public Task<DeviceHealth> GetHealthAsync(CancellationToken cancellationToken)
    {
        return Task.FromResult(new DeviceHealth
        {
            DeviceId = DeviceId,
            Status = AdapterStatus.Offline,
            Message = string.IsNullOrEmpty(_lastError)
                ? "FOCAS handle allocated (collect still TODO)"
                : _lastError,
            Timestamp = DateTimeOffset.UtcNow
        });
    }

    public ValueTask DisposeAsync()
    {
        // TODO: cnc_freelibhndl when a real handle exists.
        return ValueTask.CompletedTask;
    }
}
