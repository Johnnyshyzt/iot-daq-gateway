using System.Net;
using System.Net.Sockets;
using Adapters.Fanuc.Fake;
using Gateway.Host.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using MQTTnet.Server;
using Sinks.Mqtt;
using Xunit;

namespace Gateway.Tests;

public sealed class V1BundleTests
{
    [Fact]
    public void Loads_example_bundle_without_plaintext_secrets()
    {
        var directory = Path.Combine(AppContext.BaseDirectory, "examples", "v1");
        var loaded = V1BundleLoader.Load(directory);
        var config = loaded.Configuration;

        Assert.True(loaded.IsBundle);
        Assert.Equal("plant-a", config.Gateway.Site);
        Assert.Equal("gw-plant-a", config.Gateway.Id);
        Assert.Equal("Plant A Gateway", loaded.DisplayName);
        Assert.False(config.ProgramTransfer.Enabled);
        Assert.True(config.Pipeline.ChangeOnly);
        Assert.Equal(TimeSpan.FromSeconds(1), config.Pipeline.SweepInterval);
        Assert.Equal("127.0.0.1", config.Mqtt.Host);
        Assert.Equal(1883, config.Mqtt.Port);
        Assert.Equal(EnvironmentOrNull("MQTT_USER"), config.Mqtt.Username);
        Assert.Equal(EnvironmentOrNull("MQTT_PASSWORD"), config.Mqtt.Password);

        var device = Assert.Single(config.Devices);
        Assert.Equal("cnc-01", device.Id);
        Assert.Equal("fanuc.fake", device.Adapter);
        Assert.Equal("192.168.1.10", device.Options["host"]?.ToString());
        Assert.Equal(8193, device.Options["port"]);
        Assert.Equal(3000, device.Options["timeoutMs"]);
        Assert.Equal("state,alarm,program", device.Options["points"]?.ToString());
    }

    [Fact]
    public void Single_file_yaml_stays_on_the_legacy_loader()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "examples", "gateway.yaml");
        var loaded = GatewayConfigLoader.Load(path);
        Assert.False(loaded.IsBundle);
        Assert.False(string.IsNullOrWhiteSpace(loaded.Configuration.Gateway.Id));
        Assert.NotEmpty(loaded.Configuration.Devices);
    }

    [Fact]
    public void Mappings_directory_is_rejected()
    {
        var source = Path.Combine(AppContext.BaseDirectory, "examples", "v1");
        var directory = Directory.CreateTempSubdirectory("v1-mappings").FullName;
        CopyDirectory(source, directory);
        Directory.CreateDirectory(Path.Combine(directory, "mappings"));
        File.WriteAllText(Path.Combine(directory, "mappings", "extra.yaml"), "kind: Mapping\n");

        var error = Assert.Throws<InvalidOperationException>(() => V1BundleLoader.Load(directory));
        Assert.Contains("mappings/", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Fake_adapter_emits_configured_points()
    {
        var adapter = new FakeFanucAdapter(
            "cnc-01",
            new Dictionary<string, object?> { ["points"] = "state,spindle" },
            NullLogger<FakeFanucAdapter>.Instance);
        await adapter.ConnectAsync(CancellationToken.None);
        var observations = await adapter.CollectAsync(CancellationToken.None);
        Assert.Equal(["state", "spindle"], observations.Select(item => item.Point).ToArray());
        Assert.Equal(0, observations.Single(item => item.Point == "spindle").Value);
    }

    [Fact]
    public async Task Published_v1_config_reaches_mqtt()
    {
        var port = FreeTcpPort();
        var factory = new MqttServerFactory();
        var serverOptions = new MqttServerOptionsBuilder()
            .WithDefaultEndpoint()
            .WithDefaultEndpointPort(port)
            .Build();
        var server = factory.CreateMqttServer(serverOptions);
        var received = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        server.InterceptingPublishAsync += args =>
        {
            if (args.ApplicationMessage.Topic == "daq/plant-a/cnc-01/state")
            {
                received.TrySetResult(args.ApplicationMessage.Topic);
            }

            return Task.CompletedTask;
        };

        await server.StartAsync();
        try
        {
            var loaded = V1BundleLoader.Load(Path.Combine(AppContext.BaseDirectory, "examples", "v1"));
            loaded.Configuration.Mqtt.Port = port;
            loaded.Configuration.Mqtt.ClientId = "v1-test-" + Guid.NewGuid().ToString("N");
            var device = loaded.Configuration.Devices[0];
            await using var sink = new MqttObservationSink(loaded.Configuration, NullLogger<MqttObservationSink>.Instance);
            await sink.StartAsync(CancellationToken.None);
            var adapter = new FakeFanucAdapter(device.Id, device.Options, NullLogger<FakeFanucAdapter>.Instance);
            await adapter.ConnectAsync(CancellationToken.None);
            foreach (var observation in await adapter.CollectAsync(CancellationToken.None))
            {
                await sink.PublishObservationAsync(observation, CancellationToken.None);
            }

            var topic = await received.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal("daq/plant-a/cnc-01/state", topic);
        }
        finally
        {
            await server.StopAsync();
            server.Dispose();
        }
    }

    private static string? EnvironmentOrNull(string name)
    {
        var value = Environment.GetEnvironmentVariable(name);
        return string.IsNullOrEmpty(value) ? null : value;
    }

    private static int FreeTcpPort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        return ((IPEndPoint)listener.LocalEndpoint).Port;
    }

    private static void CopyDirectory(string source, string destination)
    {
        Directory.CreateDirectory(destination);
        foreach (var directory in Directory.GetDirectories(source, "*", SearchOption.AllDirectories))
        {
            Directory.CreateDirectory(directory.Replace(source, destination, StringComparison.Ordinal));
        }

        foreach (var file in Directory.GetFiles(source, "*", SearchOption.AllDirectories))
        {
            var target = file.Replace(source, destination, StringComparison.Ordinal);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target, overwrite: true);
        }
    }
}
