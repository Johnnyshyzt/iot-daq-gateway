namespace Gateway.Host.Configuration;

internal static class ConfigPath
{
    public static string Resolve(string[] args)
    {
        for (var i = 0; i < args.Length - 1; i++)
        {
            if (string.Equals(args[i], "--config", StringComparison.OrdinalIgnoreCase))
            {
                return Path.GetFullPath(args[i + 1]);
            }
        }

        var fromEnv = Environment.GetEnvironmentVariable("GATEWAY_CONFIG");
        if (!string.IsNullOrWhiteSpace(fromEnv))
        {
            return Path.GetFullPath(fromEnv);
        }

        foreach (var candidate in CandidatePaths())
        {
            if (File.Exists(candidate))
            {
                return Path.GetFullPath(candidate);
            }
        }

        throw new FileNotFoundException(
            "Gateway YAML not found. Pass --config <path> or set GATEWAY_CONFIG.");
    }

    private static IEnumerable<string> CandidatePaths()
    {
        yield return Path.Combine(Directory.GetCurrentDirectory(), "gateway.yaml");
        yield return Path.Combine(Directory.GetCurrentDirectory(), "configs", "examples", "gateway.yaml");
        yield return Path.Combine(AppContext.BaseDirectory, "gateway.yaml");
    }
}
