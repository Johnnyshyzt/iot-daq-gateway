namespace Gateway.Abstractions.Models;

/// <summary>
/// One southbound sample ready for northbound publish.
/// </summary>
public sealed record Observation
{
    public required string DeviceId { get; init; }

    public required string Point { get; init; }

    public object? Value { get; init; }

    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;

    public string Quality { get; init; } = "good";

    public string? Unit { get; init; }
}
