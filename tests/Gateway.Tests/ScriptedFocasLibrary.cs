using Adapters.Fanuc.Focas;

namespace Gateway.Tests;

internal sealed class ScriptedFocasLibrary : IFocasLibrary
{
    private readonly Queue<short> _allocate = new();
    private readonly Queue<short> _status = new();

    public bool Available { get; set; } = true;

    private FocasLibraryProblem _availabilityProblem = FocasLibraryProblem.MissingLibrary;

    public FocasLibraryProblem AvailabilityProblem
    {
        get => Available ? FocasLibraryProblem.None : _availabilityProblem;
        set => _availabilityProblem = value;
    }

    public string UnavailableReason { get; set; } =
        "Missing Fwlib64.dll next to the process. Licensed FANUC FOCAS libraries are not shipped.";

    public FocasStatInfo Status { get; set; } = new(Aut: 1, Run: 3, Emergency: 0, Alarm: 0);

    public int AlarmNumber { get; set; }

    public int ProgramNumber { get; set; } = 1;

    public short AlarmResult { get; set; } = FocasReturn.Ok;

    public short ProgramResult { get; set; } = FocasReturn.Ok;

    public int AllocateCalls { get; private set; }

    public int FreeCalls { get; private set; }

    public void EnqueueAllocate(params short[] codes)
    {
        foreach (var code in codes)
        {
            _allocate.Enqueue(code);
        }
    }

    public void EnqueueStatus(params short[] codes)
    {
        foreach (var code in codes)
        {
            _status.Enqueue(code);
        }
    }

    public bool TryGetAvailability(out string error)
    {
        error = Available ? string.Empty : UnavailableReason;
        return Available;
    }

    public short AllocateHandle(string ipAddress, ushort port, int timeoutSeconds, out ushort handle)
    {
        AllocateCalls++;
        _ = ipAddress;
        _ = port;
        _ = timeoutSeconds;
        var rc = Dequeue(_allocate, FocasReturn.Ok);
        handle = rc == FocasReturn.Ok ? (ushort)1 : (ushort)0;
        return rc;
    }

    public short SetTimeout(ushort handle, int timeoutSeconds)
    {
        _ = handle;
        _ = timeoutSeconds;
        return FocasReturn.Ok;
    }

    public short FreeHandle(ushort handle)
    {
        _ = handle;
        FreeCalls++;
        return FocasReturn.Ok;
    }

    public short ReadStatus(ushort handle, out FocasStatInfo status)
    {
        _ = handle;
        status = Status;
        return Dequeue(_status, FocasReturn.Ok);
    }

    public short ReadFirstAlarmNumber(ushort handle, out int alarmNumber)
    {
        _ = handle;
        alarmNumber = AlarmNumber;
        return AlarmResult;
    }

    public short ReadProgramNumber(ushort handle, out int programNumber)
    {
        _ = handle;
        programNumber = ProgramNumber;
        return ProgramResult;
    }

    private static short Dequeue(Queue<short> queue, short fallback) =>
        queue.Count > 0 ? queue.Dequeue() : fallback;
}
