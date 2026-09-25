using Adapters.Fanuc.Focas;
using Gateway.Abstractions.Models;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Gateway.Tests;

public sealed class FocasFanucAdapterTests
{
    private static readonly Dictionary<string, object?> Options = new(StringComparer.OrdinalIgnoreCase)
    {
        ["host"] = "192.168.1.10",
        ["port"] = 8193,
        ["timeoutMs"] = 3000
    };

    [Fact]
    public async Task MissingLibrary_CollectsNothing_StaysOffline_DoesNotThrow()
    {
        var library = new ScriptedFocasLibrary { Available = false };
        var adapter = Create(library);

        await adapter.ConnectAsync(CancellationToken.None);
        var points = await adapter.CollectAsync(CancellationToken.None);
        var health = await adapter.GetHealthAsync(CancellationToken.None);

        Assert.Empty(points);
        Assert.Equal(AdapterStatus.Offline, health.Status);
        Assert.Contains("Fwlib64", health.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(0, library.AllocateCalls);
        await adapter.DisposeAsync();
    }

    [Fact]
    public async Task NativeLibraryMissing_DoesNotCrashProcess()
    {
        var adapter = new FocasFanucAdapter("cnc-01", Options, NullLogger<FocasFanucAdapter>.Instance);

        await adapter.ConnectAsync(CancellationToken.None);
        var points = await adapter.CollectAsync(CancellationToken.None);
        var health = await adapter.GetHealthAsync(CancellationToken.None);

        Assert.Empty(points);
        Assert.Equal(AdapterStatus.Offline, health.Status);
        Assert.False(string.IsNullOrWhiteSpace(health.Message));
        Assert.Contains("Fwlib64", health.Message, StringComparison.OrdinalIgnoreCase);
        await adapter.DisposeAsync();
    }

    [Fact]
    public async Task FailedConnect_RetriesOnLaterCollect()
    {
        var library = new ScriptedFocasLibrary();
        library.EnqueueAllocate(FocasReturn.Socket, FocasReturn.Ok);
        var adapter = Create(library);

        await adapter.ConnectAsync(CancellationToken.None);
        var afterConnect = await adapter.GetHealthAsync(CancellationToken.None);

        Assert.Equal(AdapterStatus.Offline, afterConnect.Status);
        Assert.Contains("EW_SOCKET", afterConnect.Message, StringComparison.Ordinal);
        Assert.Equal(1, library.AllocateCalls);

        var recovered = await adapter.CollectAsync(CancellationToken.None);
        var recoveredHealth = await adapter.GetHealthAsync(CancellationToken.None);

        Assert.Equal(2, library.AllocateCalls);
        Assert.Equal(3, recovered.Count);
        Assert.Equal(AdapterStatus.Online, recoveredHealth.Status);
        Assert.Equal("RUNNING", Point(recovered, "state"));
        Assert.Equal(0, Point(recovered, "alarm"));
        Assert.Equal("O0001", Point(recovered, "program"));
        await adapter.DisposeAsync();
    }

    [Fact]
    public async Task DroppedSession_FreesHandle_AndReconnectsNextSweep()
    {
        var library = new ScriptedFocasLibrary
        {
            Status = new FocasStatInfo(Aut: 1, Run: 3, Emergency: 0, Alarm: 0),
            ProgramNumber = 12
        };
        library.EnqueueStatus(FocasReturn.Ok, FocasReturn.Socket, FocasReturn.Ok);
        var adapter = Create(library);

        var first = await adapter.CollectAsync(CancellationToken.None);
        Assert.Equal("O0012", Point(first, "program"));
        Assert.Equal(AdapterStatus.Online, (await adapter.GetHealthAsync(CancellationToken.None)).Status);

        var second = await adapter.CollectAsync(CancellationToken.None);
        Assert.Empty(second);
        Assert.Equal(AdapterStatus.Offline, (await adapter.GetHealthAsync(CancellationToken.None)).Status);
        Assert.Equal(1, library.FreeCalls);

        var third = await adapter.CollectAsync(CancellationToken.None);
        Assert.Equal(2, library.AllocateCalls);
        Assert.Equal(AdapterStatus.Online, (await adapter.GetHealthAsync(CancellationToken.None)).Status);
        Assert.Equal("RUNNING", Point(third, "state"));
        await adapter.DisposeAsync();
    }

    [Fact]
    public async Task AlarmStatus_MapsToAlarmPoint()
    {
        var library = new ScriptedFocasLibrary
        {
            Status = new FocasStatInfo(Aut: 1, Run: 1, Emergency: 0, Alarm: 1),
            AlarmNumber = 100,
            ProgramNumber = 1
        };
        var adapter = Create(library);

        var points = await adapter.CollectAsync(CancellationToken.None);

        Assert.Equal("ALARM", Point(points, "state"));
        Assert.Equal(100, Point(points, "alarm"));
        Assert.Equal("uncertain", points.Single(p => p.Point == "state").Quality);
        await adapter.DisposeAsync();
    }

    [Fact]
    public async Task CollectException_DoesNotThrow_GoesOffline()
    {
        var library = new ThrowingFocasLibrary();
        var adapter = Create(library);

        var points = await adapter.CollectAsync(CancellationToken.None);
        var health = await adapter.GetHealthAsync(CancellationToken.None);

        Assert.Empty(points);
        Assert.Equal(AdapterStatus.Offline, health.Status);
        Assert.Contains("boom", health.Message, StringComparison.OrdinalIgnoreCase);
        await adapter.DisposeAsync();
    }

    private static FocasFanucAdapter Create(IFocasLibrary library) =>
        new("cnc-01", Options, NullLogger<FocasFanucAdapter>.Instance, library);

    private static object? Point(IReadOnlyList<Observation> points, string name) =>
        points.Single(p => p.Point == name).Value;

    private sealed class ThrowingFocasLibrary : IFocasLibrary
    {
        public bool TryGetAvailability(out string error)
        {
            error = string.Empty;
            return true;
        }

        public FocasLibraryProblem AvailabilityProblem => FocasLibraryProblem.None;

        public short AllocateHandle(string ipAddress, ushort port, int timeoutSeconds, out ushort handle)
        {
            handle = 1;
            return FocasReturn.Ok;
        }

        public short SetTimeout(ushort handle, int timeoutSeconds) => FocasReturn.Ok;

        public short FreeHandle(ushort handle) => FocasReturn.Ok;

        public short ReadStatus(ushort handle, out FocasStatInfo status)
        {
            throw new InvalidOperationException("boom");
        }

        public short ReadFirstAlarmNumber(ushort handle, out int alarmNumber)
        {
            alarmNumber = 0;
            return FocasReturn.Ok;
        }

        public short ReadProgramNumber(ushort handle, out int programNumber)
        {
            programNumber = 0;
            return FocasReturn.Ok;
        }
    }
}
