using System.Text.Json;

namespace Gateway.Abstractions.Reliability;

public sealed class ReliabilityOptions
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public int ReconnectInitialSeconds { get; set; } = 2;

    public double ReconnectMultiplier { get; set; } = 2;

    public int ReconnectCapSeconds { get; set; } = 120;

    public double ReconnectJitter { get; set; } = 0.2;

    public int StallSeconds { get; set; } = 30;

    public int SpoolMaxMessages { get; set; } = 10_000;

    public int SpoolMaxAgeHours { get; set; } = 24;

    public int SpoolMaxMegabytes { get; set; } = 64;

    public string SpoolDirectory { get; set; } = "";

    public TimeSpan Initial => TimeSpan.FromSeconds(Math.Clamp(ReconnectInitialSeconds, 1, 3600));

    public TimeSpan Cap => TimeSpan.FromSeconds(Math.Clamp(Math.Max(ReconnectCapSeconds, ReconnectInitialSeconds), 1, 86_400));

    public TimeSpan Stall => TimeSpan.FromSeconds(Math.Clamp(StallSeconds, 5, 3600));

    public TimeSpan SpoolMaxAge => TimeSpan.FromHours(Math.Clamp(SpoolMaxAgeHours, 1, 24 * 30));

    public int SpoolMessageCap => Math.Clamp(SpoolMaxMessages, 10, 1_000_000);

    public long SpoolByteCap => Math.Clamp(SpoolMaxMegabytes, 1, 10_240) * 1024L * 1024L;

    public void Normalize()
    {
        ReconnectInitialSeconds = Math.Clamp(ReconnectInitialSeconds, 1, 3600);
        ReconnectCapSeconds = Math.Clamp(Math.Max(ReconnectCapSeconds, ReconnectInitialSeconds), 1, 86_400);
        ReconnectMultiplier = Math.Clamp(ReconnectMultiplier, 1.1, 10);
        ReconnectJitter = Math.Clamp(ReconnectJitter, 0, 0.5);
        StallSeconds = Math.Clamp(StallSeconds, 5, 3600);
        SpoolMaxMessages = SpoolMessageCap;
        SpoolMaxAgeHours = Math.Clamp(SpoolMaxAgeHours, 1, 24 * 30);
        SpoolMaxMegabytes = Math.Clamp(SpoolMaxMegabytes, 1, 10_240);
    }

    public string ToJson() => JsonSerializer.Serialize(this, Json);

    public void OverlayJson(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return;
        }

        try
        {
            var parsed = JsonSerializer.Deserialize<ReliabilityOptions>(json, Json);
            if (parsed is null)
            {
                return;
            }

            ReconnectInitialSeconds = parsed.ReconnectInitialSeconds;
            ReconnectMultiplier = parsed.ReconnectMultiplier;
            ReconnectCapSeconds = parsed.ReconnectCapSeconds;
            ReconnectJitter = parsed.ReconnectJitter;
            StallSeconds = parsed.StallSeconds;
            SpoolMaxMessages = parsed.SpoolMaxMessages;
            SpoolMaxAgeHours = parsed.SpoolMaxAgeHours;
            SpoolMaxMegabytes = parsed.SpoolMaxMegabytes;
            Normalize();
        }
        catch (JsonException)
        {
            // Keep the values already loaded from configuration.
        }
    }
}
