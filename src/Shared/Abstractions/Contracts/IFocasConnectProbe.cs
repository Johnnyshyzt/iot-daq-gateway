namespace Gateway.Abstractions.Contracts;

/// <summary>
/// One-shot FOCAS Ethernet handshake used by device test. Same library path as collection.
/// A missing or unloadable Fwlib64.dll is a failed probe, never a crash and never a TCP-only success.
/// </summary>
public interface IFocasConnectProbe
{
    FocasConnectProbeResult Probe(string host, int port, int timeoutMs);
}

public sealed class FocasConnectProbeResult
{
    public bool Ok { get; init; }

    /// <summary>
    /// missing_host, invalid_port, unsupported_os, wrong_arch, missing_library, load_failed, connect_failed, or ok.
    /// </summary>
    public string Code { get; init; } = "";

    public string Message { get; init; } = "";
}
