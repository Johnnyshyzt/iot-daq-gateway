namespace Gateway.Host.Configuration;

internal static class ConfigPath
{
    public static string Resolve(string[] args)
    {
        var specified = GetOption(args, "--config")
            ?? Environment.GetEnvironmentVariable("GATEWAY_CONFIG");

        if (!string.IsNullOrWhiteSpace(specified))
        {
            return FindExisting(specified)
                ?? throw new FileNotFoundException($"Gateway config not found: {specified}");
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

    private static string? GetOption(string[] args, string name)
    {
        for (var i = 0; i < args.Length - 1; i++)
        {
            if (string.Equals(args[i], name, StringComparison.OrdinalIgnoreCase))
            {
                return args[i + 1];
            }
        }

        return null;
    }

    private static string? FindExisting(string path)
    {
        if (Exists(path))
        {
            return Path.GetFullPath(path);
        }

        if (Path.IsPathRooted(path))
        {
            return null;
        }

        var dir = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (dir is not null)
        {
            var combined = Path.Combine(dir.FullName, path);
            if (Exists(combined))
            {
                return Path.GetFullPath(combined);
            }

            dir = dir.Parent;
        }

        return null;
    }

    private static bool Exists(string path) => File.Exists(path) || Directory.Exists(path);

    private static IEnumerable<string> CandidatePaths()
    {
        yield return Path.Combine(Directory.GetCurrentDirectory(), "gateway.yaml");
        yield return Path.Combine(Directory.GetCurrentDirectory(), "configs", "examples", "gateway.yaml");
        yield return Path.Combine(AppContext.BaseDirectory, "gateway.yaml");

        var dir = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (dir is not null)
        {
            yield return Path.Combine(dir.FullName, "configs", "examples", "gateway.yaml");
            dir = dir.Parent;
        }
    }
}
