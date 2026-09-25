using Gateway.Abstractions.Contracts;

namespace Adapters.Fanuc.Focas;

/// <summary>
/// Device-test handshake. Uses <see cref="IFocasLibrary.AllocateHandle"/> (cnc_allclibhndl3), then releases the handle.
/// Does not treat a bare TCP connect as success.
/// </summary>
public sealed class FocasConnectProbe : IFocasConnectProbe
{
    private readonly IFocasLibrary _library;

    public FocasConnectProbe()
        : this(NativeFocasLibrary.Instance)
    {
    }

    internal FocasConnectProbe(IFocasLibrary library)
    {
        _library = library;
    }

    public FocasConnectProbeResult Probe(string host, int port, int timeoutMs)
    {
        if (string.IsNullOrWhiteSpace(host))
        {
            return Fail("missing_host", "未填写机床地址，无法做 FOCAS 握手。");
        }

        if (port is < 1 or > 65535)
        {
            return Fail("invalid_port", $"端口 {port} 无效。FOCAS 常用端口是 8193。");
        }

        if (!_library.TryGetAvailability(out var detail))
        {
            return Fail(CodeFor(_library.AvailabilityProblem), MessageFor(_library.AvailabilityProblem, detail));
        }

        var timeoutSeconds = FocasNative.ToTimeoutSeconds(timeoutMs);
        ushort handle;
        short rc;
        try
        {
            rc = _library.AllocateHandle(host.Trim(), (ushort)port, timeoutSeconds, out handle);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            return Fail("connect_failed", $"FOCAS 握手异常，进程仍在运行：{ex.Message}");
        }

        if (rc != FocasReturn.Ok || handle == 0)
        {
            return Fail(
                "connect_failed",
                $"FOCAS 握手失败：{FocasReturn.DescribeZh(rc)}。地址 {host.Trim()}:{port}。" +
                "不能只根据 TCP 是否连通来判断成功。");
        }

        try
        {
            _library.FreeHandle(handle);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            return new FocasConnectProbeResult
            {
                Ok = true,
                Code = "ok",
                Message =
                    $"FOCAS 握手成功（cnc_allclibhndl3，{host.Trim()}:{port}），但释放句柄时出现：{ex.Message}。" +
                    "这只说明此刻库和机床能完成连接，不代表采集已经保持在线。"
            };
        }

        return new FocasConnectProbeResult
        {
            Ok = true,
            Code = "ok",
            Message =
                $"FOCAS 握手成功（cnc_allclibhndl3，{host.Trim()}:{port}），已释放句柄。" +
                "这只说明此刻库和机床能完成连接，不代表采集已经保持在线。"
        };
    }

    private static string CodeFor(FocasLibraryProblem problem) => problem switch
    {
        FocasLibraryProblem.UnsupportedOperatingSystem => "unsupported_os",
        FocasLibraryProblem.WrongProcessArchitecture => "wrong_arch",
        FocasLibraryProblem.WrongLibraryArchitecture => "wrong_arch",
        FocasLibraryProblem.MissingLibrary => "missing_library",
        FocasLibraryProblem.LoadFailed => "load_failed",
        _ => "load_failed"
    };

    private static string MessageFor(FocasLibraryProblem problem, string detail) => problem switch
    {
        FocasLibraryProblem.UnsupportedOperatingSystem =>
            "当前系统不能加载 Fwlib64.dll（FOCAS 只支持 Windows x64 采集机）。设备测试失败，进程不会退出，也不会把 TCP 通断当成握手成功。",
        FocasLibraryProblem.WrongProcessArchitecture =>
            "当前进程不是 64 位，无法加载 Fwlib64.dll。请使用 win-x64 自包含包。",
        FocasLibraryProblem.WrongLibraryArchitecture =>
            "Fwlib64.dll 位数不对（BadImageFormat）。请把 64 位库放到与 Host.exe 同一目录，不要使用 32 位库。",
        FocasLibraryProblem.MissingLibrary =>
            "未找到 Fwlib64.dll。请把授权的 64 位库放到 Host.exe 同一目录。安装包和仓库都不附带该文件。",
        _ => string.IsNullOrWhiteSpace(detail)
            ? "加载 Fwlib64.dll 失败。请确认它是 64 位、与 Host.exe 放在同一目录，并已安装厂商要求的运行库。"
            : $"加载 Fwlib64.dll 失败：{detail}"
    };

    private static FocasConnectProbeResult Fail(string code, string message) => new()
    {
        Ok = false,
        Code = code,
        Message = message
    };
}
