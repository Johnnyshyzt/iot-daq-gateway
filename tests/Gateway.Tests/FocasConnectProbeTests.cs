using Adapters.Fanuc.Focas;
using Xunit;

namespace Gateway.Tests;

public sealed class FocasConnectProbeTests
{
    [Fact]
    public void NativeProbe_WithoutFwlib_FailsClearly_DoesNotThrow()
    {
        var result = new FocasConnectProbe().Probe("192.0.2.1", 8193, 1000);

        Assert.False(result.Ok);
        Assert.False(string.IsNullOrWhiteSpace(result.Message));
        Assert.Contains("Fwlib64", result.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("端口可达", result.Message, StringComparison.Ordinal);
        Assert.NotEqual(FocasLibraryProblem.None, FocasNative.LastProblem);
    }

    [Fact]
    public void MissingLibrary_ReturnsChineseReason_WithoutAllocate()
    {
        var library = new ScriptedFocasLibrary
        {
            Available = false,
            AvailabilityProblem = FocasLibraryProblem.MissingLibrary
        };

        var result = new FocasConnectProbe(library).Probe("10.0.0.8", 8193, 3000);

        Assert.False(result.Ok);
        Assert.Equal("missing_library", result.Code);
        Assert.Contains("未找到", result.Message, StringComparison.Ordinal);
        Assert.Contains("Fwlib64", result.Message, StringComparison.Ordinal);
        Assert.Equal(0, library.AllocateCalls);
    }

    [Fact]
    public void WrongLibraryArchitecture_ReturnsChineseReason()
    {
        var library = new ScriptedFocasLibrary
        {
            Available = false,
            AvailabilityProblem = FocasLibraryProblem.WrongLibraryArchitecture
        };

        var result = new FocasConnectProbe(library).Probe("10.0.0.8", 8193, 3000);

        Assert.False(result.Ok);
        Assert.Equal("wrong_arch", result.Code);
        Assert.Contains("位数不对", result.Message, StringComparison.Ordinal);
        Assert.Contains("Fwlib64", result.Message, StringComparison.Ordinal);
        Assert.Equal(0, library.AllocateCalls);
    }

    [Fact]
    public void WrongProcessArchitecture_ReturnsChineseReason()
    {
        var library = new ScriptedFocasLibrary
        {
            Available = false,
            AvailabilityProblem = FocasLibraryProblem.WrongProcessArchitecture
        };

        var result = new FocasConnectProbe(library).Probe("10.0.0.8", 8193, 3000);

        Assert.Equal("wrong_arch", result.Code);
        Assert.Contains("不是 64 位", result.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void SocketFailure_IsHandshakeFailure_NotTcpSuccess()
    {
        var library = new ScriptedFocasLibrary();
        library.EnqueueAllocate(FocasReturn.Socket);

        var result = new FocasConnectProbe(library).Probe("10.0.0.8", 8193, 3000);

        Assert.False(result.Ok);
        Assert.Equal("connect_failed", result.Code);
        Assert.Contains("EW_SOCKET", result.Message, StringComparison.Ordinal);
        Assert.Contains("握手", result.Message, StringComparison.Ordinal);
        Assert.Contains("TCP", result.Message, StringComparison.Ordinal);
        Assert.Equal(1, library.AllocateCalls);
        Assert.Equal(0, library.FreeCalls);
    }

    [Fact]
    public void HandshakeSuccess_FreesHandle()
    {
        var library = new ScriptedFocasLibrary();

        var result = new FocasConnectProbe(library).Probe("10.0.0.8", 8193, 3000);

        Assert.True(result.Ok);
        Assert.Equal("ok", result.Code);
        Assert.Contains("cnc_allclibhndl3", result.Message, StringComparison.Ordinal);
        Assert.Equal(1, library.AllocateCalls);
        Assert.Equal(1, library.FreeCalls);
    }

    [Fact]
    public void EmptyHost_DoesNotCallLibrary()
    {
        var library = new ScriptedFocasLibrary();

        var result = new FocasConnectProbe(library).Probe("  ", 8193, 3000);

        Assert.False(result.Ok);
        Assert.Equal("missing_host", result.Code);
        Assert.Contains("机床地址", result.Message, StringComparison.Ordinal);
        Assert.Equal(0, library.AllocateCalls);
    }
}
