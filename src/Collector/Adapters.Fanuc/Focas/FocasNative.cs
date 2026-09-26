using System.Runtime.InteropServices;
using System.Text;

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

    static FocasNative()
    {
        NativeLibrary.SetDllImportResolver(typeof(FocasNative).Assembly, ResolveLibrary);
    }

    private static IntPtr ResolveLibrary(string libraryName, System.Reflection.Assembly assembly, DllImportSearchPath? searchPath)
    {
        if (libraryName is not (WindowsLibrary or "Fwlib32" or "fwlib32"))
        {
            return IntPtr.Zero;
        }

        foreach (var candidate in CandidateFiles())
        {
            if (File.Exists(candidate) && NativeLibrary.TryLoad(candidate, out var handle))
            {
                return handle;
            }
        }

        return IntPtr.Zero;
    }

    internal static IEnumerable<string> CandidateFiles()
    {
        var names = OperatingSystem.IsWindows()
            ? new[] { WindowsLibraryFile, "Fwlib32.dll" }
            : new[] { "libfwlib32.so", "libfwlib32.so.1", WindowsLibraryFile };
        foreach (var directory in new[] { FocasLibraryFiles.SdkDirectory, AppContext.BaseDirectory })
        {
            foreach (var name in names)
            {
                yield return Path.Combine(directory, name);
            }
        }
    }

    private static bool Probe(out string error, out FocasLibraryProblem problem)
    {
        if (OperatingSystem.IsWindows() && !Environment.Is64BitProcess)
        {
            problem = FocasLibraryProblem.WrongProcessArchitecture;
            error = "FOCAS adapter requires a 64-bit process to load Fwlib64.dll.";
            return false;
        }

        var found = CandidateFiles().Where(File.Exists).ToList();
        if (found.Count == 0)
        {
            problem = FocasLibraryProblem.MissingLibrary;
            error =
                $"SDK 未安装：未找到 Fwlib64.dll。请把 Fwlib64.dll 或 libfwlib32.so 放到 {FocasLibraryFiles.SdkDirectory}，" +
                $"或与进程放在同一目录（{AppContext.BaseDirectory}）。";
            return false;
        }

        try
        {
            foreach (var candidate in found)
            {
                if (NativeLibrary.TryLoad(candidate, out _))
                {
                    problem = FocasLibraryProblem.None;
                    error = string.Empty;
                    return true;
                }
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

        problem = FocasLibraryProblem.LoadFailed;
        error = $"Failed to load Fwlib64.dll from {string.Join(", ", found)}.";
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

    [DllImport(WindowsLibrary, EntryPoint = "cnc_acts", CallingConvention = CallingConvention.Winapi)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.AssemblyDirectory | DllImportSearchPath.SafeDirectories)]
    private static extern short cnc_acts(ushort FlibHndl, out OdbAct actual);

    [DllImport(WindowsLibrary, EntryPoint = "cnc_actf", CallingConvention = CallingConvention.Winapi)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.AssemblyDirectory | DllImportSearchPath.SafeDirectories)]
    private static extern short cnc_actf(ushort FlibHndl, out OdbAct actual);

    [DllImport(WindowsLibrary, EntryPoint = "cnc_absolute", CallingConvention = CallingConvention.Winapi)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.AssemblyDirectory | DllImportSearchPath.SafeDirectories)]
    private static extern short cnc_absolute(ushort FlibHndl, short axis, short length, ref OdbAxis absolute);

    [DllImport(WindowsLibrary, EntryPoint = "cnc_sysinfo", CallingConvention = CallingConvention.Winapi)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.AssemblyDirectory | DllImportSearchPath.SafeDirectories)]
    private static extern short cnc_sysinfo(ushort FlibHndl, out OdbSys info);

    public static short ReadActualSpindle(ushort handle, out OdbAct actual) => cnc_acts(handle, out actual);

    public static short ReadActualFeed(ushort handle, out OdbAct actual) => cnc_actf(handle, out actual);

    public static short ReadAbsolute(ushort handle, ref OdbAxis absolute) =>
        cnc_absolute(handle, -1, (short)Marshal.SizeOf<OdbAxis>(), ref absolute);

    public static short ReadSystemInfo(ushort handle, out OdbSys info) => cnc_sysinfo(handle, out info);

    public static string DescribeSystem(in OdbSys info)
    {
        var type = Ascii(info.CncType0, info.CncType1);
        var series = Ascii(info.Series0, info.Series1, info.Series2, info.Series3);
        return string.IsNullOrEmpty(series) ? type : $"{type} {series}".Trim();
    }

    public static string DescribeVersion(in OdbSys info) =>
        Ascii(info.Version0, info.Version1, info.Version2, info.Version3);

    private static string Ascii(params byte[] bytes) =>
        Encoding.ASCII.GetString(bytes).Trim('\0', ' ');

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

    /// <summary>
    /// <c>ODBACT</c> for <c>cnc_acts</c> / <c>cnc_actf</c>. Two dummy shorts plus a 32-bit actual value.
    /// Feed is the raw library integer; the increment is controller-dependent and is not scaled here.
    /// </summary>
    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    internal struct OdbAct
    {
        public short Dummy0;
        public short Dummy1;
        public int Data;
    }

    /// <summary>
    /// <c>ODBAXIS</c> with eight 32-bit positions (36 bytes). <c>cnc_absolute</c> length is this size.
    /// Values are divided by 1000 later, which matches IS-B 0.001 mm and must be accepted on site.
    /// </summary>
    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    internal struct OdbAxis
    {
        public short Dummy;
        public short Type;
        public int Data0;
        public int Data1;
        public int Data2;
        public int Data3;
        public int Data4;
        public int Data5;
        public int Data6;
        public int Data7;
    }

    /// <summary>
    /// <c>ODBSYS</c> from the public FOCAS <c>cnc_sysinfo</c> layout. Character fields are raw bytes so the size stays 18.
    /// </summary>
    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    internal struct OdbSys
    {
        public short AddInfo;
        public short MaxAxis;
        public byte CncType0;
        public byte CncType1;
        public byte MtType0;
        public byte MtType1;
        public byte Series0;
        public byte Series1;
        public byte Series2;
        public byte Series3;
        public byte Version0;
        public byte Version1;
        public byte Version2;
        public byte Version3;
        public byte Axes0;
        public byte Axes1;
    }
}
