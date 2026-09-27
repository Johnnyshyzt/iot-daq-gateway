using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using IotDaq.Persistence;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Studio.Host.Config;
using Studio.Host.Northbound;
using Xunit;

namespace Studio.Tests;

public sealed class HttpPushBehaviorTests
{
    [Fact]
    public async Task Down_target_spools_then_replays_with_hmac()
    {
        var directory = Directory.CreateTempSubdirectory("phase6-http").FullName;
        var port = FreePort();
        try
        {
            var store = new ConfigStore(directory);
            store.EnsureInitialized();
            var id = "push1";
            store.Database.SaveHttpPushTarget(new HttpPushTargetRow
            {
                Id = id,
                Name = "MES",
                Enabled = true,
                Url = $"http://127.0.0.1:{port}/hook",
                Method = "POST",
                HeadersJson = """[{"name":"X-Line","value":"A"}]""",
                AuthKind = "hmac-sha256",
                AuthSecret = "top-secret",
                SignatureHeader = "X-DAQ-Signature",
                SendValues = false,
                SendStatus = false,
                SendAlarms = true,
                BatchMax = 20,
                TimeoutMs = 1000,
                BackoffInitialMs = 50,
                BackoffMaxMs = 50,
                UpdatedUnixMs = 1
            });
            var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            store.Database.SetSetting("httppush." + id + ".raise", "0");
            store.Database.SetSetting("httppush." + id + ".clear", "0");
            store.Database.SetSetting("httppush." + id + ".status", now.ToString());
            store.Database.SetSetting("httppush." + id + ".sample", now.ToString());
            using (var db = store.Database.CreateContext())
            {
                db.Alarms.Add(new AlarmRow
                {
                    Id = "alarm-1",
                    DeviceId = "cnc-01",
                    PointId = "alarm",
                    Code = "100",
                    Message = "servo",
                    Severity = "alarm",
                    Active = true,
                    RaisedUnixMs = now
                });
                db.SaveChanges();
            }

            var dispatcher = new HttpPushDispatcher(store.Database, store, NullLogger<HttpPushDispatcher>.Instance);
            await dispatcher.TickAsync(CancellationToken.None);
            Assert.Equal(1, dispatcher.SpoolDepth(id));
            var failed = store.Database.FindHttpPushTarget(id);
            Assert.NotNull(failed);
            Assert.True(failed!.Failed >= 1);
            Assert.DoesNotContain("top-secret", JsonSerializer.Serialize(new { failed.Url, failed.AuthKind }));

            using var listener = new HttpListener();
            listener.Prefixes.Add($"http://127.0.0.1:{port}/");
            listener.Start();
            var served = Task.Run(async () =>
            {
                var context = await listener.GetContextAsync();
                using var reader = new StreamReader(context.Request.InputStream, Encoding.UTF8);
                var payload = await reader.ReadToEndAsync();
                var signature = context.Request.Headers["X-DAQ-Signature"];
                var line = context.Request.Headers["X-Line"];
                context.Response.StatusCode = 204;
                context.Response.Close();
                return (payload, signature, line);
            });
            await Task.Delay(120);
            await dispatcher.TickAsync(CancellationToken.None);
            var (body, signature, line) = await served.WaitAsync(TimeSpan.FromSeconds(5));
            var expected = "sha256=" + Convert.ToHexString(HMACSHA256.HashData(Encoding.UTF8.GetBytes("top-secret"), Encoding.UTF8.GetBytes(body))).ToLowerInvariant();
            Assert.Equal(expected, signature);
            Assert.Equal("A", line);
            Assert.Contains("\"kind\":\"alarm\"", body.Replace(" ", ""), StringComparison.Ordinal);
            Assert.Contains("\"action\":\"raise\"", body.Replace(" ", ""), StringComparison.Ordinal);
            Assert.Equal(0, dispatcher.SpoolDepth(id));
            Assert.True(store.Database.FindHttpPushTarget(id)!.Delivered >= 1);
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    [Fact]
    public void Backoff_grows_until_the_cap()
    {
        Assert.Equal(1000, HttpPushDispatcher.BackoffMs(1, 1000, 8000));
        Assert.Equal(2000, HttpPushDispatcher.BackoffMs(2, 1000, 8000));
        Assert.Equal(8000, HttpPushDispatcher.BackoffMs(8, 1000, 8000));
    }

    private static int FreePort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }
}

[Collection("studio-host")]
public sealed class Phase6ApiTests : IClassFixture<Phase6Factory>
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private readonly Phase6Factory _factory;

    public Phase6ApiTests(Phase6Factory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Api_key_is_hashed_revocable_and_query_is_read_only()
    {
        using var client = _factory.CreateClient();
        var anonymous = await client.GetAsync("/api/query/v1/devices");
        Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);

        var schema = await client.GetAsync("/api/contract/v1/point-value.schema.json");
        Assert.Equal(HttpStatusCode.OK, schema.StatusCode);
        var openapi = await client.GetAsync("/api/query/v1/openapi.json");
        Assert.Equal(HttpStatusCode.OK, openapi.StatusCode);
        Assert.Contains("只读", await openapi.Content.ReadAsStringAsync(), StringComparison.Ordinal);

        await Authorize(client);
        var created = await client.PostAsJsonAsync("/api/v1/api-keys", new { name = "MES 只读" }, Json);
        var body = await created.Content.ReadFromJsonAsync<JsonElement>(Json);
        Assert.Equal(HttpStatusCode.OK, created.StatusCode);
        var key = body.GetProperty("key").GetString();
        var id = body.GetProperty("id").GetString();
        Assert.StartsWith("daq_", key, StringComparison.Ordinal);

        var listed = await client.GetFromJsonAsync<JsonElement>("/api/v1/api-keys", Json);
        var listText = listed.GetRawText();
        Assert.DoesNotContain(key!, listText, StringComparison.Ordinal);
        Assert.Contains(body.GetProperty("prefix").GetString()!, listText, StringComparison.Ordinal);

        using var query = _factory.CreateClient();
        query.DefaultRequestHeaders.Add("X-Api-Key", key);
        var devices = await query.GetAsync("/api/query/v1/devices");
        Assert.Equal(HttpStatusCode.OK, devices.StatusCode);
        var deviceJson = await devices.Content.ReadAsStringAsync();
        Assert.Contains("northbound/1.0", deviceJson, StringComparison.Ordinal);

        var studioOnQuery = await client.GetAsync("/api/query/v1/devices");
        Assert.Equal(HttpStatusCode.Unauthorized, studioOnQuery.StatusCode);
        var keyOnStudio = _factory.CreateClient();
        keyOnStudio.DefaultRequestHeaders.Add("X-Api-Key", key);
        var blocked = await keyOnStudio.GetAsync("/api/v1/config");
        Assert.Equal(HttpStatusCode.Unauthorized, blocked.StatusCode);

        var history = await query.GetAsync("/api/query/v1/devices/cnc-01/history?from=0&to=1");
        Assert.Equal(HttpStatusCode.OK, history.StatusCode);
        var alarms = await query.GetAsync("/api/query/v1/alarms");
        Assert.Equal(HttpStatusCode.OK, alarms.StatusCode);
        var utilization = await query.GetAsync("/api/query/v1/utilization");
        Assert.Equal(HttpStatusCode.OK, utilization.StatusCode);
        Assert.Contains("\"kind\":\"utilization\"", (await utilization.Content.ReadAsStringAsync()).Replace(" ", ""), StringComparison.Ordinal);

        await client.PostAsync($"/api/v1/api-keys/{id}/revoke", content: null);
        var revoked = await query.GetAsync("/api/query/v1/devices");
        Assert.Equal(HttpStatusCode.Unauthorized, revoked.StatusCode);

        var audit = await client.GetFromJsonAsync<JsonElement>("/api/v1/audit?limit=20", Json);
        var actions = audit.GetProperty("events").EnumerateArray().Select(item => item.GetProperty("action").GetString()).ToList();
        Assert.Contains("apikey.create", actions);
        Assert.Contains("apikey.revoke", actions);

        var store = new ConfigStore(_factory.DataDirectory);
        var row = store.Database.ListApiKeys().Single(item => item.Id == id);
        Assert.NotEqual(key, row.KeyHash);
        Assert.Equal(64, row.KeyHash.Length);
        Assert.DoesNotContain(key!, row.KeyHash, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Http_push_and_opcua_settings_are_audited_and_secrets_stay_hidden()
    {
        using var client = _factory.CreateClient();
        await Authorize(client);
        var saved = await client.PutAsJsonAsync("/api/v1/http-push", new
        {
            name = "云端",
            enabled = true,
            url = "https://mes.example/hook",
            method = "POST",
            authKind = "bearer",
            secret = "bearer-token",
            headers = new[] { new { name = "X-Token", value = "header-secret" } },
            sendValues = true,
            valueMode = "change",
            sendStatus = true,
            sendAlarms = true
        }, Json);
        Assert.Equal(HttpStatusCode.OK, saved.StatusCode);
        var view = await saved.Content.ReadAsStringAsync();
        Assert.DoesNotContain("bearer-token", view, StringComparison.Ordinal);
        Assert.DoesNotContain("header-secret", view, StringComparison.Ordinal);
        Assert.Contains("\"hasSecret\":true", view.Replace(" ", ""), StringComparison.Ordinal);

        var opc = await client.PutAsJsonAsync("/api/v1/opcua", new
        {
            enabled = false,
            port = 48400,
            allowAnonymous = true,
            allowNone = true,
            allowSignAndEncrypt = true,
            username = "opc",
            password = "opc-secret"
        }, Json);
        Assert.Equal(HttpStatusCode.OK, opc.StatusCode);
        var opcBody = await opc.Content.ReadAsStringAsync();
        Assert.DoesNotContain("opc-secret", opcBody, StringComparison.Ordinal);
        Assert.Contains("\"hasPassword\":true", opcBody.Replace(" ", ""), StringComparison.Ordinal);

        var audit = await client.GetFromJsonAsync<JsonElement>("/api/v1/audit?limit=20", Json);
        var actions = audit.GetProperty("events").EnumerateArray().Select(item => item.GetProperty("action").GetString()).ToList();
        Assert.Contains("httppush.save", actions);
        Assert.Contains("opcua.update", actions);
    }

    [Fact]
    public void Schema_upgrades_from_v1_to_current()
    {
        var directory = Directory.CreateTempSubdirectory("phase6-schema").FullName;
        try
        {
            var store = new ConfigStore(directory);
            store.EnsureInitialized();
            using (var db = store.Database.CreateContext())
            {
                db.Database.ExecuteSqlRaw("DROP TABLE http_push_targets");
                db.Database.ExecuteSqlRaw("DROP TABLE api_keys");
                db.Database.ExecuteSqlRaw("DROP TABLE link_status");
                db.Database.ExecuteSqlRaw("UPDATE schema_info SET Version = 1 WHERE Id = 1");
            }

            store.Database.EnsureReady();
            using var check = store.Database.CreateContext();
            Assert.Equal(GatewayPersistence.SchemaVersion, check.SchemaInfo.AsNoTracking().Single().Version);
            Assert.Equal(7, GatewayPersistence.SchemaVersion);
            Assert.Empty(check.InstalledLicense.ToList());
            Assert.Empty(check.HttpPushTargets.ToList());
            Assert.Empty(check.ApiKeys.ToList());
            Assert.Empty(check.LinkStatus.ToList());
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static async Task Authorize(HttpClient client)
    {
        var login = await client.PostAsJsonAsync("/api/v1/auth/login", new { username = "admin", password = "admin" }, Json);
        var body = await login.Content.ReadFromJsonAsync<JsonElement>(Json);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", body.GetProperty("token").GetString());
    }
}

public sealed class Phase6Factory : WebApplicationFactory<Program>
{
    public string DataDirectory { get; } = Directory.CreateTempSubdirectory("phase6-api").FullName;

    public Phase6Factory()
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
            try
            {
                Directory.Delete(DataDirectory, recursive: true);
            }
            catch (IOException)
            {
                // The host may still be releasing the SQLite file.
            }
        }
    }
}
