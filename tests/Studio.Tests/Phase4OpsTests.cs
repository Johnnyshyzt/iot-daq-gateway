using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using IotDaq.Persistence;
using Microsoft.EntityFrameworkCore;
using Studio.Contracts;
using Studio.Host.Config;
using Studio.Host.Endpoints;
using Xunit;

namespace Studio.Tests;

public sealed class SqliteBackupTests
{
    [Fact]
    public void Backup_roundtrip_upgrades_v2_and_keeps_audit()
    {
        var directory = Directory.CreateTempSubdirectory("phase4-backup").FullName;
        try
        {
            var store = new ConfigStore(directory);
            store.EnsureInitialized();
            store.Database.AppendAudit("admin", "admin", "device.upsert", "cnc-01", "测试设备");

            var backup = Path.Combine(directory, "backup.db");
            store.Database.WriteSqliteBackupFile(backup);
            Assert.True(new FileInfo(backup).Length > 16);

            using (var db = store.Database.CreateContext())
            {
                db.Database.ExecuteSqlRaw("DROP TABLE audit_events");
                db.Database.ExecuteSqlRaw("UPDATE schema_info SET Version = 2 WHERE Id = 1");
            }

            store.Database.EnsureReady();
            using (var db = store.Database.CreateContext())
            {
                Assert.Equal(GatewayPersistence.SchemaVersion, db.SchemaInfo.AsNoTracking().Single().Version);
            }

            var restored = store.Database.ListAudit(10);
            Assert.Empty(restored);

            using var upload = File.OpenRead(backup);
            store.Database.RestoreSqlite(upload);
            Assert.Contains(store.Database.ListAudit(10), row => row.Action == "device.upsert" && row.Target == "cnc-01");
            Assert.True(store.Database.HasSlot("published"));

            using var garbage = new MemoryStream(Encoding.UTF8.GetBytes("not a database"));
            var rejected = Assert.Throws<InvalidOperationException>(() => store.Database.RestoreSqlite(garbage));
            Assert.Contains("SQLite", rejected.Message, StringComparison.Ordinal);
            Assert.Contains(store.Database.ListAudit(10), row => row.Action == "device.upsert");
        }
        finally
        {
            TryDelete(directory);
        }
    }

    [Fact]
    public void Fifty_simulator_samples_stay_bounded()
    {
        var now = new DateTimeOffset(2026, 9, 27, 8, 0, 0, TimeSpan.Zero);
        var total = 0;
        for (var cycle = 0; cycle < 5; cycle++)
        {
            for (var i = 0; i < 50; i++)
            {
                var rows = Adapters.Cnc.BrandSimulatorAdapter.Sample($"sim-{i:00}", "fanuc", now.AddSeconds(cycle), null);
                Assert.NotEmpty(rows);
                Assert.True(rows.Count < 80);
                total += rows.Count;
            }
        }

        Assert.True(total > 0);
    }

    private static void TryDelete(string directory)
    {
        try
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
        catch (IOException)
        {
            // SQLite may still be releasing the file.
        }
    }
}

[Collection("studio-host")]
public sealed class Phase4ApiTests : IClassFixture<StudioApiFactory>
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private readonly StudioApiFactory _factory;

    public Phase4ApiTests(StudioApiFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Health_is_anonymous_and_backup_is_admin_only()
    {
        using var client = _factory.CreateClient();
        var health = await client.GetAsync("/healthz");
        var healthBody = await health.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, health.StatusCode);
        Assert.Contains("\"status\":\"ok\"", healthBody, StringComparison.Ordinal);
        Assert.Contains("0.5.0", healthBody, StringComparison.Ordinal);
        Assert.Contains("Sqlite", healthBody, StringComparison.Ordinal);
        Assert.Contains("\"schemaVersion\":3", healthBody, StringComparison.Ordinal);

        var audit = await client.GetAsync("/api/v1/audit");
        Assert.Equal(HttpStatusCode.Unauthorized, audit.StatusCode);

        await Authorize(client, "viewer", "viewer");
        var denied = await client.GetAsync("/api/v1/ops/backup");
        Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);

        await Authorize(client, "admin", "admin");
        var onboarding = await Read<OnboardingView>(await client.GetAsync("/api/v1/onboarding"));
        Assert.Equal(5, onboarding.Steps.Count);
        Assert.Contains(onboarding.Steps, step => step.Id == "password");
        Assert.Contains(onboarding.Steps, step => step.Id == "device" && step.Done);

        var backup = await client.GetAsync("/api/v1/ops/backup");
        Assert.Equal(HttpStatusCode.OK, backup.StatusCode);
        var bytes = await backup.Content.ReadAsByteArrayAsync();
        Assert.StartsWith("SQLite format 3", Encoding.ASCII.GetString(bytes[..16]), StringComparison.Ordinal);

        using var garbage = new ByteArrayContent("not-a-db"u8.ToArray());
        garbage.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        var rejected = await client.PostAsync("/api/v1/ops/restore", garbage);
        Assert.Equal(HttpStatusCode.BadRequest, rejected.StatusCode);
        var rejectedBody = await rejected.Content.ReadFromJsonAsync<ApiError>(Json);
        Assert.Equal("restore_rejected", rejectedBody!.Code);

        var stillThere = await client.GetAsync("/api/v1/config");
        Assert.Equal(HttpStatusCode.OK, stillThere.StatusCode);

        using var restore = new ByteArrayContent(bytes);
        restore.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        var restored = await client.PostAsync("/api/v1/ops/restore", restore);
        Assert.Equal(HttpStatusCode.OK, restored.StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/v1/config")).StatusCode);

        var events = await Read<AuditList>(await client.GetAsync("/api/v1/audit?limit=20"));
        Assert.Contains(events.Events, item => item.Action == "ops.restore");
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
