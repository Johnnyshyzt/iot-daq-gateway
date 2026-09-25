using System.Globalization;
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
    private readonly string[]? _points;
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
        _points = ReadPoints(options);
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

        var ids = _points ?? ["state", "alarm", "program"];
        var points = new List<Observation>(ids.Length);
        foreach (var id in ids)
        {
            object? value = id switch
            {
                "state" => state,
                "alarm" => alarm,
                "program" => "O0001",
                _ => 0
            };
            var pointQuality = id is "state" or "alarm" ? quality : "good";
            points.Add(new Observation
            {
                DeviceId = DeviceId,
                Point = id,
                Value = value,
                Timestamp = now,
                Quality = pointQuality
            });
        }

        return Task.FromResult<IReadOnlyList<Observation>>(points);
    }

    private static string[]? ReadPoints(IReadOnlyDictionary<string, object?> options)
    {
        if (!options.TryGetValue("points", out var value) || value is null)
        {
            return null;
        }

        if (value is string text)
        {
            var parts = text.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            return parts.Length == 0 ? null : parts;
        }

        if (value is System.Collections.IEnumerable list and not string)
        {
            var parts = new List<string>();
            foreach (var item in list)
            {
                var part = Convert.ToString(item, CultureInfo.InvariantCulture);
                if (!string.IsNullOrWhiteSpace(part))
                {
                    parts.Add(part);
                }
            }

            return parts.Count == 0 ? null : parts.ToArray();
        }

        return null;
    }

    public Task<DeviceHealth> GetHealthAsync(CancellationToken cancellationToken)
    {
        return Task.FromResult(new DeviceHealth
        {
            DeviceId = DeviceId,
            Status = _connected ? AdapterStatus.Online : AdapterStatus.Offline,
            Message = _connected ? "Fake 适配器已连接" : "Fake 适配器未连接",
            Timestamp = DateTimeOffset.UtcNow
        });
    }

    public ValueTask DisposeAsync()
    {
        _connected = false;
        return ValueTask.CompletedTask;
    }
}
