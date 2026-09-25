using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using MQTTnet.Server;
using Studio.Contracts;
using Xunit;

namespace Studio.Tests;

[Collection("studio-host")]
public sealed class InProcessAcquisitionTests
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    [Fact]
    public async Task Publish_reloads_fake_fanuc_and_mqtt_in_process()
    {
        var port = FreeTcpPort();
        var data = Directory.CreateTempSubdirectory("host-acquire").FullName;
        CopySeed(FindSeed(), Path.Combine(data, "seed"));
        var mqttPath = Path.Combine(data, "seed", "sinks", "mqtt.yaml");
        var mqtt = File.ReadAllText(mqttPath)
            .Replace("port: 1883", $"port: {port}", StringComparison.Ordinal)
            .Replace("clientId: iot-daq-gateway", "clientId: host-acquire-" + Guid.NewGuid().ToString("N")[..8], StringComparison.Ordinal);
        File.WriteAllText(mqttPath, mqtt);

        var factory = new MqttServerFactory();
        var server = factory.CreateMqttServer(new MqttServerOptionsBuilder()
            .WithDefaultEndpoint()
            .WithDefaultEndpointPort(port)
            .Build());
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

        await using var host = new AcquisitionFactory(data);
        try
        {
            using var client = host.CreateClient();
            await Authorize(client);
            var topic = await received.Task.WaitAsync(TimeSpan.FromSeconds(20));
            Assert.Equal("daq/plant-a/cnc-01/state", topic);

            var before = await Read<RuntimeStatus>(await client.GetAsync("/api/v1/runtime/status"));
            Assert.Equal("live", before.Mode);
            Assert.Contains(before.Devices, device => device.Id == "cnc-01");

            await Authorize(client);
            var device = await Read<DeviceDocument>(await client.GetAsync("/api/v1/config/devices/cnc-01"));
            device.Metadata.DisplayName = "In-process lathe";
            await Read<DeviceDocument>(await client.PutAsJsonAsync("/api/v1/config/devices/cnc-01", device, Json));
            var published = await Read<PublishResult>(await client.PostAsJsonAsync(
                "/api/v1/config/publish",
                new PublishRequest { Note = "in-process" },
                Json));

            var after = await Read<RuntimeStatus>(await client.GetAsync("/api/v1/runtime/status"));
            Assert.Equal("live", after.Mode);
            Assert.Equal(published.Revision, after.ActiveRevision);
            Assert.Contains(after.Devices, item => item.Id == "cnc-01" && item.DisplayName == "In-process lathe");
        }
        finally
        {
            await server.StopAsync();
            if (Directory.Exists(data))
            {
                Directory.Delete(data, recursive: true);
            }
        }
    }

    private static async Task Authorize(HttpClient client)
    {
        var response = await client.PostAsJsonAsync("/api/v1/auth/login", new LoginRequest
        {
            Username = "engineer",
            Password = "engineer"
        }, Json);
        var login = await Read<LoginResponse>(response);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", login.Token);
    }

    private static async Task<T> Read<T>(HttpResponseMessage response)
    {
        var json = await response.Content.ReadAsStringAsync();
        Assert.True(response.IsSuccessStatusCode, $"{(int)response.StatusCode} {json}");
        var body = JsonSerializer.Deserialize<T>(json, Json);
        Assert.NotNull(body);
        return body;
    }

    private static int FreeTcpPort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    private static string FindSeed()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, "data", "seed", "gateway.yaml");
            if (File.Exists(candidate))
            {
                return Path.Combine(dir.FullName, "data", "seed");
            }

            dir = dir.Parent;
        }

        throw new DirectoryNotFoundException("data/seed was not found from the test output directory.");
    }

    private static void CopySeed(string source, string destination)
    {
        foreach (var file in Directory.GetFiles(source, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(source, file);
            var target = Path.Combine(destination, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target);
        }
    }

    private sealed class AcquisitionFactory : WebApplicationFactory<Program>
    {
        private readonly string _dataDirectory;

        public AcquisitionFactory(string dataDirectory)
        {
            _dataDirectory = dataDirectory;
        }

        protected override void ConfigureWebHost(Microsoft.AspNetCore.Hosting.IWebHostBuilder builder)
        {
            builder.UseSetting("Studio:DataDirectory", _dataDirectory);
            builder.UseSetting("Host:Acquisition", "on");
            builder.UseSetting("Studio:GatewayLoopback", "off");
        }
    }
}
