namespace Adapters.Cnc.Drivers;

public sealed class SdkInspection
{
    public bool Installed { get; init; }

    public string Directory { get; init; } = "";

    public IReadOnlyList<string> Files { get; init; } = [];

    public string Message { get; init; } = "";
}

public static class SdkLocator
{
    public static string Root()
    {
        var configured = Environment.GetEnvironmentVariable("GATEWAY_SDK");
        if (!string.IsNullOrWhiteSpace(configured))
        {
            return Path.GetFullPath(configured);
        }

        foreach (var variable in new[] { "HOST_DATA", "STUDIO_DATA" })
        {
            var data = Environment.GetEnvironmentVariable(variable);
            if (!string.IsNullOrWhiteSpace(data))
            {
                return Path.Combine(Path.GetFullPath(data), "sdk");
            }
        }

        var walked = WalkForDataSdk();
        return walked ?? Path.Combine(AppContext.BaseDirectory, "data", "sdk");
    }

    public static string VendorDirectory(string vendor, string? overridePath = null)
    {
        if (!string.IsNullOrWhiteSpace(overridePath))
        {
            return Path.GetFullPath(overridePath);
        }

        return Path.Combine(Root(), vendor);
    }

    public static SdkInspection Inspect(string vendor, string? overridePath = null)
    {
        var directory = VendorDirectory(vendor, overridePath);
        if (!Directory.Exists(directory))
        {
            return Missing(vendor, directory);
        }

        var files = Directory.EnumerateFiles(directory)
            .Select(Path.GetFileName)
            .Where(name => name is not null && !name.EndsWith(".md", StringComparison.OrdinalIgnoreCase))
            .Cast<string>()
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (files.Count == 0)
        {
            return Missing(vendor, directory);
        }

        return new SdkInspection
        {
            Installed = true,
            Directory = directory,
            Files = files,
            Message = $"已在 {directory} 找到 {string.Join("、", files)}。"
        };
    }

    private static SdkInspection Missing(string vendor, string directory) => new()
    {
        Installed = false,
        Directory = directory,
        Message = $"SDK 未安装：请把 {vendor} 的厂商文件放到 {directory}。仓库和安装包都不附带这些文件。"
    };

    private static string? WalkForDataSdk()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        for (var i = 0; i < 6 && current is not null; i++)
        {
            var candidate = Path.Combine(current.FullName, "data", "sdk");
            if (Directory.Exists(Path.Combine(current.FullName, "data")))
            {
                return candidate;
            }

            current = current.Parent;
        }

        return null;
    }
}
