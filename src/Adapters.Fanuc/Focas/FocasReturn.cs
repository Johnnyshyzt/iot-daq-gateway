namespace Adapters.Fanuc.Focas;

/// <summary>
/// FOCAS Data Window return codes used by the Ethernet APIs this adapter calls.
/// </summary>
internal static class FocasReturn
{
    public const short Protocol = -17;
    public const short Socket = -16;
    public const short NoDll = -15;
    public const short Handle = -8;
    public const short Version = -7;
    public const short Unexpected = -6;
    public const short Reset = -2;
    public const short Busy = -1;
    public const short Ok = 0;
    public const short Func = 1;
    public const short Length = 2;
    public const short Number = 3;
    public const short Attrib = 4;

    public static bool IsSessionLost(short rc) =>
        rc is Protocol or Socket or NoDll or Handle or Unexpected or Reset;

    public static string Describe(short rc) => rc switch
    {
        Ok => "EW_OK",
        Protocol => "EW_PROTOCOL (FOCAS protocol error)",
        Socket => "EW_SOCKET (CNC unreachable or connection dropped)",
        NoDll => "EW_NODLL (FOCAS library missing or failed to load)",
        Handle => "EW_HANDLE (invalid library handle)",
        Version => "EW_VERSION (CNC/library version mismatch)",
        Unexpected => "EW_UNEXP (unexpected FOCAS error)",
        Reset => "EW_RESET (CNC reset during call)",
        Busy => "EW_BUSY",
        Func => "EW_FUNC (function not supported or not ready)",
        Length => "EW_LENGTH",
        Number => "EW_NUMBER",
        Attrib => "EW_ATTRIB",
        _ => $"FOCAS error {rc}"
    };
}
