using Gateway.Abstractions.Contracts;
using Gateway.Abstractions.Models;
using Microsoft.Extensions.Logging;

namespace Adapters.Fanuc.Fake;

/// <summary>
/// In-process Fanuc stand-in that emits sample state / alarm / program points
/// without FOCAS libraries or machine hardware.
/// </summary>
public sealed class FakeFanucAdapter : ISouthboundAdapter
{
    public const string Kind = "fanuc.fake";

    private readonly ILogger<FakeFanucAdapter> _logger;
    private readonly string _host;
    private readonly int _port;
    private bool _connected;

    public FakeFanucAdapter(
        string deviceId,
        IReadOnlyDictionary<string, object?> options,
        ILogger<FakeFanucAdapter> logger)
    {
        DeviceId = deviceId;
        _logger = logger;
        _host = OptionReader.GetString(options, "host", "127.0.0.1");
        _port = OptionReader.GetInt(options, "port", 8193);
    }

    public string AdapterKind => Kind;

    public string DeviceId { get; }

    public Task ConnectAsync(CancellationToken cancellationToken)
    {
        _connected = true;
        _logger.LogInformation(
            "Fake Fanuc adapter {DeviceId} ready (simulated {Host}:{Port})",
            DeviceId,
            _host,
            _port);
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<Observation>> CollectAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!_connected)
        {
            throw new InvalidOperationException($"Adapter {DeviceId} is not connected.");
        }

        var now = DateTimeOffset.UtcNow;
        var phase = now.ToUnixTimeSeconds() % 60;
        var (state, alarm, quality) = phase switch
        {
            < 20 => ("IDLE", 0, "good"),
            < 50 => ("RUNNING", 0, "good"),
            _ => ("ALARM", 100, "uncertain")
        };

        IReadOnlyList<Observation> points =
        [
            new Observation
            {
                DeviceId = DeviceId,
                Point = "state",
                Value = state,
                Timestamp = now,
                Quality = quality
            },
            new Observation
            {
                DeviceId = DeviceId,
                Point = "alarm",
                Value = alarm,
                Timestamp = now,
                Quality = quality
            },
            new Observation
            {
                DeviceId = DeviceId,
                Point = "program",
                Value = "O0001",
                Timestamp = now,
                Quality = "good"
            }
        ];

        return Task.FromResult(points);
    }

    public Task<DeviceHealth> GetHealthAsync(CancellationToken cancellationToken)
    {
        return Task.FromResult(new DeviceHealth
        {
            DeviceId = DeviceId,
            Status = _connected ? AdapterStatus.Online : AdapterStatus.Offline,
            Message = _connected ? "fake adapter" : "not connected",
            Timestamp = DateTimeOffset.UtcNow
        });
    }

    public ValueTask DisposeAsync()
    {
        _connected = false;
        return ValueTask.CompletedTask;
    }
}
