using Studio.Contracts;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace Studio.Host.Config;

public static class YamlFiles
{
    private static readonly ISerializer Serializer = new SerializerBuilder()
        .WithNamingConvention(CamelCaseNamingConvention.Instance)
        .ConfigureDefaultValuesHandling(DefaultValuesHandling.OmitNull)
        .DisableAliases()
        .Build();

    private static readonly IDeserializer Deserializer = new DeserializerBuilder()
        .WithNamingConvention(CamelCaseNamingConvention.Instance)
        .IgnoreUnmatchedProperties()
        .Build();

    public static T Read<T>(string path)
    {
        if (!File.Exists(path))
        {
            throw new ConfigStoreException("config_unreadable", $"找不到配置文件 {path}", StatusCodes.Status500InternalServerError);
        }

        try
        {
            var yaml = File.ReadAllText(path);
            var document = Deserializer.Deserialize<T>(yaml);
            if (document is null)
            {
                throw new InvalidOperationException("YAML deserialized to null.");
            }

            return document;
        }
        catch (ConfigStoreException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new ConfigStoreException(
                "config_unreadable",
                $"无法解析 {path}: {ex.Message}",
                StatusCodes.Status500InternalServerError);
        }
    }

    public static void Write<T>(string path, T document)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var yaml = Serializer.Serialize(document).Replace("\r\n", "\n", StringComparison.Ordinal);
        if (!yaml.EndsWith('\n'))
        {
            yaml += "\n";
        }

        File.WriteAllText(path, yaml);
    }

    public static ConfigBundle Normalize(ConfigBundle bundle)
    {
        bundle.Gateway ??= ConfigDefaults.Gateway();
        bundle.Gateway.Metadata ??= new GatewayMetadata();
        bundle.Gateway.Spec ??= new GatewaySpec();
        bundle.Gateway.Spec.Features ??= new GatewayFeatures();
        bundle.Gateway.Spec.Acquisition ??= new AcquisitionSpec();
        bundle.Devices ??= [];
        bundle.PointSets ??= [];
        bundle.Mqtt ??= ConfigDefaults.Mqtt();
        bundle.Mqtt.Metadata ??= new MqttSinkMetadata();
        bundle.Mqtt.Spec ??= new MqttSinkSpec();
        bundle.Mqtt.Spec.Broker ??= new MqttBrokerSpec();

        foreach (var device in bundle.Devices)
        {
            device.Metadata ??= new DeviceMetadata();
            device.Spec ??= new DeviceSpec();
            device.Spec.Connection ??= new DeviceConnection();
        }

        foreach (var points in bundle.PointSets)
        {
            points.Metadata ??= new PointSetMetadata();
            points.Spec ??= new PointSetSpec();
            points.Spec.Points ??= [];
        }

        return bundle;
    }
}
