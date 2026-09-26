using System.Globalization;
using System.Text.Json;
using Gateway.Abstractions.Configuration;

namespace Adapters.Cnc.Drivers;

public sealed class ConnectionSettings
{
    public string Host { get; init; } = "127.0.0.1";

    public int Port { get; init; } = 8193;

    public int TimeoutMs { get; init; } = 3000;

    public string Path { get; init; } = "";

    public string NamespaceUri { get; init; } = "";

    public string Username { get; init; } = "";

    public string Password { get; init; } = "";

    public int UnitId { get; init; } = 1;

    public string BrandId { get; init; } = "";

    public string ModelId { get; init; } = "";

    public string SdkPath { get; init; } = "";

    public IReadOnlyCollection<string>? Points { get; init; }

    public IReadOnlyDictionary<string, string> Addresses { get; init; } =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    public static ConnectionSettings Read(DeviceBinding binding)
    {
        var options = binding.Options;
        return new ConnectionSettings
        {
            Host = Text(options, "host") ?? "127.0.0.1",
            Port = Int(options, "port", 8193),
            TimeoutMs = Math.Clamp(Int(options, "timeoutMs", 3000), 100, 120_000),
            Path = Text(options, "path") ?? "",
            NamespaceUri = Text(options, "namespace") ?? "",
            Username = Text(options, "username") ?? "",
            Password = Text(options, "password") ?? "",
            UnitId = Math.Clamp(Int(options, "unitId", 1), 0, 255),
            BrandId = Text(options, "brandId") ?? "",
            ModelId = Text(options, "controllerModelId") ?? "",
            SdkPath = Text(options, "sdkPath") ?? "",
            Points = ReadPoints(options),
            Addresses = ReadAddresses(options)
        };
    }

    public string AddressFor(string itemId)
    {
        return Addresses.TryGetValue(itemId, out var address) ? address : "";
    }

    public bool Wants(string itemId)
    {
        return Points is null || Points.Contains(itemId, StringComparer.OrdinalIgnoreCase);
    }

    private static IReadOnlyCollection<string>? ReadPoints(IReadOnlyDictionary<string, object?> options)
    {
        if (!options.TryGetValue("points", out var value) || value is null)
        {
            return null;
        }

        if (value is string text)
        {
            var parts = text.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            return parts.Length == 0 ? null : parts;
        }

        return null;
    }

    private static IReadOnlyDictionary<string, string> ReadAddresses(IReadOnlyDictionary<string, object?> options)
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (!options.TryGetValue("addresses", out var value) || value is null)
        {
            return map;
        }

        var text = Convert.ToString(value, CultureInfo.InvariantCulture);
        if (string.IsNullOrWhiteSpace(text))
        {
            return map;
        }

        try
        {
            var parsed = JsonSerializer.Deserialize<Dictionary<string, string>>(text);
            if (parsed is null)
            {
                return map;
            }

            foreach (var pair in parsed)
            {
                if (!string.IsNullOrWhiteSpace(pair.Key) && !string.IsNullOrWhiteSpace(pair.Value))
                {
                    map[pair.Key] = pair.Value;
                }
            }
        }
        catch (JsonException)
        {
            // A missing or hand-edited map falls back to the driver's built-in item ids.
        }

        return map;
    }

    private static string? Text(IReadOnlyDictionary<string, object?> options, string key)
    {
        if (!options.TryGetValue(key, out var value) || value is null)
        {
            return null;
        }

        var text = Convert.ToString(value, CultureInfo.InvariantCulture);
        return string.IsNullOrWhiteSpace(text) ? null : text.Trim();
    }

    private static int Int(IReadOnlyDictionary<string, object?> options, string key, int fallback)
    {
        var text = Text(options, key);
        return int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var number) ? number : fallback;
    }
}
