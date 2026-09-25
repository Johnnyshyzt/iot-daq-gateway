using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Studio.Contracts;
using Xunit;

namespace Studio.Tests;

[CollectionDefinition("studio-host", DisableParallelization = true)]
public sealed class StudioHostCollection;

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
        builder.UseSetting("Host:Acquisition", "off");
        builder.UseSetting("Studio:GatewayLoopback", "http://127.0.0.1:9");
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

[Collection("studio-host")]
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

    [Fact]
    public async Task Focas_device_test_reports_a_real_handshake_failure()
    {
        using var client = _factory.CreateClient();
        await Authorize(client, "engineer", "engineer");
        var device = new DeviceDocument
        {
            Metadata = new DeviceMetadata { Id = "cnc-focas", DisplayName = "FOCAS 探测" },
            Spec = new DeviceSpec
            {
                Adapter = "fanuc.focas",
                Enabled = true,
                IntervalMs = 1000,
                Connection = new DeviceConnection { Host = "192.0.2.10", Port = 8193, FocasTimeoutMs = 1000 }
            }
        };
        await Read<DeviceDocument>(await client.PutAsJsonAsync("/api/v1/config/devices/cnc-focas", device, Json));

        var test = await Read<DeviceTestResult>(await client.PostAsync("/api/v1/devices/cnc-focas/test", content: null));

        Assert.False(test.Ok);
        Assert.Equal("fanuc.focas", test.Adapter);
        Assert.Contains("Fwlib64", test.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("端口可达", test.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Demo_posture_says_localhost_accounts_are_not_a_field_password()
    {
        using var client = _factory.CreateClient();
        var posture = await Read<AuthPosture>(await client.GetAsync("/api/v1/auth/posture"));
        Assert.Equal("demo", posture.Mode);
        Assert.Contains("admin / admin", posture.Message, StringComparison.Ordinal);
        Assert.Contains("localhost", posture.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Field_bootstrap_rejects_admin_admin_until_password_changes()
    {
        var previous = Environment.GetEnvironmentVariable("STUDIO_DATA");
        var directory = Directory.CreateTempSubdirectory("studio-field").FullName;
        Environment.SetEnvironmentVariable("STUDIO_DATA", directory);
        try
        {
            using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
            {
                builder.UseSetting("Studio:DataDirectory", directory);
                builder.UseSetting("Host:Acquisition", "off");
                builder.UseSetting("Studio:AccountMode", "field");
                builder.UseSetting("Studio:GatewayLoopback", "http://127.0.0.1:9");
            });
            using var client = factory.CreateClient();

            var posture = await Read<AuthPosture>(await client.GetAsync("/api/v1/auth/posture"));
            Assert.Equal("field", posture.Mode);
            Assert.Contains("bootstrap-password.txt", posture.Message, StringComparison.Ordinal);

            var denied = await client.PostAsJsonAsync("/api/v1/auth/login", new LoginRequest
            {
                Username = "admin",
                Password = "admin"
            }, Json);
            Assert.Equal(HttpStatusCode.Unauthorized, denied.StatusCode);

            var bootstrapPath = Path.Combine(directory, "auth", "bootstrap-password.txt");
            Assert.True(File.Exists(bootstrapPath));
            var password = BootstrapPassword(bootstrapPath, "admin");
            var accounts = File.ReadAllText(Path.Combine(directory, "auth", "accounts.json"));
            Assert.Contains("pbkdf2-sha256", accounts, StringComparison.Ordinal);
            Assert.DoesNotContain(password, accounts, StringComparison.Ordinal);

            var login = await Read<LoginResponse>(await client.PostAsJsonAsync("/api/v1/auth/login", new LoginRequest
            {
                Username = "admin",
                Password = password
            }, Json));
            Assert.True(login.MustChangePassword);
            Assert.Equal("admin", login.Role);
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", login.Token);

            var blocked = await client.GetAsync("/api/v1/config");
            Assert.Equal(HttpStatusCode.Forbidden, blocked.StatusCode);
            var blockedError = await blocked.Content.ReadFromJsonAsync<ApiError>(Json);
            Assert.Equal("password_change_required", blockedError!.Code);

            var weak = await client.PostAsJsonAsync("/api/v1/auth/password", new ChangePasswordRequest
            {
                CurrentPassword = password,
                NewPassword = "admin"
            }, Json);
            Assert.Equal(HttpStatusCode.BadRequest, weak.StatusCode);

            var changed = await Read<ChangePasswordResult>(await client.PostAsJsonAsync("/api/v1/auth/password", new ChangePasswordRequest
            {
                CurrentPassword = password,
                NewPassword = "field-pass-1"
            }, Json));
            Assert.True(changed.Ok);

            var config = await client.GetAsync("/api/v1/config");
            Assert.Equal(HttpStatusCode.OK, config.StatusCode);
            var remaining = File.ReadAllText(bootstrapPath);
            Assert.DoesNotContain("admin=", remaining, StringComparison.Ordinal);
            Assert.Contains("engineer=", remaining, StringComparison.Ordinal);
            Assert.False(File.ReadAllText(Path.Combine(directory, "auth", "accounts.json")).Contains(password, StringComparison.Ordinal));
        }
        finally
        {
            Environment.SetEnvironmentVariable("STUDIO_DATA", previous);
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    private static string BootstrapPassword(string path, string username)
    {
        var prefix = username + "=";
        var line = File.ReadAllLines(path).Single(item => item.StartsWith(prefix, StringComparison.Ordinal));
        return line[prefix.Length..];
    }

    private static async Task Authorize(HttpClient client, string username, string password)
    {
        var response = await client.PostAsJsonAsync("/api/v1/auth/login", new LoginRequest
        {
            Username = username,
            Password = password
        }, Json);
        var login = await Read<LoginResponse>(response);
        Assert.False(login.MustChangePassword);
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
