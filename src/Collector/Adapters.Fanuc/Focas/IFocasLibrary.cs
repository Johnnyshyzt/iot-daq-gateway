namespace Adapters.Fanuc.Focas;

/// <summary>
/// Testable FOCAS session operations. Production uses <see cref="NativeFocasLibrary"/>.
/// </summary>
internal enum FocasLibraryProblem
{
    None = 0,
    UnsupportedOperatingSystem,
    WrongProcessArchitecture,
    WrongLibraryArchitecture,
    MissingLibrary,
    LoadFailed
}

internal interface IFocasLibrary
{
    bool TryGetAvailability(out string error);

    FocasLibraryProblem AvailabilityProblem { get; }

    short AllocateHandle(string ipAddress, ushort port, int timeoutSeconds, out ushort handle);

    short SetTimeout(ushort handle, int timeoutSeconds);

    short FreeHandle(ushort handle);

    short ReadStatus(ushort handle, out FocasStatInfo status);

    short ReadFirstAlarmNumber(ushort handle, out int alarmNumber);

    short ReadProgramNumber(ushort handle, out int programNumber);
}

internal readonly record struct FocasStatInfo(
    short Aut,
    short Run,
    short Emergency,
    short Alarm);

internal readonly record struct FocasSignalSnapshot(
    bool HasSpindle,
    int Spindle,
    bool HasFeed,
    int Feed,
    bool HasAxes,
    double AxisX,
    double AxisY,
    double AxisZ,
    bool HasSystem,
    string SystemType,
    string SoftwareVersion,
    int AxisCount,
    bool HasMainProgram,
    int MainProgram);

internal interface IFocasSignals
{
    FocasSignalSnapshot Read(ushort handle, in FocasStatInfo status);
}
