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

    public static string DescribeZh(short rc) => rc switch
    {
        Ok => "握手成功（EW_OK）",
        Protocol => "FOCAS 协议错误（EW_PROTOCOL）",
        Socket => "无法连接机床或连接被断开（EW_SOCKET）",
        NoDll => "FOCAS 库缺失或加载失败（EW_NODLL）",
        Handle => "库句柄无效（EW_HANDLE）",
        Version => "数控系统或库版本不匹配（EW_VERSION）",
        Unexpected => "FOCAS 返回未预期错误（EW_UNEXP）",
        Reset => "调用过程中数控系统复位（EW_RESET）",
        Busy => "数控系统忙（EW_BUSY）",
        Func => "函数不受支持或尚未就绪（EW_FUNC）",
        Length => "数据长度错误（EW_LENGTH）",
        Number => "数据号错误（EW_NUMBER）",
        Attrib => "数据属性错误（EW_ATTRIB）",
        _ => $"FOCAS 返回未知错误码 {rc}"
    };
}
