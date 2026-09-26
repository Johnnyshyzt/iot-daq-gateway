using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Text.Json;
using Studio.Contracts;
using Xunit;

namespace Studio.Tests;

[Collection("studio-host")]
public sealed class DriverApiTests : IClassFixture<StudioApiFactory>
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private readonly StudioApiFactory _factory;

    public DriverApiTests(StudioApiFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Unsaved_modbus_test_returns_samples_and_timing()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        using var serving = new CancellationTokenSource(TimeSpan.FromSeconds(8));
        var serve = Task.Run(() => ServeModbus(listener, serving.Token));
        using var client = _factory.CreateClient();
        await Authorize(client);

        var test = await Read<DeviceTestResult>(await client.PostAsJsonAsync("/api/v1/devices/test", new DeviceDocument
        {
            Metadata = new DeviceMetadata { Id = "unsaved-delta", DisplayName = "未保存台达" },
            Spec = new DeviceSpec
            {
                Adapter = "delta.modbus",
                BrandId = "delta",
                Enabled = true,
                IntervalMs = 1000,
                Connection = new DeviceConnection
                {
                    Host = "127.0.0.1",
                    Port = port,
                    TimeoutMs = 2000,
                    Parameters = new Dictionary<string, string> { ["unitId"] = "1" }
                }
            }
        }, Json));

        serving.Cancel();
        Assert.True(test.Ok, test.Message + " " + test.Error);
        Assert.True(test.Reachable);
        Assert.True(test.Handshake);
        Assert.True(test.LatencyMs >= 0);
        Assert.Contains(test.Samples, sample => sample.Point == "state" && sample.Value == "RUNNING");
        await serve;
    }

    [Fact]
    public async Task Unsaved_sdk_driver_reports_the_missing_directory()
    {
        var directory = Directory.CreateTempSubdirectory("studio-sdk");
        using var client = _factory.CreateClient();
        await Authorize(client);
        var test = await Read<DeviceTestResult>(await client.PostAsJsonAsync("/api/v1/devices/test", new DeviceDocument
        {
            Metadata = new DeviceMetadata { Id = "unsaved-syntec", DisplayName = "新代" },
            Spec = new DeviceSpec
            {
                Adapter = "syntec.custom",
                BrandId = "syntec",
                Enabled = true,
                IntervalMs = 1000,
                Connection = new DeviceConnection
                {
                    Host = "127.0.0.1",
                    Port = 1,
                    TimeoutMs = 400,
                    Parameters = new Dictionary<string, string> { ["sdkPath"] = directory.FullName }
                }
            }
        }, Json));

        Assert.False(test.Ok);
        Assert.False(test.Handshake);
        Assert.Equal("missing", test.SdkStatus);
        Assert.Contains("SDK 未安装", test.Message, StringComparison.Ordinal);
        Assert.Contains(directory.FullName, test.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Collection_toggle_updates_published_without_a_full_publish()
    {
        using var client = _factory.CreateClient();
        await Authorize(client);
        var device = new DeviceDocument
        {
            Metadata = new DeviceMetadata { Id = "collect-probe", DisplayName = "启停" },
            Spec = new DeviceSpec
            {
                Adapter = "fanuc.sim",
                BrandId = "fanuc",
                Enabled = true,
                IntervalMs = 1000,
                PointTemplateId = "fanuc-catalog",
                Connection = new DeviceConnection { Host = "127.0.0.1", Port = 8193, TimeoutMs = 1000 }
            }
        };
        await Read<DeviceDocument>(await client.PutAsJsonAsync("/api/v1/config/devices/collect-probe", device, Json));
        var published = await Read<PublishResult>(await client.PostAsJsonAsync("/api/v1/config/publish", new PublishRequest { Note = "drivers" }, Json));
        Assert.False(string.IsNullOrWhiteSpace(published.Revision));

        var stopped = await Read<CollectionResult>(await client.PostAsJsonAsync(
            "/api/v1/devices/collect-probe/collection",
            new CollectionRequest { Enabled = false },
            Json));
        Assert.False(stopped.Enabled);
        Assert.True(stopped.Reloaded);

        var current = await Read<DeviceDocument>(await client.GetAsync("/api/v1/config/devices/collect-probe"));
        Assert.False(current.Spec.Enabled);
        var revisions = await Read<RevisionList>(await client.GetAsync("/api/v1/config/revisions"));
        Assert.Contains(revisions.Revisions, item => item.Action == "collection");
    }

    [Fact]
    public async Task Catalog_support_lists_levels_for_a_brand()
    {
        using var client = _factory.CreateClient();
        await Authorize(client);
        var support = await Read<SupportReport>(await client.GetAsync("/api/v1/catalog/support?brandId=siemens"));
        var opc = Assert.Single(support.Drivers, driver => driver.AdapterId == "siemens.opcua");
        Assert.Contains(opc.Items, item => item.ItemId == "state" && item.Level == "supported");
        Assert.Contains(opc.Items, item => item.ItemId == "cycleTime" && item.Level == "notAvailable");
    }

    private static async Task ServeModbus(TcpListener listener, CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var client = await listener.AcceptTcpClientAsync(cancellationToken);
                _ = AnswerModbus(client, cancellationToken);
            }
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            listener.Stop();
        }
    }

    private static async Task AnswerModbus(TcpClient client, CancellationToken cancellationToken)
    {
        using (client)
        await using (var stream = client.GetStream())
        {
            var request = new byte[12];
            var offset = 0;
            while (offset < request.Length)
            {
                var read = await stream.ReadAsync(request.AsMemory(offset), cancellationToken);
                if (read == 0)
                {
                    return;
                }

                offset += read;
            }

            var count = System.Buffers.Binary.BinaryPrimitives.ReadUInt16BigEndian(request.AsSpan(10));
            var dataBytes = count * 2;
            var length = (ushort)(3 + dataBytes);
            var response = new byte[6 + length];
            response[0] = request[0];
            response[1] = request[1];
            response[4] = (byte)(length >> 8);
            response[5] = (byte)length;
            response[6] = request[6];
            response[7] = request[7];
            response[8] = (byte)dataBytes;
            if (count > 0)
            {
                response[9] = 0;
                response[10] = 1;
            }

            await stream.WriteAsync(response, cancellationToken);
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

    private sealed class SupportReport
    {
        public string BrandId { get; set; } = "";

        public List<SupportDriver> Drivers { get; set; } = [];
    }

    private sealed class SupportDriver
    {
        public string AdapterId { get; set; } = "";

        public List<SupportItem> Items { get; set; } = [];
    }

    private sealed class SupportItem
    {
        public string ItemId { get; set; } = "";

        public string Level { get; set; } = "";
    }
}
