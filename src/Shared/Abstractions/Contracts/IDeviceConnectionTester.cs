namespace Gateway.Abstractions.Contracts;

/// <summary>
/// One-shot connection test for a saved or unsaved device. Does not publish configuration.
/// </summary>
public interface IDeviceConnectionTester
{
    bool CanTest(string? adapterId);

    Task<DeviceConnectionReport> TestAsync(DeviceConnectionRequest request, CancellationToken cancellationToken);
}

public sealed class DeviceConnectionRequest
{
    public string DeviceId { get; init; } = "";

    public string Adapter { get; init; } = "";

    public string? BrandId { get; init; }

    public string? ControllerModelId { get; init; }

    public IReadOnlyDictionary<string, string?> Options { get; init; } =
        new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
}

public sealed class DeviceConnectionReport
{
    public bool Ok { get; init; }

    public bool Reachable { get; init; }

    public bool Handshake { get; init; }

    public int LatencyMs { get; init; }

    public int ReachableMs { get; init; }

    public int HandshakeMs { get; init; }

    public string Message { get; init; } = "";

    public string? Error { get; init; }

    /// <summary>missing, present, or none.</summary>
    public string? SdkStatus { get; init; }

    public IReadOnlyList<DeviceConnectionSample> Samples { get; init; } = [];
}

public sealed class DeviceConnectionSample
{
    public string Point { get; init; } = "";

    public string? Value { get; init; }

    public string Quality { get; init; } = "good";

    public string? Unit { get; init; }
}
