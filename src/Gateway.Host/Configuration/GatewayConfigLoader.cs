namespace Gateway.Host.Configuration;

internal static class GatewayConfigLoader
{
    public static LoadedGateway Load(string path)
    {
        if (Directory.Exists(path))
        {
            return V1BundleLoader.Load(path);
        }

        if (!File.Exists(path))
        {
            throw new FileNotFoundException($"Gateway config not found: {path}", path);
        }

        if (LooksLikeV1Gateway(path))
        {
            var directory = Path.GetDirectoryName(Path.GetFullPath(path))
                ?? throw new InvalidOperationException($"Cannot resolve bundle directory for {path}");
            return V1BundleLoader.Load(directory);
        }

        var configuration = GatewayYamlLoader.Load(path);
        return new LoadedGateway(
            configuration,
            Path.GetFullPath(path),
            Revision: null,
            DisplayName: configuration.Gateway.Id,
            IsBundle: false);
    }

    private static bool LooksLikeV1Gateway(string path)
    {
        var hasVersion = false;
        var hasKind = false;
        foreach (var line in File.ReadLines(path).Take(40))
        {
            var trimmed = line.Trim();
            if (trimmed.Length == 0 || trimmed.StartsWith('#'))
            {
                continue;
            }

            if (trimmed.StartsWith("apiVersion:", StringComparison.Ordinal)
                && trimmed.Contains("daq.gateway/v1", StringComparison.Ordinal))
            {
                hasVersion = true;
            }

            if (trimmed.StartsWith("kind:", StringComparison.Ordinal)
                && trimmed.Contains("Gateway", StringComparison.Ordinal))
            {
                hasKind = true;
            }
        }

        return hasVersion && hasKind;
    }
}
