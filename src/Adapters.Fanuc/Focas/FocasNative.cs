namespace Adapters.Fanuc.Focas;

/// <summary>
/// Placeholder for user-supplied FANUC FOCAS native entry points.
/// Vendor binaries (Fwlib64.dll / libfwlib32.so) must NOT be committed here.
/// </summary>
internal static class FocasNative
{
    public const string WindowsLibrary = "Fwlib64";
    public const string LinuxLibrary = "libfwlib32";

    public static string LibraryName =>
        OperatingSystem.IsWindows() ? WindowsLibrary : LinuxLibrary;

    /// <summary>
    /// V1 does not invoke native FOCAS. Integrators should replace this stub
    /// with real P/Invoke once they have licensed libraries on the host.
    /// </summary>
    public static bool TryAllocateHandle(string host, ushort port, int timeoutMs, out ushort handle, out string error)
    {
        _ = host;
        _ = port;
        _ = timeoutMs;
        handle = 0;
        error =
            $"FOCAS P/Invoke is not wired in V1. Place {LibraryName} beside the process " +
            "and implement the TODO signatures in FocasNative.";
        return false;
    }

    // TODO: bind after the integrator supplies FOCAS binaries (do not call in V1).
    // Typical Ethernet FOCAS entry points:
    //   short cnc_allclibhndl3(string ipaddr, ushort port, int timeout, out ushort FlibHndl);
    //   short cnc_freelibhndl(ushort FlibHndl);
    //   short cnc_statinfo(ushort FlibHndl, out /* ODBST */ status);
    //   short cnc_rdalmmsg(ushort FlibHndl, short type, ref short num, /* ODBALMMSG[] */ msg);
    //   short cnc_rdprgnum(ushort FlibHndl, out /* ODBPRO */ prgnum);
    //
    // Example shape (kept as documentation, not a live import):
    // [DllImport(WindowsLibrary, EntryPoint = "cnc_allclibhndl3", CallingConvention = CallingConvention.Cdecl)]
    // private static extern short cnc_allclibhndl3(string ipaddr, ushort port, int timeout, out ushort handle);
}
