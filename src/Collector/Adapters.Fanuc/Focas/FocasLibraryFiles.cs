namespace Adapters.Fanuc.Focas;

/// <summary>
/// Site-facing FOCAS library placement. Vendor binaries are never shipped.
/// </summary>
public static class FocasLibraryFiles
{
    public const string WindowsDll = FocasNative.WindowsLibraryFile;

    public static string SearchDirectory => AppContext.BaseDirectory;

    public static string ExpectedPath => Path.Combine(SearchDirectory, WindowsDll);

    public static string SdkDirectory
    {
        get
        {
            var configured = Environment.GetEnvironmentVariable("GATEWAY_SDK");
            if (!string.IsNullOrWhiteSpace(configured))
            {
                return Path.Combine(Path.GetFullPath(configured), "fanuc");
            }

            foreach (var variable in new[] { "HOST_DATA", "STUDIO_DATA" })
            {
                var data = Environment.GetEnvironmentVariable(variable);
                if (!string.IsNullOrWhiteSpace(data))
                {
                    return Path.Combine(Path.GetFullPath(data), "sdk", "fanuc");
                }
            }

            var current = new DirectoryInfo(AppContext.BaseDirectory);
            for (var i = 0; i < 6 && current is not null; i++)
            {
                if (Directory.Exists(Path.Combine(current.FullName, "data")))
                {
                    return Path.Combine(current.FullName, "data", "sdk", "fanuc");
                }

                current = current.Parent;
            }

            return Path.Combine(AppContext.BaseDirectory, "data", "sdk", "fanuc");
        }
    }

    public static bool TryLoad(out string error) => FocasNative.TryLoad(out error);
}
