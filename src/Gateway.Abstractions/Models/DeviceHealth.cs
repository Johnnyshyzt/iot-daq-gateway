namespace Gateway.Abstractions.Models;

public sealed record DeviceHealth
{
    public required string DeviceId { get; init; }

    public required AdapterStatus Status { get; init; }

    public string? Message { get; init; }

    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;
}
