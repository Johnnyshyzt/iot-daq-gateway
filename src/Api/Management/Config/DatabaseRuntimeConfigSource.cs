using System.Text.Json;
using Gateway.Abstractions.Configuration;
using Gateway.Abstractions.Contracts;
using Studio.Contracts;

namespace Studio.Host.Config;

public sealed class DatabaseRuntimeConfigSource(ConfigStore store) : IRuntimeConfigSource
{
    public RuntimeConfigSnapshot Load()
    {
        var bundle = store.ReadPublished();
        var revision = store.ActiveRevision();
        return new RuntimeConfigSnapshot
        {
            Configuration = BundleRuntime.ToConfiguration(bundle),
            Revision = revision,
            DisplayName = bundle.Gateway.Metadata.Name,
            Source = "database"
        };
    }
}

public static class BundleRuntime
{
    public static GatewayConfiguration ToConfiguration(ConfigBundle bundle)
    {
        var gateway = bundle.Gateway;
        var site = gateway.Metadata.SiteId;
        var defaultInterval = Math.Max(100, gateway.Spec.Acquisition.DefaultIntervalMs);
        var devices = new List<DeviceBinding>();
        var intervals = new List<int>();
        foreach (var device in bundle.Devices)
        {
            var interval = Math.Max(100, device.Spec.IntervalMs > 0 ? device.Spec.IntervalMs : defaultInterval);
            if (device.Spec.Enabled)
            {
                intervals.Add(interval);
            }

            var connection = device.Spec.Connection ?? new DeviceConnection();
            var options = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
            if (connection.Parameters is not null)
            {
                foreach (var pair in connection.Parameters)
                {
                    if (!string.IsNullOrWhiteSpace(pair.Key))
                    {
                        options[pair.Key] = pair.Value;
                    }
                }
            }

            options["host"] = string.IsNullOrWhiteSpace(connection.Host) ? "127.0.0.1" : connection.Host;
            options["port"] = connection.Port;
            options["timeoutMs"] = connection.TimeoutMs ?? connection.FocasTimeoutMs ?? 3000;
            options["displayName"] = device.Metadata.DisplayName;
            options["path"] = connection.Path;
            options["namespace"] = connection.Namespace;
            options["username"] = connection.Username;
            options["password"] = connection.Password;
            options["brandId"] = device.Spec.BrandId;
            options["controllerModelId"] = device.Spec.ControllerModelId;
            var addresses = AddressMap(bundle, device);
            if (addresses is not null)
            {
                options["addresses"] = addresses;
            }
            var points = EnabledPointIds(bundle, device);
            if (points is not null)
            {
                options["points"] = points;
            }

            devices.Add(new DeviceBinding
            {
                Id = device.Metadata.Id,
                Enabled = device.Spec.Enabled,
                Adapter = device.Spec.Adapter,
                Options = options
            });
        }

        if (devices.Count == 0)
        {
            throw new InvalidOperationException("已发布配置里没有设备。");
        }

        var mqtt = bundle.Mqtt.Spec;
        var broker = mqtt.Broker;
        return new GatewayConfiguration
        {
            Gateway = new GatewayIdentity
            {
                Id = "gw-" + site,
                Site = site
            },
            Pipeline = new PipelineOptions
            {
                SweepInterval = TimeSpan.FromMilliseconds(intervals.Count > 0 ? intervals.Min() : defaultInterval),
                ChangeOnly = gateway.Spec.Acquisition.ChangeOnly
            },
            ProgramTransfer = new ProgramTransferOptions
            {
                Enabled = gateway.Spec.Features.ProgramWrite
            },
            Mqtt = new MqttOptions
            {
                Host = broker.Host,
                Port = broker.Port,
                ClientId = string.IsNullOrWhiteSpace(broker.ClientId) ? "iot-daq-gateway" : broker.ClientId,
                Qos = mqtt.Qos,
                Tls = broker.Tls,
                Retain = mqtt.Retain,
                TopicTemplate = string.IsNullOrWhiteSpace(mqtt.TopicTemplate) ? "daq/{site}/{deviceId}/{point}" : mqtt.TopicTemplate,
                StatusTopic = string.IsNullOrWhiteSpace(mqtt.StatusTopic) ? "daq/{site}/{deviceId}/$status" : mqtt.StatusTopic,
                Username = ResolveEnv(broker.UsernameFromEnv),
                Password = ResolveEnv(broker.PasswordFromEnv)
            },
            Devices = devices
        };
    }

    private static string? EnabledPointIds(ConfigBundle bundle, DeviceDocument device)
    {
        var templateId = device.Spec.PointTemplateId;
        if (string.IsNullOrWhiteSpace(templateId))
        {
            var only = bundle.PointSets.FirstOrDefault(set =>
                string.Equals(set.Metadata.DeviceId, device.Metadata.Id, StringComparison.OrdinalIgnoreCase));
            return only is null
                ? null
                : string.Join(',', only.Spec.Points.Where(point => point.Enabled).Select(point => point.Id));
        }

        var template = bundle.PointTemplates.FirstOrDefault(item =>
            string.Equals(item.Metadata.Id, templateId, StringComparison.OrdinalIgnoreCase));
        if (template is null)
        {
            throw new InvalidOperationException($"设备 {device.Metadata.Id} 引用的点位模板「{templateId}」不存在。");
        }

        var rows = template.Spec.Points.Select(point => (point.Id, point.Enabled)).ToList();
        var overrides = bundle.PointSets.FirstOrDefault(set =>
            string.Equals(set.Metadata.DeviceId, device.Metadata.Id, StringComparison.OrdinalIgnoreCase));
        if (overrides is not null)
        {
            foreach (var point in overrides.Spec.Points)
            {
                var index = rows.FindIndex(row => string.Equals(row.Id, point.Id, StringComparison.OrdinalIgnoreCase));
                if (index >= 0)
                {
                    rows[index] = (rows[index].Id, point.Enabled);
                }
                else
                {
                    rows.Add((point.Id, point.Enabled));
                }
            }
        }

        return string.Join(',', rows.Where(row => row.Enabled).Select(row => row.Id));
    }

    private static string? AddressMap(ConfigBundle bundle, DeviceDocument device)
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        void Take(IEnumerable<PointDefinition> points)
        {
            foreach (var point in points)
            {
                if (!string.IsNullOrWhiteSpace(point.Id) && !string.IsNullOrWhiteSpace(point.Address))
                {
                    map[point.Id] = point.Address;
                }
            }
        }

        var templateId = device.Spec.PointTemplateId;
        if (!string.IsNullOrWhiteSpace(templateId))
        {
            var template = bundle.PointTemplates.FirstOrDefault(item =>
                string.Equals(item.Metadata.Id, templateId, StringComparison.OrdinalIgnoreCase));
            if (template is not null)
            {
                Take(template.Spec.Points.Where(point => point.Enabled));
            }
        }

        var overrides = bundle.PointSets.FirstOrDefault(set =>
            string.Equals(set.Metadata.DeviceId, device.Metadata.Id, StringComparison.OrdinalIgnoreCase));
        if (overrides is not null)
        {
            Take(overrides.Spec.Points.Where(point => point.Enabled));
        }

        return map.Count == 0 ? null : JsonSerializer.Serialize(map);
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
}
