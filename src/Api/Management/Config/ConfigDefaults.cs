using Studio.Contracts;

namespace Studio.Host.Config;

public static class ConfigDefaults
{
    public const string DefaultFanucTemplateId = "fanuc-standard";

    public static ConfigBundle Create()
    {
        return new ConfigBundle
        {
            Gateway = Gateway(),
            Devices = [Device()],
            PointTemplates = [FanucTemplate()],
            PointSets = [],
            Mqtt = Mqtt()
        };
    }

    public static GatewayDocument Gateway() => new()
    {
        ApiVersion = StudioApi.Version,
        Kind = "Gateway",
        Metadata = new GatewayMetadata
        {
            SiteId = "plant-a",
            Name = "Plant A Gateway"
        },
        Spec = new GatewaySpec
        {
            LogLevel = "Information",
            Features = new GatewayFeatures { ProgramWrite = false },
            Acquisition = new AcquisitionSpec
            {
                DefaultIntervalMs = 1000,
                ChangeOnly = true
            }
        }
    };

    public static DeviceDocument Device() => new()
    {
        ApiVersion = StudioApi.Version,
        Kind = "Device",
        Metadata = new DeviceMetadata
        {
            Id = "cnc-01",
            DisplayName = "Lathe 01"
        },
        Spec = new DeviceSpec
        {
            Adapter = "fanuc.fake",
            Enabled = true,
            IntervalMs = 1000,
            PointTemplateId = DefaultFanucTemplateId,
            Connection = new DeviceConnection
            {
                Host = "192.168.1.10",
                Port = 8193,
                FocasTimeoutMs = 3000
            }
        }
    };

    public static PointTemplateDocument FanucTemplate() => new()
    {
        ApiVersion = StudioApi.Version,
        Kind = "PointTemplate",
        Metadata = new PointTemplateMetadata
        {
            Id = DefaultFanucTemplateId,
            DisplayName = "Fanuc 标准三态"
        },
        Spec = new PointTemplateSpec
        {
            Adapter = FanucPointCatalog.Family,
            Points = FanucPointCatalog.DefaultPoints()
        }
    };

    public static PointSetDocument DefaultPoints(string deviceId) => new()
    {
        ApiVersion = StudioApi.Version,
        Kind = "PointSet",
        Metadata = new PointSetMetadata { DeviceId = deviceId },
        Spec = new PointSetSpec
        {
            Points = FanucPointCatalog.DefaultPoints()
        }
    };

    public static PointSetDocument EmptyPoints(string deviceId) => new()
    {
        ApiVersion = StudioApi.Version,
        Kind = "PointSet",
        Metadata = new PointSetMetadata { DeviceId = deviceId },
        Spec = new PointSetSpec()
    };

    public static MqttSinkDocument Mqtt() => new()
    {
        ApiVersion = StudioApi.Version,
        Kind = "MqttSink",
        Metadata = new MqttSinkMetadata { Id = "mqtt-main" },
        Spec = new MqttSinkSpec
        {
            Broker = new MqttBrokerSpec
            {
                Host = "127.0.0.1",
                Port = 1883,
                ClientId = "iot-daq-gateway",
                UsernameFromEnv = "MQTT_USER",
                PasswordFromEnv = "MQTT_PASSWORD"
            },
            TopicTemplate = "daq/{site}/{deviceId}/{point}",
            Qos = 1,
            Retain = false,
            StatusTopic = "daq/{site}/{deviceId}/$status"
        }
    };

}
