namespace Adapters.Fanuc.Focas;

/// <summary>
/// Site-facing FOCAS library placement. Vendor binaries are never shipped.
/// </summary>
public static class FocasLibraryFiles
{
    public const string WindowsDll = FocasNative.WindowsLibraryFile;

    public static string SearchDirectory => AppContext.BaseDirectory;

    public static string ExpectedPath => Path.Combine(SearchDirectory, WindowsDll);

    public static bool TryLoad(out string error) => FocasNative.TryLoad(out error);
}
