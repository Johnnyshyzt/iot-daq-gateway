using System.Globalization;
using Gateway.Abstractions.Configuration;

namespace Gateway.Host.Acquisition;

internal sealed class ConsoleConfigDocument
{
    public string GatewayId { get; set; } = string.Empty;

    public string Site { get; set; } = string.Empty;

    public string MqttHost { get; set; } = string.Empty;

    public int MqttPort { get; set; } = 1883;

    public string MqttClientId { get; set; } = string.Empty;

    public int SweepIntervalSeconds { get; set; } = 2;

    public List<ConsoleDeviceDocument> Devices { get; set; } = [];
}

internal sealed class ConsoleDeviceDocument
{
    public string Id { get; set; } = string.Empty;

    public bool Enabled { get; set; } = true;

    public string Adapter { get; set; } = "fanuc.focas";

    public string Host { get; set; } = string.Empty;

    public int Port { get; set; } = 8193;
}

internal static class ConsoleConfigMapper
{
    public const int DefaultDeviceTimeoutMs = 3000;

    public static ConsoleConfigDocument ToDocument(GatewayConfiguration config)
    {
        return new ConsoleConfigDocument
        {
            GatewayId = config.Gateway.Id,
            Site = config.Gateway.Site,
            MqttHost = config.Mqtt.Host,
            MqttPort = config.Mqtt.Port,
            MqttClientId = config.Mqtt.ClientId,
            SweepIntervalSeconds = Math.Max(1, (int)Math.Round(config.Pipeline.SweepInterval.TotalSeconds)),
            Devices = config.Devices.Select(ToDevice).ToList()
        };
    }

    public static GatewayConfiguration Merge(GatewayConfiguration baseline, ConsoleConfigDocument document)
    {
        ArgumentNullException.ThrowIfNull(baseline);
        ArgumentNullException.ThrowIfNull(document);

        var next = Clone(baseline);
        next.Gateway.Id = Require(document.GatewayId, "gateway.id");
        next.Gateway.Site = Require(document.Site, "gateway.site");
        next.Mqtt.Host = Require(document.MqttHost, "mqtt.host");
        next.Mqtt.Port = document.MqttPort;
        next.Mqtt.ClientId = string.IsNullOrWhiteSpace(document.MqttClientId)
            ? next.Gateway.Id
            : document.MqttClientId.Trim();
        next.Pipeline.SweepInterval = TimeSpan.FromSeconds(Math.Max(1, document.SweepIntervalSeconds));

        var previous = baseline.Devices.ToDictionary(d => d.Id, d => d, StringComparer.OrdinalIgnoreCase);
        next.Devices = document.Devices.Select(d => MergeDevice(d, previous)).ToList();
        return next;
    }

    private static ConsoleDeviceDocument ToDevice(DeviceBinding device)
    {
        return new ConsoleDeviceDocument
        {
            Id = device.Id,
            Enabled = device.Enabled,
            Adapter = device.Adapter,
            Host = GetString(device.Options, "host", ""),
            Port = GetInt(device.Options, "port", 8193)
        };
    }

    private static DeviceBinding MergeDevice(
        ConsoleDeviceDocument document,
        IReadOnlyDictionary<string, DeviceBinding> previous)
    {
        var id = Require(document.Id, "devices.id");
        var adapter = Require(document.Adapter, "devices.adapter");
        previous.TryGetValue(id, out var existing);
        var timeoutMs = existing is null
            ? DefaultDeviceTimeoutMs
            : GetInt(existing.Options, "timeoutMs", DefaultDeviceTimeoutMs);

        return new DeviceBinding
        {
            Id = id,
            Enabled = document.Enabled,
            Adapter = adapter,
            Options = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
            {
                ["host"] = Require(document.Host, $"device {id} host"),
                ["port"] = document.Port <= 0 ? 8193 : document.Port,
                ["timeoutMs"] = timeoutMs
            }
        };
    }

    private static GatewayConfiguration Clone(GatewayConfiguration source)
    {
        return new GatewayConfiguration
        {
            Gateway = new GatewayIdentity
            {
                Id = source.Gateway.Id,
                Site = source.Gateway.Site
            },
            Pipeline = new PipelineOptions
            {
                SweepInterval = source.Pipeline.SweepInterval,
                ChangeOnly = source.Pipeline.ChangeOnly
            },
            ProgramTransfer = new ProgramTransferOptions
            {
                Enabled = source.ProgramTransfer.Enabled
            },
            Mqtt = new MqttOptions
            {
                Host = source.Mqtt.Host,
                Port = source.Mqtt.Port,
                ClientId = source.Mqtt.ClientId,
                Username = source.Mqtt.Username,
                Password = source.Mqtt.Password,
                Qos = source.Mqtt.Qos,
                Tls = source.Mqtt.Tls
            },
            Console = new ConsoleOptions
            {
                Enabled = source.Console.Enabled,
                Bind = source.Console.Bind,
                Port = source.Console.Port,
                Token = source.Console.Token
            },
            Devices = source.Devices.Select(d => new DeviceBinding
            {
                Id = d.Id,
                Enabled = d.Enabled,
                Adapter = d.Adapter,
                Options = new Dictionary<string, object?>(d.Options, StringComparer.OrdinalIgnoreCase)
            }).ToList()
        };
    }

    private static string Require(string? value, string name)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException($"{name} is required.");
        }

        return value.Trim();
    }

    private static string GetString(IReadOnlyDictionary<string, object?> options, string key, string fallback)
    {
        if (!options.TryGetValue(key, out var value) || value is null)
        {
            return fallback;
        }

        return Convert.ToString(value, CultureInfo.InvariantCulture) ?? fallback;
    }

    private static int GetInt(IReadOnlyDictionary<string, object?> options, string key, int fallback)
    {
        if (!options.TryGetValue(key, out var value) || value is null)
        {
            return fallback;
        }

        return value switch
        {
            int i => i,
            long l => checked((int)l),
            string s when int.TryParse(s, CultureInfo.InvariantCulture, out var parsed) => parsed,
            _ => fallback
        };
    }
}
