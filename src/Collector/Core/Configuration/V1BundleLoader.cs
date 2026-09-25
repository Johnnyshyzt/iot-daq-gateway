using System.Globalization;
using Gateway.Abstractions.Configuration;
using YamlDotNet.Serialization;

namespace Gateway.Host.Configuration;

internal static class V1BundleLoader
{
    public static LoadedGateway Load(string directory)
    {
        directory = Path.GetFullPath(directory);
        if (!Directory.Exists(directory))
        {
            throw new DirectoryNotFoundException($"Gateway bundle directory not found: {directory}");
        }

        RejectMappings(directory);

        var gatewayFile = Path.Combine(directory, "gateway.yaml");
        var gateway = ReadMap(gatewayFile);
        RequireKind(gateway, "Gateway", gatewayFile);
        var metadata = ChildMap(gateway, "metadata", gatewayFile);
        var spec = ChildMap(gateway, "spec", gatewayFile);
        var siteId = RequiredString(metadata, "siteId", gatewayFile);
        var name = OptionalString(metadata, "name") ?? siteId;
        var features = ChildMapOrEmpty(spec, "features");
        var acquisition = ChildMapOrEmpty(spec, "acquisition");
        var defaultInterval = OptionalInt(acquisition, "defaultIntervalMs") ?? 1000;
        var changeOnly = OptionalBool(acquisition, "changeOnly") ?? true;
        var programWrite = OptionalBool(features, "programWrite") ?? false;

        var devices = LoadDevices(directory, defaultInterval, out var sweepMs);
        var mqtt = LoadMqtt(directory);

        var configuration = new GatewayConfiguration
        {
            Gateway = new GatewayIdentity
            {
                Id = "gw-" + siteId,
                Site = siteId
            },
            Pipeline = new PipelineOptions
            {
                SweepInterval = TimeSpan.FromMilliseconds(sweepMs),
                ChangeOnly = changeOnly
            },
            ProgramTransfer = new ProgramTransferOptions
            {
                Enabled = programWrite
            },
            Mqtt = mqtt,
            Devices = devices
        };

        var revisionPath = Path.Combine(directory, ".revision");
        var revision = File.Exists(revisionPath) ? File.ReadAllText(revisionPath).Trim() : null;
        if (string.IsNullOrWhiteSpace(revision))
        {
            revision = null;
        }

        return new LoadedGateway(configuration, directory, revision, name, IsBundle: true);
    }

    private static List<DeviceBinding> LoadDevices(string directory, int defaultInterval, out int sweepMs)
    {
        var deviceDir = Path.Combine(directory, "devices");
        var files = Directory.Exists(deviceDir)
            ? Directory.GetFiles(deviceDir, "*.yaml").OrderBy(path => path, StringComparer.Ordinal).ToArray()
            : [];
        if (files.Length == 0)
        {
            throw new InvalidOperationException($"{directory}: devices/ must contain at least one device.");
        }

        var devices = new List<DeviceBinding>();
        var intervals = new List<int>();
        foreach (var file in files)
        {
            var document = ReadMap(file);
            RequireKind(document, "Device", file);
            var metadata = ChildMap(document, "metadata", file);
            var spec = ChildMap(document, "spec", file);
            var id = RequiredString(metadata, "id", file);
            var enabled = OptionalBool(spec, "enabled") ?? true;
            var adapter = RequiredString(spec, "adapter", file);
            var connection = ChildMapOrEmpty(spec, "connection");
            var interval = Math.Max(100, OptionalInt(spec, "intervalMs") ?? defaultInterval);
            if (enabled)
            {
                intervals.Add(interval);
            }

            var options = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
            {
                ["host"] = OptionalString(connection, "host") ?? "127.0.0.1",
                ["port"] = OptionalInt(connection, "port") ?? 8193,
                ["timeoutMs"] = OptionalInt(connection, "focasTimeoutMs") ?? 3000,
                ["displayName"] = OptionalString(metadata, "displayName") ?? id
            };

            var points = EnabledPointIds(directory, id);
            if (points.Count > 0)
            {
                options["points"] = string.Join(',', points);
            }

            devices.Add(new DeviceBinding
            {
                Id = id,
                Enabled = enabled,
                Adapter = adapter,
                Options = options
            });
        }

        sweepMs = intervals.Count > 0 ? intervals.Min() : Math.Max(100, defaultInterval);
        return devices;
    }

    private static List<string> EnabledPointIds(string directory, string deviceId)
    {
        var file = Path.Combine(directory, "points", deviceId + ".yaml");
        if (!File.Exists(file))
        {
            return [];
        }

        var document = ReadMap(file);
        RequireKind(document, "PointSet", file);
        var spec = ChildMap(document, "spec", file);
        if (!spec.TryGetValue("points", out var raw) || raw is not System.Collections.IEnumerable list)
        {
            return [];
        }

        var ids = new List<string>();
        foreach (var item in list)
        {
            var point = AsMap(item, file);
            if (OptionalBool(point, "enabled") == false)
            {
                continue;
            }

            var id = OptionalString(point, "id");
            if (!string.IsNullOrWhiteSpace(id))
            {
                ids.Add(id);
            }
        }

        return ids;
    }

    private static MqttOptions LoadMqtt(string directory)
    {
        var file = Path.Combine(directory, "sinks", "mqtt.yaml");
        var document = ReadMap(file);
        RequireKind(document, "MqttSink", file);
        var spec = ChildMap(document, "spec", file);
        var broker = ChildMap(spec, "broker", file);
        var host = RequiredString(broker, "host", file);
        var options = new MqttOptions
        {
            Host = host,
            Port = OptionalInt(broker, "port") ?? 1883,
            ClientId = OptionalString(broker, "clientId") ?? "iot-daq-gateway",
            Qos = OptionalInt(spec, "qos") ?? 1,
            Tls = OptionalBool(broker, "tls") ?? false,
            Retain = OptionalBool(spec, "retain") ?? false,
            TopicTemplate = OptionalString(spec, "topicTemplate") ?? "daq/{site}/{deviceId}/{point}",
            StatusTopic = OptionalString(spec, "statusTopic") ?? "daq/{site}/{deviceId}/$status",
            Username = ResolveEnv(OptionalString(broker, "usernameFromEnv")),
            Password = ResolveEnv(OptionalString(broker, "passwordFromEnv"))
        };
        return options;
    }

    private static string? ResolveEnv(string? variable)
    {
        if (string.IsNullOrWhiteSpace(variable))
        {
            return null;
        }

        var value = Environment.GetEnvironmentVariable(variable);
        return string.IsNullOrEmpty(value) ? null : value;
    }

    private static void RejectMappings(string directory)
    {
        var mappings = Path.Combine(directory, "mappings");
        if (!Directory.Exists(mappings))
        {
            return;
        }

        var extra = Directory.EnumerateFiles(mappings, "*", SearchOption.AllDirectories)
            .Any(path => !Path.GetFileName(path).StartsWith('.'));
        if (extra)
        {
            throw new InvalidOperationException($"{directory}: mappings/ is not supported in M1.");
        }
    }

    private static Dictionary<string, object?> ReadMap(string path)
    {
        if (!File.Exists(path))
        {
            throw new FileNotFoundException($"Gateway bundle file not found: {path}", path);
        }

        var yaml = File.ReadAllText(path);
        var deserializer = new DeserializerBuilder().Build();
        var node = deserializer.Deserialize<object?>(yaml);
        return AsMap(node, path);
    }

    private static Dictionary<string, object?> AsMap(object? node, string path)
    {
        if (node is IDictionary<string, object> typed)
        {
            return typed.ToDictionary(pair => pair.Key, pair => (object?)pair.Value, StringComparer.OrdinalIgnoreCase);
        }

        if (node is System.Collections.IDictionary dictionary)
        {
            var map = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
            foreach (System.Collections.DictionaryEntry entry in dictionary)
            {
                var key = Convert.ToString(entry.Key, CultureInfo.InvariantCulture);
                if (!string.IsNullOrEmpty(key))
                {
                    map[key] = entry.Value;
                }
            }

            return map;
        }

        throw new InvalidOperationException($"{path}: expected a YAML mapping.");
    }

    private static Dictionary<string, object?> ChildMap(Dictionary<string, object?> map, string key, string path)
    {
        if (!map.TryGetValue(key, out var value) || value is null)
        {
            throw new InvalidOperationException($"{path}: {key} is required.");
        }

        return AsMap(value, path);
    }

    private static Dictionary<string, object?> ChildMapOrEmpty(Dictionary<string, object?> map, string key)
    {
        if (!map.TryGetValue(key, out var value) || value is null)
        {
            return new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        }

        return AsMap(value, key);
    }

    private static void RequireKind(Dictionary<string, object?> map, string kind, string path)
    {
        var actual = OptionalString(map, "kind");
        if (!string.Equals(actual, kind, StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"{path}: kind must be {kind}.");
        }
    }

    private static string RequiredString(Dictionary<string, object?> map, string key, string path)
    {
        var value = OptionalString(map, key);
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException($"{path}: {key} is required.");
        }

        return value;
    }

    private static string? OptionalString(Dictionary<string, object?> map, string key)
    {
        if (!map.TryGetValue(key, out var value) || value is null)
        {
            return null;
        }

        return value as string ?? Convert.ToString(value, CultureInfo.InvariantCulture);
    }

    private static int? OptionalInt(Dictionary<string, object?> map, string key)
    {
        if (!map.TryGetValue(key, out var value) || value is null)
        {
            return null;
        }

        return value switch
        {
            int number => number,
            long number => checked((int)number),
            short number => number,
            string text when int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) => parsed,
            _ => null
        };
    }

    private static bool? OptionalBool(Dictionary<string, object?> map, string key)
    {
        if (!map.TryGetValue(key, out var value) || value is null)
        {
            return null;
        }

        return value switch
        {
            bool flag => flag,
            string text when bool.TryParse(text, out var parsed) => parsed,
            _ => null
        };
    }
}
