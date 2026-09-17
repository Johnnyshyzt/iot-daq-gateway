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

        Normalize(config);
        Validate(config, path);
        return config;
    }

    public static void Save(string path, GatewayConfiguration config)
    {
        Normalize(config);
        Validate(config, path);

        var yaml = Serialize(config);
        var fullPath = Path.GetFullPath(path);
        var directory = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var temp = fullPath + ".tmp";
        File.WriteAllText(temp, yaml);
        File.Move(temp, fullPath, overwrite: true);
    }

    public static string Serialize(GatewayConfiguration config)
    {
        Normalize(config);
        var serializer = new SerializerBuilder()
            .WithNamingConvention(CamelCaseNamingConvention.Instance)
            .ConfigureDefaultValuesHandling(DefaultValuesHandling.OmitNull)
            .WithIndentedSequences()
            .Build();

        var body = serializer.Serialize(config).TrimEnd() + Environment.NewLine;
        return
            "# 采集网关配置。Web 控制台会写回本文件，改完不必重新打包。" + Environment.NewLine +
            "# 也可手工编辑；保存并应用或重启进程后生效。" + Environment.NewLine +
            body;
    }

    internal static void Validate(GatewayConfiguration config, string path)
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

        if (config.Mqtt.Port is < 1 or > 65535)
        {
            throw new InvalidOperationException($"{path}: mqtt.port must be 1-65535.");
        }

        if (config.Pipeline.SweepInterval <= TimeSpan.Zero)
        {
            throw new InvalidOperationException($"{path}: pipeline.sweepInterval must be greater than zero.");
        }

        if (config.Console.Port is < 1 or > 65535)
        {
            throw new InvalidOperationException($"{path}: console.port must be 1-65535.");
        }

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var device in config.Devices)
        {
            if (string.IsNullOrWhiteSpace(device.Id))
            {
                throw new InvalidOperationException($"{path}: each device needs an id.");
            }

            if (!seen.Add(device.Id.Trim()))
            {
                throw new InvalidOperationException($"{path}: duplicate device id '{device.Id}'.");
            }

            if (string.IsNullOrWhiteSpace(device.Adapter))
            {
                throw new InvalidOperationException($"{path}: device {device.Id} needs an adapter kind.");
            }
        }
    }

    private static void Normalize(GatewayConfiguration config)
    {
        if (string.IsNullOrWhiteSpace(config.Mqtt.Username))
        {
            config.Mqtt.Username = null;
        }

        if (string.IsNullOrWhiteSpace(config.Mqtt.Password))
        {
            config.Mqtt.Password = null;
        }

        if (string.IsNullOrWhiteSpace(config.Console.Token))
        {
            config.Console.Token = null;
        }

        foreach (var device in config.Devices)
        {
            device.Options = NormalizeOptions(device.Options);
        }
    }

    private static Dictionary<string, object?> NormalizeOptions(Dictionary<string, object?> options)
    {
        var normalized = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        foreach (var pair in options)
        {
            normalized[pair.Key] = UnwrapYamlScalar(pair.Value);
        }

        return normalized;
    }

    private static object? UnwrapYamlScalar(object? value)
    {
        if (value is null)
        {
            return null;
        }

        // YamlDotNet may box integers as long.
        if (value is long l and >= int.MinValue and <= int.MaxValue)
        {
            return (int)l;
        }

        return value;
    }
}
