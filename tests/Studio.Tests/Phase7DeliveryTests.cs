using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using IotDaq.Licensing;
using IotDaq.Persistence;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Studio.Host.Config;
using Studio.Host.Licensing;
using Xunit;

namespace Studio.Tests;

public sealed class LicenseCodecTests
{
    [Fact]
    public void Roundtrip_verifies_and_rejects_tamper()
    {
        var (publicSpki, privatePem) = LicenseCrypto.Generate();
        using var privateKey = LicenseCrypto.CreatePrivate(privatePem);
        var document = LicenseCodec.Sign(new LicensePayload
        {
            Customer = "示例工厂",
            Edition = "commercial",
            DeviceLimit = 20,
            PointLimit = null,
            ExpiresAt = "2027-01-01T00:00:00Z",
            Features = ["query-api", "opcua"],
            IssuedAt = "2026-09-01T00:00:00Z"
        }, privateKey);

        using var publicKey = LicenseCrypto.CreatePublic(publicSpki);
        var check = LicenseCodec.Verify(document, publicKey);
        Assert.True(check.Ok);
        Assert.Equal("示例工厂", check.Payload!.Customer);
        Assert.Equal(20, check.Payload.DeviceLimit);
        Assert.Null(check.Payload.PointLimit);
        Assert.Equal(["opcua", "query-api"], check.Payload.Features);

        var signature = JsonSerializer.Deserialize<JsonElement>(document).GetProperty("signature").GetString()!;
        var chars = signature.ToCharArray();
        chars[8] = chars[8] == 'A' ? 'B' : 'A';
        var tampered = document.Replace(signature, new string(chars), StringComparison.Ordinal);
        Assert.NotEqual(document, tampered);
        var rejected = LicenseCodec.Verify(tampered, publicKey);
        Assert.False(rejected.Ok);
        Assert.Equal("license_signature", rejected.Code);
    }

    [Fact]
    public void Upgrade_package_checks_signature_and_checksum()
    {
        var (publicSpki, privatePem) = LicenseCrypto.Generate();
        using var privateKey = LicenseCrypto.CreatePrivate(privatePem);
        var zip = UpgradePackage.Create(
            [("Host.dll", Encoding.UTF8.GetBytes("host")), ("wwwroot/index.html", Encoding.UTF8.GetBytes("web"))],
            privateKey,
            "0.8.0");
        using var publicKey = LicenseCrypto.CreatePublic(publicSpki);
        using var ok = new MemoryStream(zip);
        var inspection = UpgradePackage.Inspect(ok, publicKey);
        Assert.True(inspection.Ok);
        Assert.Equal("0.8.0", inspection.Version);
        Assert.True(inspection.SignatureVerified);
        Assert.True(inspection.ChecksumVerified);

        using var brokenStream = new MemoryStream();
        brokenStream.Write(zip);
        brokenStream.Position = 0;
        using (var archive = new System.IO.Compression.ZipArchive(brokenStream, System.IO.Compression.ZipArchiveMode.Update, leaveOpen: true))
        {
            archive.GetEntry("Host.dll")!.Delete();
            using var entry = archive.CreateEntry("Host.dll").Open();
            entry.Write(Encoding.UTF8.GetBytes("tampered"));
        }

        brokenStream.Position = 0;
        var failed = UpgradePackage.Inspect(brokenStream, publicKey);
        Assert.False(failed.Ok);
        Assert.Equal("upgrade_checksum", failed.Code);
    }
}

public sealed class SchemaUpgradeV6Tests
{
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    public void Older_schema_gains_installed_license(int version)
    {
        var directory = Directory.CreateTempSubdirectory("schema-v6-" + version).FullName;
        try
        {
            var store = new ConfigStore(directory);
            store.EnsureInitialized();
            using (var db = store.Database.CreateContext())
            {
                db.Database.ExecuteSqlRaw("DROP TABLE IF EXISTS installed_license");
                db.Database.ExecuteSqlRaw("DROP TABLE IF EXISTS security_state");
                db.Database.ExecuteSqlRaw("DROP TABLE IF EXISTS self_test_runs");
                db.Database.ExecuteSqlRaw("UPDATE schema_info SET Version = {0} WHERE Id = 1", version);
            }

            store.Database.EnsureReady();
            using var check = store.Database.CreateContext();
            Assert.Equal(GatewayPersistence.SchemaVersion, check.SchemaInfo.AsNoTracking().Single().Version);
            Assert.Empty(check.InstalledLicense.ToList());
            Assert.Empty(check.SecurityState.ToList());
            Assert.Empty(check.SelfTestRuns.ToList());
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}

public sealed class PostgresToolTests
{
    [Fact]
    public void Missing_client_explains_the_path()
    {
        Assert.Null(PostgresTools.Find("pg_dump_not_installed_here"));
        if (PostgresTools.Find("pg_dump") is not null)
        {
            return;
        }

        var error = Assert.Throws<InvalidOperationException>(() =>
            PostgresTools.Dump("Host=127.0.0.1;Database=daq;Username=daq", Path.GetTempPath(), "x.dump"));
        Assert.Contains("pg_dump", error.Message, StringComparison.Ordinal);
        Assert.Contains("PATH", error.Message, StringComparison.Ordinal);
    }
}

public sealed class LicenseApiTests : IClassFixture<LicenseApiFactory>
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private readonly LicenseApiFactory _factory;

    public LicenseApiTests(LicenseApiFactory factory) => _factory = factory;

    [Fact]
    public async Task Community_blocks_gated_feature_and_device_increase()
    {
        using var client = await Client();
        await client.DeleteAsync("/api/v1/license");
        var license = await client.GetFromJsonAsync<JsonElement>("/api/v1/license", Json);
        Assert.Equal("community", license.GetProperty("edition").GetString());
        Assert.False(license.GetProperty("entitlements").GetProperty("opcua").GetBoolean());
        Assert.False(string.IsNullOrWhiteSpace(license.GetProperty("machineFingerprint").GetString()));

        var denied = await client.PutAsJsonAsync("/api/v1/opcua", new { enabled = true, port = 4840, allowAnonymous = true, allowNone = true, allowSignAndEncrypt = true }, Json);
        Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        var body = await denied.Content.ReadFromJsonAsync<JsonElement>(Json);
        Assert.Contains("OPC UA", body.GetProperty("message").GetString(), StringComparison.Ordinal);
        Assert.Contains("采集", body.GetProperty("message").GetString(), StringComparison.Ordinal);

        var devices = await client.GetFromJsonAsync<JsonElement>("/api/v1/config/devices", Json);
        var count = devices.GetArrayLength();
        var extra = await client.PutAsJsonAsync("/api/v1/config/devices/over-limit", new
        {
            apiVersion = "daq.gateway/v1",
            kind = "Device",
            metadata = new { id = "over-limit", displayName = "超限" },
            spec = new
            {
                adapter = "fanuc.sim",
                enabled = true,
                intervalMs = 1000,
                connection = new { host = "127.0.0.1", port = 8193, focasTimeoutMs = 3000 }
            }
        }, Json);
        Assert.Equal(HttpStatusCode.Forbidden, extra.StatusCode);
        var still = await client.GetFromJsonAsync<JsonElement>("/api/v1/config/devices", Json);
        Assert.Equal(count, still.GetArrayLength());
    }

    [Fact]
    public async Task Commercial_license_unlocks_features_until_it_expires()
    {
        using var client = await Client();
        var fingerprint = (await client.GetFromJsonAsync<JsonElement>("/api/v1/license", Json)).GetProperty("machineFingerprint").GetString();
        var document = Sign(new LicensePayload
        {
            Customer = "商用客户",
            Edition = "commercial",
            DeviceLimit = 100,
            PointLimit = 100000,
            Features = ["*"],
            MachineFingerprint = fingerprint,
            IssuedAt = "2026-01-01T00:00:00Z",
            ExpiresAt = DateTimeOffset.UtcNow.AddDays(30).ToString("yyyy-MM-dd'T'HH:mm:ss'Z'")
        });
        var imported = await client.PostAsync("/api/v1/license", new StringContent(document, Encoding.UTF8, "application/json"));
        Assert.Equal(HttpStatusCode.OK, imported.StatusCode);
        var view = await imported.Content.ReadFromJsonAsync<JsonElement>(Json);
        Assert.Equal("commercial", view.GetProperty("edition").GetString());
        Assert.Equal("valid", view.GetProperty("status").GetString());
        Assert.True(view.GetProperty("entitlements").GetProperty("http-push").GetBoolean());

        var audit = await client.GetFromJsonAsync<JsonElement>("/api/v1/audit?limit=20", Json);
        Assert.Contains("license.import", audit.GetRawText(), StringComparison.Ordinal);

        var mismatch = Sign(new LicensePayload
        {
            Customer = "别人的机器",
            Edition = "commercial",
            Features = ["*"],
            MachineFingerprint = "00112233445566778899aabbccddeeff",
            IssuedAt = "2026-01-01T00:00:00Z"
        });
        var rejected = await client.PostAsync("/api/v1/license", new StringContent(mismatch, Encoding.UTF8, "application/json"));
        var bound = await rejected.Content.ReadFromJsonAsync<JsonElement>(Json);
        Assert.Equal(HttpStatusCode.OK, rejected.StatusCode);
        Assert.Equal("invalid", bound.GetProperty("status").GetString());
        Assert.False(bound.GetProperty("entitlements").GetProperty("opcua").GetBoolean());

        var expired = Sign(new LicensePayload
        {
            Customer = "过期客户",
            Edition = "commercial",
            DeviceLimit = 100,
            Features = ["*"],
            IssuedAt = "2020-01-01T00:00:00Z",
            ExpiresAt = "2020-02-01T00:00:00Z"
        });
        var expiredResponse = await client.PostAsync("/api/v1/license", new StringContent(expired, Encoding.UTF8, "application/json"));
        var expiredView = await expiredResponse.Content.ReadFromJsonAsync<JsonElement>(Json);
        Assert.Equal("expired", expiredView.GetProperty("status").GetString());
        Assert.True(expiredView.GetProperty("collectionContinues").GetBoolean());
        Assert.False(expiredView.GetProperty("entitlements").GetProperty("query-api").GetBoolean());
        Assert.False(string.IsNullOrWhiteSpace(expiredView.GetProperty("banner").GetString()));

        var removed = await client.DeleteAsync("/api/v1/license");
        var community = await removed.Content.ReadFromJsonAsync<JsonElement>(Json);
        Assert.Equal("community", community.GetProperty("edition").GetString());
    }

    [Fact]
    public async Task Upgrade_stages_signed_zip_and_rollback_restores_backup()
    {
        using var client = await Client();
        using var privateKey = LicenseCrypto.CreatePrivate(_factory.PrivatePem);
        var zip = UpgradePackage.Create(
            [("Host.dll", Encoding.UTF8.GetBytes("host-bytes")), ("wwwroot/index.html", Encoding.UTF8.GetBytes("<html></html>"))],
            privateKey,
            "0.8.0");
        var staged = await client.PostAsync("/api/v1/ops/upgrade", new ByteArrayContent(zip));
        Assert.Equal(HttpStatusCode.OK, staged.StatusCode);
        var state = await client.GetFromJsonAsync<JsonElement>("/api/v1/ops/upgrade", Json);
        Assert.Equal("staged", state.GetProperty("state").GetProperty("phase").GetString());
        Assert.True(File.Exists(state.GetProperty("state").GetProperty("backupPath").GetString()));

        using var other = LicenseCrypto.CreatePrivate(LicenseCrypto.Generate().PrivatePem);
        var foreign = UpgradePackage.Create(
            [("Host.dll", Encoding.UTF8.GetBytes("other")), ("wwwroot/index.html", Encoding.UTF8.GetBytes("web"))],
            other,
            "9.9.9");
        var rejected = await client.PostAsync("/api/v1/ops/upgrade", new ByteArrayContent(foreign));
        Assert.Equal(HttpStatusCode.BadRequest, rejected.StatusCode);

        var rollback = await client.PostAsync("/api/v1/ops/upgrade/rollback", null);
        Assert.Equal(HttpStatusCode.OK, rollback.StatusCode);
    }

    private string Sign(LicensePayload payload)
    {
        using var key = LicenseCrypto.CreatePrivate(_factory.PrivatePem);
        return LicenseCodec.Sign(payload, key);
    }

    private async Task<HttpClient> Client()
    {
        var client = _factory.CreateClient();
        var login = await client.PostAsJsonAsync("/api/v1/auth/login", new { username = "admin", password = "admin" }, Json);
        var body = await login.Content.ReadFromJsonAsync<JsonElement>(Json);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", body.GetProperty("token").GetString());
        return client;
    }
}

public sealed class LicenseApiFactory : WebApplicationFactory<Program>
{
    public string DataDirectory { get; } = Directory.CreateTempSubdirectory("phase7-api").FullName;

    public string PublicKey { get; }

    public string PrivatePem { get; }

    public LicenseApiFactory()
    {
        var generated = LicenseCrypto.Generate();
        PublicKey = generated.PublicSpki;
        PrivatePem = generated.PrivatePem;
        Environment.SetEnvironmentVariable("STUDIO_DATA", DataDirectory);
    }

    protected override void ConfigureWebHost(Microsoft.AspNetCore.Hosting.IWebHostBuilder builder)
    {
        builder.UseSetting("Studio:DataDirectory", DataDirectory);
        builder.UseSetting("Host:DataDirectory", DataDirectory);
        builder.UseSetting("Host:Acquisition", "off");
        builder.UseSetting("Licensing:PublicKeySpki", PublicKey);
        builder.UseSetting("Licensing:GraceDays", "7");
        builder.UseSetting("Licensing:Community:DeviceLimit", "1");
        builder.UseSetting("Licensing:Community:PointLimit", "50");
        builder.UseSetting("Licensing:Community:Features:0", "none");
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
            }
        }
    }
}
