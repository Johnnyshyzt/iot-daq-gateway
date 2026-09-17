using System.Runtime.InteropServices;
using Adapters.Fanuc;
using Adapters.Fanuc.Focas;
using Gateway.Abstractions.Contracts;
using Gateway.Abstractions.Models;
using Gateway.Host.Acquisition;
using Gateway.Host.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Gateway.Tests;

public sealed class GatewayYamlLoaderTests
{
    [Theory]
    [InlineData("gateway.yaml")]
    [InlineData("gateway.focas.yaml")]
    [InlineData("gateway.docker.yaml")]
    public void LoadsExampleConfigs(string fileName)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "examples", fileName);
        Assert.True(File.Exists(path), path);

        var config = GatewayYamlLoader.Load(path);

        Assert.False(string.IsNullOrWhiteSpace(config.Gateway.Id));
        Assert.False(string.IsNullOrWhiteSpace(config.Gateway.Site));
        Assert.NotEmpty(config.Devices);
        Assert.Contains(config.Devices, d => d.Enabled);
    }

    [Fact]
    public void FocasExample_KeepsHostPortAndTimeoutInYaml()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "examples", "gateway.focas.yaml");
        var config = GatewayYamlLoader.Load(path);
        var device = config.Devices.Single(d => d.Id == "cnc-01");

        Assert.Equal(FocasFanucAdapter.Kind, device.Adapter);
        Assert.Equal("192.168.1.10", OptionReader.GetString(device.Options, "host", ""));
        Assert.Equal(8193, OptionReader.GetInt(device.Options, "port", 0));
        Assert.Equal(3000, OptionReader.GetInt(device.Options, "timeoutMs", 0));
    }

    [Fact]
    public async Task AdapterFactory_FocasWithoutDll_StaysOfflineWithoutThrowing()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "examples", "gateway.focas.yaml");
        var config = GatewayYamlLoader.Load(path);

        var services = new ServiceCollection();
        services.AddSingleton<ILoggerFactory>(NullLoggerFactory.Instance);
        services.AddFanucAdapters();
        await using var provider = services.BuildServiceProvider();

        var adapters = AdapterFactory.Create(config, provider.GetServices<ISouthboundAdapterFactory>());
        var adapter = Assert.Single(adapters);

        await adapter.ConnectAsync(CancellationToken.None);
        var points = await adapter.CollectAsync(CancellationToken.None);
        var health = await adapter.GetHealthAsync(CancellationToken.None);

        Assert.Empty(points);
        Assert.Equal(AdapterStatus.Offline, health.Status);
        Assert.Contains("Fwlib64", health.Message, StringComparison.OrdinalIgnoreCase);
        await adapter.DisposeAsync();
    }
}

public sealed class FocasNativeLayoutTests
{
    [Fact]
    public void X64StructSizes_MatchFwlibPack4()
    {
        Assert.Equal(18, Marshal.SizeOf<FocasNative.OdbSt>());
        Assert.Equal(8, Marshal.SizeOf<FocasNative.OdbPro>());
        Assert.Equal(12, Marshal.SizeOf<FocasNative.OdbProO8>());
        Assert.Equal(44, Marshal.SizeOf<FocasNative.OdbAlmMsg>());
    }

    [Fact]
    public void TimeoutMs_ConvertsToWholeSeconds()
    {
        Assert.Equal(3, FocasNative.ToTimeoutSeconds(3000));
        Assert.Equal(1, FocasNative.ToTimeoutSeconds(1));
        Assert.Equal(10, FocasNative.ToTimeoutSeconds(0));
    }

    [Fact]
    public void PointMapper_FormatsLikeFakeAdapter()
    {
        Assert.Equal("IDLE", FocasPointMapper.MapState(new FocasStatInfo(1, 0, 0, 0)));
        Assert.Equal("RUNNING", FocasPointMapper.MapState(new FocasStatInfo(1, 3, 0, 0)));
        Assert.Equal("ALARM", FocasPointMapper.MapState(new FocasStatInfo(1, 3, 1, 0)));
        Assert.Equal("O0001", FocasPointMapper.FormatProgram(1));
        Assert.Equal("O12345", FocasPointMapper.FormatProgram(12345));
    }
}
