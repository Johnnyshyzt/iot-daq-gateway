namespace Adapters.Fanuc.Focas;

/// <summary>
/// Testable FOCAS session operations. Production uses <see cref="NativeFocasLibrary"/>.
/// </summary>
internal interface IFocasLibrary
{
    bool TryGetAvailability(out string error);

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
