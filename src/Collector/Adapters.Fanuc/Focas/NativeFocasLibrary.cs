namespace Adapters.Fanuc.Focas;

/// <summary>
/// Live <c>Fwlib64.dll</c> calls. Missing/unloadable libraries become FOCAS error codes, never process crashes.
/// </summary>
internal sealed class NativeFocasLibrary : IFocasLibrary
{
    public static NativeFocasLibrary Instance { get; } = new();

    private NativeFocasLibrary()
    {
    }

    public bool TryGetAvailability(out string error) => FocasNative.TryLoad(out error);

    public short AllocateHandle(string ipAddress, ushort port, int timeoutSeconds, out ushort handle)
    {
        try
        {
            return FocasNative.AllocateHandle(ipAddress, port, timeoutSeconds, out handle);
        }
        catch (Exception ex) when (IsLoadFailure(ex))
        {
            handle = 0;
            return FocasReturn.NoDll;
        }
        catch (EntryPointNotFoundException)
        {
            handle = 0;
            return FocasReturn.Func;
        }
    }

    public short SetTimeout(ushort handle, int timeoutSeconds)
    {
        return InvokeOptional(() => FocasNative.SetTimeout(handle, timeoutSeconds));
    }

    public short FreeHandle(ushort handle)
    {
        return InvokeOptional(() => FocasNative.FreeHandle(handle));
    }

    public short ReadStatus(ushort handle, out FocasStatInfo status)
    {
        status = default;
        try
        {
            var rc = FocasNative.ReadStatus(handle, out var odb);
            if (rc == FocasReturn.Ok)
            {
                status = new FocasStatInfo(odb.Aut, odb.Run, odb.Emergency, odb.Alarm);
            }

            return rc;
        }
        catch (Exception ex) when (IsLoadFailure(ex))
        {
            return FocasReturn.NoDll;
        }
        catch (EntryPointNotFoundException)
        {
            return FocasReturn.Func;
        }
    }

    public short ReadFirstAlarmNumber(ushort handle, out int alarmNumber)
    {
        alarmNumber = 0;
        try
        {
            short count = 1;
            var message = new FocasNative.OdbAlmMsg { AlmMsg = string.Empty };
            var rc = FocasNative.ReadAlarmMessage(handle, type: -1, ref count, ref message);
            if (rc == FocasReturn.Attrib)
            {
                return FocasReturn.Ok;
            }

            if (rc == FocasReturn.Ok && count > 0)
            {
                alarmNumber = message.AlmNo;
            }

            return rc;
        }
        catch (Exception ex) when (IsLoadFailure(ex))
        {
            return FocasReturn.NoDll;
        }
        catch (EntryPointNotFoundException)
        {
            return FocasReturn.Func;
        }
    }

    public short ReadProgramNumber(ushort handle, out int programNumber)
    {
        programNumber = 0;
        try
        {
            try
            {
                var o8 = FocasNative.ReadProgramNumberO8(handle, out var eight);
                if (o8 == FocasReturn.Ok)
                {
                    programNumber = eight.Data;
                    return o8;
                }

                if (FocasReturn.IsSessionLost(o8))
                {
                    return o8;
                }
            }
            catch (EntryPointNotFoundException)
            {
                // Older libraries only export cnc_rdprgnum.
            }

            var rc = FocasNative.ReadProgramNumber(handle, out var four);
            if (rc == FocasReturn.Ok)
            {
                programNumber = four.Data;
            }

            return rc;
        }
        catch (Exception ex) when (IsLoadFailure(ex))
        {
            return FocasReturn.NoDll;
        }
        catch (EntryPointNotFoundException)
        {
            return FocasReturn.Func;
        }
    }

    private static bool IsLoadFailure(Exception ex) =>
        ex is DllNotFoundException or BadImageFormatException;

    private static short InvokeOptional(Func<short> call)
    {
        try
        {
            return call();
        }
        catch (Exception ex) when (IsLoadFailure(ex))
        {
            return FocasReturn.NoDll;
        }
        catch (EntryPointNotFoundException)
        {
            return FocasReturn.Ok;
        }
    }
}
