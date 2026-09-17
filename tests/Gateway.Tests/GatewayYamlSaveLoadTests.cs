using Adapters.Fanuc;
using Gateway.Abstractions.Configuration;
using Gateway.Abstractions.Contracts;
using Gateway.Host.Acquisition;
using Gateway.Host.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Gateway.Tests;

public sealed class GatewayYamlSaveLoadTests
{
    [Fact]
    public void SaveThenLoad_RoundtripsGatewayMqttSweepAndDevices()
    {
        var path = CopyExample("gateway.yaml");
        var config = GatewayYamlLoader.Load(path);

        config.Gateway.Id = "gw-line-02";
        config.Gateway.Site = "plant-b";
        config.Mqtt.Host = "10.0.0.8";
        config.Mqtt.Port = 1884;
        config.Pipeline.SweepInterval = TimeSpan.FromSeconds(5);
        config.Devices.Add(new DeviceBinding
        {
            Id = "cnc-02",
            Enabled = true,
            Adapter = "fanuc.focas",
            Options = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
            {
                ["host"] = "10.0.0.2",
                ["port"] = 8193,
                ["timeoutMs"] = 3000
            }
        });

        GatewayYamlLoader.Save(path, config);
        var loaded = GatewayYamlLoader.Load(path);

        Assert.Equal("gw-line-02", loaded.Gateway.Id);
        Assert.Equal("plant-b", loaded.Gateway.Site);
        Assert.Equal("10.0.0.8", loaded.Mqtt.Host);
        Assert.Equal(1884, loaded.Mqtt.Port);
        Assert.Equal(TimeSpan.FromSeconds(5), loaded.Pipeline.SweepInterval);
        Assert.Equal(2, loaded.Devices.Count);
        var focas = loaded.Devices.Single(d => d.Id == "cnc-02");
        Assert.Equal("fanuc.focas", focas.Adapter);
        Assert.True(focas.Enabled);
        Assert.Equal("10.0.0.2", OptionReader.GetString(focas.Options, "host", ""));
        Assert.Equal(8193, OptionReader.GetInt(focas.Options, "port", 0));
        Assert.Contains("不必重新打包", File.ReadAllText(path), StringComparison.Ordinal);
        Assert.DoesNotContain("username:", File.ReadAllText(path), StringComparison.Ordinal);
    }

    [Fact]
    public void SaveThenLoad_CanRemoveAndDisableDevices()
    {
        var path = CopyExample("gateway.yaml");
        var config = GatewayYamlLoader.Load(path);
        config.Devices.Clear();
        config.Devices.Add(new DeviceBinding
        {
            Id = "cnc-keep",
            Enabled = false,
            Adapter = "fanuc.fake",
            Options = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
            {
                ["host"] = "192.168.1.20",
                ["port"] = 8193,
                ["timeoutMs"] = 3000
            }
        });

        GatewayYamlLoader.Save(path, config);
        var loaded = GatewayYamlLoader.Load(path);

        var device = Assert.Single(loaded.Devices);
        Assert.Equal("cnc-keep", device.Id);
        Assert.False(device.Enabled);
        Assert.DoesNotContain(loaded.Devices, d => d.Id == "cnc-01");
    }

    [Fact]
    public void Save_RejectsDuplicateDeviceIds()
    {
        var path = CopyExample("gateway.yaml");
        var config = GatewayYamlLoader.Load(path);
        config.Devices.Add(new DeviceBinding
        {
            Id = "cnc-01",
            Adapter = "fanuc.fake"
        });

        var ex = Assert.Throws<InvalidOperationException>(() => GatewayYamlLoader.Save(path, config));
        Assert.Contains("duplicate device id", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ConsoleMapper_MergePreservesMqttPasswordAndTimeout()
    {
        var path = CopyExample("gateway.yaml");
        var baseline = GatewayYamlLoader.Load(path);
        baseline.Mqtt.Username = "daq";
        baseline.Mqtt.Password = "secret";
        baseline.Devices[0].Options["timeoutMs"] = 4500;

        var document = ConsoleConfigMapper.ToDocument(baseline);
        document.Site = "plant-c";
        document.MqttHost = "mqtt.factory.local";
        document.Devices.Add(new ConsoleDeviceDocument
        {
            Id = "cnc-03",
            Enabled = true,
            Adapter = "fanuc.focas",
            Host = "192.168.1.30",
            Port = 8193
        });

        var merged = ConsoleConfigMapper.Merge(baseline, document);
        Assert.Equal("plant-c", merged.Gateway.Site);
        Assert.Equal("mqtt.factory.local", merged.Mqtt.Host);
        Assert.Equal("daq", merged.Mqtt.Username);
        Assert.Equal("secret", merged.Mqtt.Password);
        Assert.Equal(4500, OptionReader.GetInt(merged.Devices.Single(d => d.Id == "cnc-01").Options, "timeoutMs", 0));
        Assert.Equal(ConsoleConfigMapper.DefaultDeviceTimeoutMs,
            OptionReader.GetInt(merged.Devices.Single(d => d.Id == "cnc-03").Options, "timeoutMs", 0));
    }

    [Fact]
    public void PipelineManager_ApplyWritesYamlAndBumpsGeneration()
    {
        var path = CopyExample("gateway.yaml");
        var initial = GatewayYamlLoader.Load(path);
        using var provider = NewFanucProvider();
        var manager = new PipelineManager(
            path,
            initial,
            provider.GetServices<ISouthboundAdapterFactory>(),
            NullLoggerFactory.Instance,
            NullLogger<PipelineManager>.Instance);

        Assert.Equal(0, manager.Generation);
        var next = ConsoleConfigMapper.Merge(initial, new ConsoleConfigDocument
        {
            GatewayId = initial.Gateway.Id,
            Site = "applied-site",
            MqttHost = "127.0.0.1",
            MqttPort = 1883,
            MqttClientId = initial.Mqtt.ClientId,
            SweepIntervalSeconds = 3,
            Devices =
            [
                new ConsoleDeviceDocument
                {
                    Id = "cnc-01",
                    Enabled = true,
                    Adapter = "fanuc.fake",
                    Host = "192.168.1.10",
                    Port = 8193
                },
                new ConsoleDeviceDocument
                {
                    Id = "cnc-02",
                    Enabled = true,
                    Adapter = "fanuc.focas",
                    Host = "192.168.1.11",
                    Port = 8193
                }
            ]
        });

        manager.ApplyConfiguration(next);

        Assert.Equal(1, manager.Generation);
        Assert.Equal("applied-site", manager.Current.Gateway.Site);
        var reloaded = GatewayYamlLoader.Load(path);
        Assert.Equal("applied-site", reloaded.Gateway.Site);
        Assert.Equal(2, reloaded.Devices.Count);
        Assert.Contains(reloaded.Devices, d => d.Id == "cnc-02" && d.Adapter == "fanuc.focas");
    }

    private static string CopyExample(string fileName)
    {
        var source = Path.Combine(AppContext.BaseDirectory, "examples", fileName);
        var dir = Path.Combine(Path.GetTempPath(), "iot-daq-yaml-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, "gateway.yaml");
        File.Copy(source, path);
        return path;
    }

    private static ServiceProvider NewFanucProvider()
    {
        var services = new ServiceCollection();
        services.AddSingleton<ILoggerFactory>(NullLoggerFactory.Instance);
        services.AddFanucAdapters();
        return services.BuildServiceProvider();
    }
}

public sealed class ConsoleListenPolicyTests
{
    [Fact]
    public void LoopbackWithoutToken_ListensWithoutAuth()
    {
        var resolved = ConsoleListenPolicy.Resolve(new ConsoleOptions
        {
            Enabled = true,
            Bind = "127.0.0.1",
            Port = 8080,
            Token = ""
        }, _ => null);

        Assert.True(resolved.Listen);
        Assert.False(resolved.AuthRequired);
        Assert.Null(resolved.SkipReason);
    }

    [Fact]
    public void WildcardWithoutToken_DoesNotListen()
    {
        var resolved = ConsoleListenPolicy.Resolve(new ConsoleOptions
        {
            Enabled = true,
            Bind = "0.0.0.0",
            Port = 8080,
            Token = ""
        }, _ => null);

        Assert.False(resolved.Listen);
        Assert.NotNull(resolved.SkipReason);
        Assert.Contains("GATEWAY_CONSOLE_TOKEN", resolved.SkipReason, StringComparison.Ordinal);
    }

    [Fact]
    public void WildcardWithToken_ListensWithAuth()
    {
        var resolved = ConsoleListenPolicy.Resolve(new ConsoleOptions
        {
            Enabled = true,
            Bind = "0.0.0.0",
            Port = 8080,
            Token = "change-me"
        }, _ => null);

        Assert.True(resolved.Listen);
        Assert.True(resolved.AuthRequired);
        Assert.Equal("change-me", resolved.Token);
        Assert.Equal(8080, resolved.Port);
    }

    [Fact]
    public void EnvironmentToken_OverridesEmptyYamlOnIntranetBind()
    {
        var resolved = ConsoleListenPolicy.Resolve(
            new ConsoleOptions
            {
                Enabled = true,
                Bind = "0.0.0.0",
                Port = 8080,
                Token = ""
            },
            name => name == ConsoleListenPolicy.TokenEnvironmentVariable ? "from-env" : null);

        Assert.True(resolved.Listen);
        Assert.Equal("from-env", resolved.Token);
    }

    [Fact]
    public void WindowsExample_RequiresTokenForIntranetBind()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "examples", "gateway.windows.yaml");
        var config = GatewayYamlLoader.Load(path);
        Assert.Equal("0.0.0.0", config.Console.Bind);
        Assert.Equal(8080, config.Console.Port);
        Assert.False(string.IsNullOrWhiteSpace(config.Console.Token));

        var resolved = ConsoleListenPolicy.Resolve(config.Console, _ => null);
        Assert.True(resolved.Listen);
        Assert.True(resolved.AuthRequired);
    }
}
