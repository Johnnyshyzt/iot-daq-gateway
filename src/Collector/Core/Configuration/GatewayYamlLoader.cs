using Gateway.Abstractions.Configuration;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace Gateway.Host.Configuration;

internal static class GatewayYamlLoader
{
    public static GatewayConfiguration Load(string path)
    {
        if (!File.Exists(path))
        {
            throw new FileNotFoundException($"Gateway config not found: {path}", path);
        }

        var yaml = File.ReadAllText(path);
        var deserializer = new DeserializerBuilder()
            .WithNamingConvention(CamelCaseNamingConvention.Instance)
            .IgnoreUnmatchedProperties()
            .Build();

        var config = deserializer.Deserialize<GatewayConfiguration>(yaml)
            ?? throw new InvalidOperationException($"YAML deserialized to null: {path}");

        Validate(config, path);
        return config;
    }

    private static void Validate(GatewayConfiguration config, string path)
    {
        if (string.IsNullOrWhiteSpace(config.Gateway.Id))
        {
            throw new InvalidOperationException($"{path}: gateway.id is required.");
        }

        if (string.IsNullOrWhiteSpace(config.Gateway.Site))
        {
            throw new InvalidOperationException($"{path}: gateway.site is required.");
        }

        if (string.IsNullOrWhiteSpace(config.Mqtt.Host))
        {
            throw new InvalidOperationException($"{path}: mqtt.host is required.");
        }

        if (config.Devices.Count == 0)
        {
            throw new InvalidOperationException($"{path}: devices must contain at least one entry.");
        }

        foreach (var device in config.Devices)
        {
            if (string.IsNullOrWhiteSpace(device.Id))
            {
                throw new InvalidOperationException($"{path}: each device needs an id.");
            }

            if (string.IsNullOrWhiteSpace(device.Adapter))
            {
                throw new InvalidOperationException($"{path}: device {device.Id} needs an adapter kind.");
            }
        }
    }
}
