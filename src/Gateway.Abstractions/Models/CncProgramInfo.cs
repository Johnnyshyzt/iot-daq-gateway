namespace Gateway.Abstractions.Models;

/// <summary>
/// Read-only CNC program metadata. Write/transfer is reserved for a later version.
/// </summary>
public sealed record CncProgramInfo
{
    public required string DeviceId { get; init; }

    public required string ProgramId { get; init; }

    public string? Comment { get; init; }
}
