using System.Globalization;
using Gateway.Abstractions.Reliability;
using Microsoft.Extensions.Configuration;

namespace Gateway.Host.Reliability;

public static class ReliabilityOptionsLoader
{
    public static ReliabilityOptions Load(IConfiguration configuration)
    {
        var options = new ReliabilityOptions();
        if (int.TryParse(configuration["Reliability:ReconnectInitialSeconds"], NumberStyles.Integer, CultureInfo.InvariantCulture, out var initial))
        {
            options.ReconnectInitialSeconds = initial;
        }

        if (double.TryParse(configuration["Reliability:ReconnectMultiplier"], NumberStyles.Float, CultureInfo.InvariantCulture, out var multiplier))
        {
            options.ReconnectMultiplier = multiplier;
        }

        if (int.TryParse(configuration["Reliability:ReconnectCapSeconds"], NumberStyles.Integer, CultureInfo.InvariantCulture, out var cap))
        {
            options.ReconnectCapSeconds = cap;
        }

        if (double.TryParse(configuration["Reliability:ReconnectJitter"], NumberStyles.Float, CultureInfo.InvariantCulture, out var jitter))
        {
            options.ReconnectJitter = jitter;
        }

        if (int.TryParse(configuration["Reliability:StallSeconds"], NumberStyles.Integer, CultureInfo.InvariantCulture, out var stall))
        {
            options.StallSeconds = stall;
        }

        if (int.TryParse(configuration["Mqtt:StoreForward:MaxMessages"], NumberStyles.Integer, CultureInfo.InvariantCulture, out var messages))
        {
            options.SpoolMaxMessages = messages;
        }

        if (int.TryParse(configuration["Mqtt:StoreForward:MaxAgeHours"], NumberStyles.Integer, CultureInfo.InvariantCulture, out var age))
        {
            options.SpoolMaxAgeHours = age;
        }

        if (int.TryParse(configuration["Mqtt:StoreForward:MaxMegabytes"], NumberStyles.Integer, CultureInfo.InvariantCulture, out var megabytes))
        {
            options.SpoolMaxMegabytes = megabytes;
        }

        var data = configuration["Host:DataDirectory"];
        options.SpoolDirectory = string.IsNullOrWhiteSpace(data)
            ? Path.Combine(AppContext.BaseDirectory, "mqtt-spool")
            : Path.Combine(data, "mqtt-spool");
        options.Normalize();
        return options;
    }
}
