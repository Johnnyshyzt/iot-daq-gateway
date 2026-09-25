using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Studio.Contracts;

namespace Studio.Host.Config;

public static class CanonicalRevision
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    public static string Compute(ConfigBundle bundle)
    {
        var normalized = new ConfigBundle
        {
            Gateway = bundle.Gateway,
            Devices = bundle.Devices
                .OrderBy(device => device.Metadata.Id, StringComparer.Ordinal)
                .ToList(),
            PointSets = bundle.PointSets
                .OrderBy(set => set.Metadata.DeviceId, StringComparer.Ordinal)
                .Select(set => new PointSetDocument
                {
                    ApiVersion = set.ApiVersion,
                    Kind = set.Kind,
                    Metadata = set.Metadata,
                    Spec = new PointSetSpec
                    {
                        Points = set.Spec.Points
                            .OrderBy(point => point.Id, StringComparer.Ordinal)
                            .ToList()
                    }
                })
                .ToList(),
            Mqtt = bundle.Mqtt
        };

        var node = JsonSerializer.SerializeToNode(normalized, Options);
        var sorted = Sort(node);
        var json = sorted?.ToJsonString(Options) ?? "null";
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(json));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static JsonNode? Sort(JsonNode? node)
    {
        switch (node)
        {
            case null:
                return null;
            case JsonObject obj:
            {
                var sorted = new JsonObject();
                foreach (var property in obj.OrderBy(item => item.Key, StringComparer.Ordinal))
                {
                    sorted[property.Key] = Sort(property.Value);
                }

                return sorted;
            }
            case JsonArray array:
            {
                var copy = new JsonArray();
                foreach (var item in array)
                {
                    copy.Add(Sort(item));
                }

                return copy;
            }
            default:
                return node.DeepClone();
        }
    }
}
