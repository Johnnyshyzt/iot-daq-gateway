namespace Gateway.Abstractions.Configuration;

public sealed class GatewayConfiguration
{
    public GatewayIdentity Gateway { get; set; } = new();

    public PipelineOptions Pipeline { get; set; } = new();

    public ProgramTransferOptions ProgramTransfer { get; set; } = new();

    public MqttOptions Mqtt { get; set; } = new();

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

    public bool Retain { get; set; }

    public string TopicTemplate { get; set; } = "daq/{site}/{deviceId}/{point}";

    public string StatusTopic { get; set; } = "daq/{site}/{deviceId}/$status";
}

public sealed class DeviceBinding
{
    public string Id { get; set; } = string.Empty;

    public bool Enabled { get; set; } = true;

    public string Adapter { get; set; } = "fanuc.fake";

    public Dictionary<string, object?> Options { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}
