using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Studio.Contracts;
using Xunit;

namespace Studio.Tests;

public sealed class StudioApiFactory : WebApplicationFactory<Program>
{
    public string DataDirectory { get; } = Directory.CreateTempSubdirectory("studio-api").FullName;

    public StudioApiFactory()
    {
        Environment.SetEnvironmentVariable("STUDIO_DATA", DataDirectory);
    }

    protected override void ConfigureWebHost(Microsoft.AspNetCore.Hosting.IWebHostBuilder builder)
    {
        builder.UseSetting("Studio:DataDirectory", DataDirectory);
    }

    protected override void Dispose(bool disposing)
    {
        Environment.SetEnvironmentVariable("STUDIO_DATA", null);
        base.Dispose(disposing);
        if (Directory.Exists(DataDirectory))
        {
            Directory.Delete(DataDirectory, recursive: true);
        }
    }
}

public sealed class StudioApiTests : IClassFixture<StudioApiFactory>
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private readonly StudioApiFactory _factory;

    public StudioApiTests(StudioApiFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Create_edit_publish_and_rollback_over_http()
    {
        using var client = _factory.CreateClient();
        var anonymous = await client.GetAsync("/api/v1/config");
        Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);

        await Authorize(client, "engineer", "engineer");

        var device = new DeviceDocument
        {
            Metadata = new DeviceMetadata { Id = "cnc-09", DisplayName = "加工中心 09" },
            Spec = new DeviceSpec
            {
                Adapter = "fanuc.fake",
                Enabled = true,
                IntervalMs = 1500,
                Connection = new DeviceConnection { Host = "192.168.9.9", Port = 8193, FocasTimeoutMs = 3000 }
            }
        };
        var saved = await Read<DeviceDocument>(await client.PutAsJsonAsync("/api/v1/config/devices/cnc-09", device, Json));
        Assert.Equal("cnc-09", saved.Metadata.Id);

        var points = await Read<PointSetDocument>(await client.GetAsync("/api/v1/config/points/cnc-09"));
        Assert.Contains(points.Spec.Points, point => point.Id == "state");
        points.Spec.Points.Add(new PointDefinition
        {
            Id = "spindle",
            Address = "cnc/spindle",
            DataType = "int",
            Unit = "rpm",
            Scale = 1,
            Deadband = 5,
            Enabled = true
        });
        await Read<PointSetDocument>(await client.PutAsJsonAsync("/api/v1/config/points/cnc-09", points, Json));

        var mqtt = await Read<MqttSinkDocument>(await client.GetAsync("/api/v1/config/sinks/mqtt"));
        mqtt.Spec.Broker.ClientId = "studio-api";
        mqtt.Spec.Broker.PasswordFromEnv = "MQTT_PASSWORD";
        await Read<MqttSinkDocument>(await client.PutAsJsonAsync("/api/v1/config/sinks/mqtt", mqtt, Json));

        var validation = await Read<ValidationResult>(await client.PostAsync("/api/v1/config/validate", content: null));
        Assert.True(validation.Valid, string.Join("; ", validation.Issues.Select(issue => issue.Message)));

        var before = await Read<ConfigView>(await client.GetAsync("/api/v1/config"));
        Assert.True(before.Dirty);

        var published = await Read<PublishResult>(await client.PostAsJsonAsync("/api/v1/config/publish", new PublishRequest { Note = "api" }, Json));
        Assert.False(string.IsNullOrWhiteSpace(published.Revision));
        Assert.False(published.Unchanged);

        var revisions = await Read<RevisionList>(await client.GetAsync("/api/v1/config/revisions"));
        Assert.Contains(revisions.Revisions, item => item.Revision == published.Revision && item.Action == "publish");

        var test = await Read<DeviceTestResult>(await client.PostAsync("/api/v1/devices/cnc-09/test", content: null));
        Assert.True(test.Ok);
        Assert.Equal("fanuc.fake", test.Adapter);

        var status = await Read<RuntimeStatus>(await client.GetAsync("/api/v1/runtime/status"));
        Assert.Equal("mock", status.Mode);
        Assert.Contains(status.Devices, item => item.Id == "cnc-09" && item.Status == "online");

        var observations = await Read<ObservationList>(await client.GetAsync("/api/v1/runtime/observations?deviceId=cnc-09&limit=20"));
        Assert.Contains(observations.Observations, item => item.Point == "spindle");

        var rolled = await Read<RollbackResult>(await client.PostAsJsonAsync(
            "/api/v1/config/rollback",
            new RollbackRequest { Revision = before.ActiveRevision },
            Json));
        Assert.Equal(before.ActiveRevision, rolled.Revision);

        var after = await Read<ConfigView>(await client.GetAsync("/api/v1/config"));
        Assert.Equal(before.ActiveRevision, after.ActiveRevision);
        Assert.DoesNotContain(after.Published.Devices, item => item.Metadata.Id == "cnc-09");
        Assert.DoesNotContain(after.Draft.Devices, item => item.Metadata.Id == "cnc-09");
    }

    [Fact]
    public async Task Viewer_cannot_mutate_draft()
    {
        using var client = _factory.CreateClient();
        await Authorize(client, "viewer", "viewer");
        var response = await client.PutAsJsonAsync("/api/v1/config/devices/cnc-01", new DeviceDocument
        {
            Metadata = new DeviceMetadata { Id = "cnc-01", DisplayName = "denied" },
            Spec = new DeviceSpec
            {
                Adapter = "fanuc.fake",
                IntervalMs = 1000,
                Connection = new DeviceConnection { Host = "127.0.0.1", Port = 8193 }
            }
        }, Json);
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        var error = await response.Content.ReadFromJsonAsync<ApiError>(Json);
        Assert.Equal("forbidden", error!.Code);
    }

    private static async Task Authorize(HttpClient client, string username, string password)
    {
        var response = await client.PostAsJsonAsync("/api/v1/auth/login", new LoginRequest
        {
            Username = username,
            Password = password
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
}
