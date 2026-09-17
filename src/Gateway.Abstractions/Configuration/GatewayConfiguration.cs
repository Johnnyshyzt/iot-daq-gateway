namespace Gateway.Abstractions.Configuration;

public sealed class GatewayConfiguration
{
    public GatewayIdentity Gateway { get; set; } = new();

    public PipelineOptions Pipeline { get; set; } = new();

    public ProgramTransferOptions ProgramTransfer { get; set; } = new();

    public MqttOptions Mqtt { get; set; } = new();

    public ConsoleOptions Console { get; set; } = new();

    public List<DeviceBinding> Devices { get; set; } = [];
}

public sealed class GatewayIdentity
{
    public string Id { get; set; } = "gw-unspecified";

    public string Site { get; set; } = "default";
}

public sealed class PipelineOptions
{
    public TimeSpan SweepInterval { get; set; } = TimeSpan.FromSeconds(2);

    public bool ChangeOnly { get; set; } = true;
}

public sealed class ProgramTransferOptions
{
    /// <summary>V1 default is false. Transfer is not implemented even when enabled.</summary>
    public bool Enabled { get; set; }
}

public sealed class MqttOptions
{
    public string Host { get; set; } = "127.0.0.1";

    public int Port { get; set; } = 1883;

    public string ClientId { get; set; } = "iot-daq-gateway";

    public string? Username { get; set; }

    public string? Password { get; set; }

    public int Qos { get; set; } = 1;

    public bool Tls { get; set; }
}

public sealed class DeviceBinding
{
    public string Id { get; set; } = string.Empty;

    public bool Enabled { get; set; } = true;

    public string Adapter { get; set; } = "fanuc.fake";

    public Dictionary<string, object?> Options { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

/// <summary>
/// Built-in Chinese web console (not a separate SaaS). YAML remains the source of truth.
/// </summary>
public sealed class ConsoleOptions
{
    public bool Enabled { get; set; } = true;

    /// <summary>Listen address. Loopback is the safe default; use 0.0.0.0 only with a token.</summary>
    public string Bind { get; set; } = "127.0.0.1";

    public int Port { get; set; } = 8080;

    /// <summary>Shared access token. Override with GATEWAY_CONSOLE_TOKEN. Required when not loopback.</summary>
    public string? Token { get; set; }
}
