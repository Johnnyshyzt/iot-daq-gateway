namespace IotDaq.Host;

public static class HostPaths
{
    public static string ResolveDataDirectory(IHostEnvironment environment, IConfiguration configuration)
    {
        var fromEnv = Environment.GetEnvironmentVariable("HOST_DATA")
            ?? Environment.GetEnvironmentVariable("STUDIO_DATA");
        if (!string.IsNullOrWhiteSpace(fromEnv))
        {
            return Path.GetFullPath(fromEnv);
        }

        var configured = configuration["Host:DataDirectory"] ?? configuration["Studio:DataDirectory"];
        if (!string.IsNullOrWhiteSpace(configured))
        {
            return Path.IsPathRooted(configured)
                ? configured
                : Path.GetFullPath(Path.Combine(environment.ContentRootPath, configured));
        }

        var besideContent = Path.Combine(environment.ContentRootPath, "data");
        if (HasSeedOrPublished(besideContent))
        {
            return Path.GetFullPath(besideContent);
        }

        var dir = new DirectoryInfo(environment.ContentRootPath);
        for (var i = 0; i < 6 && dir is not null; i++, dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, "data");
            if (File.Exists(Path.Combine(candidate, "seed", "gateway.yaml")))
            {
                return Path.GetFullPath(candidate);
            }
        }

        var besideOutput = Path.Combine(AppContext.BaseDirectory, "data");
        if (HasSeedOrPublished(besideOutput))
        {
            return Path.GetFullPath(besideOutput);
        }

        return Path.GetFullPath(besideContent);
    }

    public static string? ResolveWebRoot(IHostEnvironment environment)
    {
        foreach (var candidate in new[]
                 {
                     Path.Combine(environment.ContentRootPath, "wwwroot"),
                     Path.Combine(AppContext.BaseDirectory, "wwwroot"),
                     Path.Combine(environment.ContentRootPath, "..", "Web", "dist")
                 })
        {
            var full = Path.GetFullPath(candidate);
            if (File.Exists(Path.Combine(full, "index.html")))
            {
                return full;
            }
        }

        return null;
    }

    private static bool HasSeedOrPublished(string dataDirectory) =>
        File.Exists(Path.Combine(dataDirectory, "seed", "gateway.yaml"))
        || File.Exists(Path.Combine(dataDirectory, "published", "gateway.yaml"));
}
