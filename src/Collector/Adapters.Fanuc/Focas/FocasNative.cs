using System.Runtime.InteropServices;

namespace Adapters.Fanuc.Focas;

/// <summary>
/// P/Invoke bindings for typical FOCAS Ethernet APIs against x64 <c>Fwlib64.dll</c>.
/// Vendor binaries are never shipped; load failures are returned as errors.
/// </summary>
internal static class FocasNative
{
    public const string WindowsLibrary = "Fwlib64";
    public const string WindowsLibraryFile = "Fwlib64.dll";

    private static readonly object LoadGate = new();
    private static bool? _available;
    private static string _loadError = string.Empty;
    private static FocasLibraryProblem _problem = FocasLibraryProblem.None;

    public static FocasLibraryProblem LastProblem
    {
        get
        {
            lock (LoadGate)
            {
                return _problem;
            }
        }
    }

    /// <summary>
    /// Seconds passed to <c>cnc_allclibhndl3</c> / <c>cnc_settimeout</c> (FOCAS uses seconds, not ms).
    /// </summary>
    public static int ToTimeoutSeconds(int timeoutMs)
    {
        if (timeoutMs <= 0)
        {
            return 10;
        }

        return Math.Max(1, (int)Math.Ceiling(timeoutMs / 1000.0));
    }

    public static bool TryLoad(out string error)
    {
        lock (LoadGate)
        {
            if (_available is bool cached)
            {
                error = _loadError;
                return cached;
            }

            var ok = Probe(out var reason, out var problem);
            _available = ok;
            _loadError = reason;
            _problem = problem;
            error = reason;
            return ok;
        }
    }

    private static bool Probe(out string error, out FocasLibraryProblem problem)
    {
        if (!OperatingSystem.IsWindows())
        {
            problem = FocasLibraryProblem.UnsupportedOperatingSystem;
            error =
                "FOCAS collection is Windows x64 only. Place licensed Fwlib64.dll next to the gateway process. " +
                "Linux Docker is not a production FOCAS path.";
            return false;
        }

        if (!Environment.Is64BitProcess)
        {
            problem = FocasLibraryProblem.WrongProcessArchitecture;
            error = "FOCAS adapter requires a 64-bit process to load Fwlib64.dll.";
            return false;
        }

        var besideProcess = Path.Combine(AppContext.BaseDirectory, WindowsLibraryFile);
        try
        {
            if (NativeLibrary.TryLoad(besideProcess, out _) ||
                NativeLibrary.TryLoad(WindowsLibrary, out _) ||
                NativeLibrary.TryLoad(WindowsLibraryFile, out _))
            {
                problem = FocasLibraryProblem.None;
                error = string.Empty;
                return true;
            }
        }
        catch (BadImageFormatException)
        {
            problem = FocasLibraryProblem.WrongLibraryArchitecture;
            error =
                $"{WindowsLibraryFile} is the wrong bitness (BadImageFormat). " +
                "Use the 64-bit FANUC library with a 64-bit gateway process.";
            return false;
        }
        catch (Exception ex)
        {
            problem = FocasLibraryProblem.LoadFailed;
            error = $"Failed to load {WindowsLibraryFile}: {ex.Message}";
            return false;
        }

        problem = FocasLibraryProblem.MissingLibrary;
        error =
            $"Missing {WindowsLibraryFile} next to the process (searched {AppContext.BaseDirectory}). " +
            "Licensed FANUC FOCAS libraries are not shipped in git or container images.";
        return false;
    }

    [DllImport(WindowsLibrary, EntryPoint = "cnc_allclibhndl3", CallingConvention = CallingConvention.Winapi, CharSet = CharSet.Ansi)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.AssemblyDirectory | DllImportSearchPath.SafeDirectories)]
    private static extern short cnc_allclibhndl3(
        [MarshalAs(UnmanagedType.LPStr)] string ipaddr,
        ushort port,
        int timeout,
        out ushort FlibHndl);

    [DllImport(WindowsLibrary, EntryPoint = "cnc_freelibhndl", CallingConvention = CallingConvention.Winapi)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.AssemblyDirectory | DllImportSearchPath.SafeDirectories)]
    private static extern short cnc_freelibhndl(ushort FlibHndl);

    [DllImport(WindowsLibrary, EntryPoint = "cnc_settimeout", CallingConvention = CallingConvention.Winapi)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.AssemblyDirectory | DllImportSearchPath.SafeDirectories)]
    private static extern short cnc_settimeout(ushort FlibHndl, int timeout);

    [DllImport(WindowsLibrary, EntryPoint = "cnc_statinfo", CallingConvention = CallingConvention.Winapi)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.AssemblyDirectory | DllImportSearchPath.SafeDirectories)]
    private static extern short cnc_statinfo(ushort FlibHndl, out OdbSt statinfo);

    [DllImport(WindowsLibrary, EntryPoint = "cnc_rdalmmsg", CallingConvention = CallingConvention.Winapi)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.AssemblyDirectory | DllImportSearchPath.SafeDirectories)]
    private static extern short cnc_rdalmmsg(ushort FlibHndl, short type, ref short num, ref OdbAlmMsg almmsg);

    [DllImport(WindowsLibrary, EntryPoint = "cnc_rdprgnum", CallingConvention = CallingConvention.Winapi)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.AssemblyDirectory | DllImportSearchPath.SafeDirectories)]
    private static extern short cnc_rdprgnum(ushort FlibHndl, out OdbPro prgnum);

    [DllImport(WindowsLibrary, EntryPoint = "cnc_rdprgnumo8", CallingConvention = CallingConvention.Winapi)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.AssemblyDirectory | DllImportSearchPath.SafeDirectories)]
    private static extern short cnc_rdprgnumo8(ushort FlibHndl, out OdbProO8 prgnum);

    public static short AllocateHandle(string ipAddress, ushort port, int timeoutSeconds, out ushort handle) =>
        cnc_allclibhndl3(ipAddress, port, timeoutSeconds, out handle);

    public static short FreeHandle(ushort handle) => cnc_freelibhndl(handle);

    public static short SetTimeout(ushort handle, int timeoutSeconds) => cnc_settimeout(handle, timeoutSeconds);

    public static short ReadStatus(ushort handle, out OdbSt status) => cnc_statinfo(handle, out status);

    public static short ReadAlarmMessage(ushort handle, short type, ref short num, ref OdbAlmMsg message) =>
        cnc_rdalmmsg(handle, type, ref num, ref message);

    public static short ReadProgramNumber(ushort handle, out OdbPro program) => cnc_rdprgnum(handle, out program);

    public static short ReadProgramNumberO8(ushort handle, out OdbProO8 program) => cnc_rdprgnumo8(handle, out program);

    /// <summary>
    /// <c>ODBST</c> for Series 16/18/21/16i/18i/21i/0i/30i (not Series 15). Pack=4 as required by fwlib64.h.
    /// Size is 18 bytes (9 × short).
    /// </summary>
    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    internal struct OdbSt
    {
        public short Hdck;
        public short TmMode;
        public short Aut;
        public short Run;
        public short Motion;
        public short Mstb;
        public short Emergency;
        public short Alarm;
        public short Edit;
    }

    /// <summary>
    /// 4-digit <c>ODBPRO</c> for <c>cnc_rdprgnum</c>. Pack=4, size 8 bytes.
    /// </summary>
    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    internal struct OdbPro
    {
        public short Dummy0;
        public short Dummy1;
        public short Data;
        public short MData;
    }

    /// <summary>
    /// 8-digit <c>ODBPRO</c> for <c>cnc_rdprgnumo8</c>. Pack=4, size 12 bytes.
    /// </summary>
    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    internal struct OdbProO8
    {
        public short Dummy0;
        public short Dummy1;
        public int Data;
        public int MData;
    }

    /// <summary>
    /// <c>ODBALMMSG</c> for <c>cnc_rdalmmsg</c>. Pack=4, size 44 bytes.
    /// </summary>
    [StructLayout(LayoutKind.Sequential, Pack = 4, CharSet = CharSet.Ansi)]
    internal struct OdbAlmMsg
    {
        public int AlmNo;
        public short Type;
        public short Axis;
        public short Dummy;
        public short MsgLen;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string AlmMsg;
    }
}
