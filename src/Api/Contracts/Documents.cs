namespace Studio.Contracts;

public static class StudioApi
{
    public const string Version = "daq.gateway/v1";
}

public sealed class ConfigBundle
{
    public GatewayDocument Gateway { get; set; } = new();

    public List<DeviceDocument> Devices { get; set; } = [];

    public List<PointSetDocument> PointSets { get; set; } = [];

    public MqttSinkDocument Mqtt { get; set; } = new();
}

public sealed class GatewayDocument
{
    public string ApiVersion { get; set; } = StudioApi.Version;

    public string Kind { get; set; } = "Gateway";

    public GatewayMetadata Metadata { get; set; } = new();

    public GatewaySpec Spec { get; set; } = new();
}

public sealed class GatewayMetadata
{
    public string SiteId { get; set; } = "";

    public string Name { get; set; } = "";
}

public sealed class GatewaySpec
{
    public string LogLevel { get; set; } = "Information";

    public GatewayFeatures Features { get; set; } = new();

    public AcquisitionSpec Acquisition { get; set; } = new();
}

public sealed class GatewayFeatures
{
    public bool ProgramWrite { get; set; }
}

public sealed class AcquisitionSpec
{
    public int DefaultIntervalMs { get; set; } = 1000;

    public bool ChangeOnly { get; set; } = true;
}

public sealed class DeviceDocument
{
    public string ApiVersion { get; set; } = StudioApi.Version;

    public string Kind { get; set; } = "Device";

    public DeviceMetadata Metadata { get; set; } = new();

    public DeviceSpec Spec { get; set; } = new();
}

public sealed class DeviceMetadata
{
    public string Id { get; set; } = "";

    public string DisplayName { get; set; } = "";
}

public sealed class DeviceSpec
{
    public string Adapter { get; set; } = "fanuc.fake";

    public bool Enabled { get; set; } = true;

    public int IntervalMs { get; set; } = 1000;

    public DeviceConnection Connection { get; set; } = new();
}

public sealed class DeviceConnection
{
    public string Host { get; set; } = "";

    public int Port { get; set; } = 8193;

    public int? FocasTimeoutMs { get; set; }
}

public sealed class PointSetDocument
{
    public string ApiVersion { get; set; } = StudioApi.Version;

    public string Kind { get; set; } = "PointSet";

    public PointSetMetadata Metadata { get; set; } = new();

    public PointSetSpec Spec { get; set; } = new();
}

public sealed class PointSetMetadata
{
    public string DeviceId { get; set; } = "";
}

public sealed class PointSetSpec
{
    public List<PointDefinition> Points { get; set; } = [];
}

public sealed class PointDefinition
{
    public string Id { get; set; } = "";

    public string Address { get; set; } = "";

    public string DataType { get; set; } = "string";

    public string Unit { get; set; } = "";

    public double Scale { get; set; } = 1;

    public double Deadband { get; set; }

    public bool Enabled { get; set; } = true;
}

public sealed class MqttSinkDocument
{
    public string ApiVersion { get; set; } = StudioApi.Version;

    public string Kind { get; set; } = "MqttSink";

    public MqttSinkMetadata Metadata { get; set; } = new();

    public MqttSinkSpec Spec { get; set; } = new();
}

public sealed class MqttSinkMetadata
{
    public string Id { get; set; } = "mqtt-main";
}

public sealed class MqttSinkSpec
{
    public MqttBrokerSpec Broker { get; set; } = new();

    public string TopicTemplate { get; set; } = "daq/{site}/{deviceId}/{point}";

    public int Qos { get; set; } = 1;

    public bool Retain { get; set; }

    public string StatusTopic { get; set; } = "daq/{site}/{deviceId}/$status";
}

public sealed class MqttBrokerSpec
{
    public string Host { get; set; } = "127.0.0.1";

    public int Port { get; set; } = 1883;

    public string ClientId { get; set; } = "iot-daq-gateway";

    public string? UsernameFromEnv { get; set; }

    public string? PasswordFromEnv { get; set; }

    public bool Tls { get; set; }
}
