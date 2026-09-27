using System.IO.Compression;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using IotDaq.Licensing;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Studio.Host.Auth;
using Studio.Host.Commissioning;
using Studio.Host.Config;
using Studio.Host.Security;
using Xunit;

namespace Studio.Tests;

public sealed class Phase8SecurityTests
{
    [Fact]
    public void Fingerprint_tolerates_one_nic_change_and_keeps_legacy_hash()
    {
        var bound = new FingerprintParts
        {
            Primary = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa",
            Host = "bbbbbbbbbbbbbbbb",
            Board = "cccccccccccccccc",
            Nics = ["1111111111111111", "2222222222222222"]
        };
        var replaced = new FingerprintParts
        {
            Primary = bound.Primary,
            Host = bound.Host,
            Board = bound.Board,
            Nics = ["1111111111111111", "3333333333333333"]
        };
        Assert.True(MachineFingerprint.MatchesParts(bound, replaced));
        Assert.True(MachineFingerprint.Matches(MachineFingerprint.Format(bound), replaced));

        var twoNics = new FingerprintParts
        {
            Primary = bound.Primary,
            Host = bound.Host,
            Board = bound.Board,
            Nics = ["3333333333333333", "4444444444444444"]
        };
        Assert.False(MachineFingerprint.MatchesParts(bound, twoNics));

        var hostAndBoard = new FingerprintParts
        {
            Primary = bound.Primary,
            Host = "dddddddddddddddd",
            Board = "eeeeeeeeeeeeeeee",
            Nics = bound.Nics
        };
        Assert.False(MachineFingerprint.MatchesParts(bound, hostAndBoard));
        Assert.True(MachineFingerprint.MatchesParts(bound, new FingerprintParts
        {
            Primary = bound.Primary,
            Host = "dddddddddddddddd",
            Board = bound.Board,
            Nics = bound.Nics
        }));

        var legacy = MachineFingerprint.LegacyHash("machine|host");
        Assert.True(MachineFingerprint.Matches(legacy, new FingerprintParts { LegacyMaterial = "machine|host" }));
        Assert.False(MachineFingerprint.Matches("00112233445566778899aabbccddeeff", new FingerprintParts { LegacyMaterial = "machine|host" }));
        Assert.True(MachineFingerprint.IsWellFormed(MachineFingerprint.Current()));
        Assert.StartsWith("fp2.", MachineFingerprint.Current(), StringComparison.Ordinal);
    }

    [Fact]
    public void Clock_rollback_respects_tolerance_and_seal_detects_edits()
    {
        const long now = 1_700_000_000_000;
        var rollback = ClockGuard.Observe(
            now,
            new ClockSnapshot { Present = true, MacValid = true, LastSeenUnixMs = now + 3_600_000 },
            new ClockSnapshot(),
            300_000);
        Assert.Equal("clock_rollback", rollback.Code);
        Assert.Equal(now + 3_600_000, rollback.LastSeenUnixMs);

        var drift = ClockGuard.Observe(
            now,
            new ClockSnapshot { Present = true, MacValid = true, LastSeenUnixMs = now - 60_000 },
            new ClockSnapshot(),
            300_000);
        Assert.Equal("", drift.Code);
        Assert.False(drift.Rollback);

        var tamper = ClockGuard.Observe(
            now,
            new ClockSnapshot { Present = true, MacValid = false, LastSeenUnixMs = now },
            new ClockSnapshot(),
            300_000);
        Assert.Equal("state_tamper", tamper.Code);

        var key = RandomNumberGenerator.GetBytes(32);
        var document = LicenseStateSeal.Create(key, now, "abc", "");
        Assert.True(LicenseStateSeal.TryRead(LicenseStateSeal.Serialize(document), key, out _));
        document.LastSeenUnixMs -= 5_000;
        Assert.False(LicenseStateSeal.TryRead(LicenseStateSeal.Serialize(document), key, out _));
    }

    [Fact]
    public void Trace_and_bundle_redact_secrets()
    {
        var directory = Directory.CreateTempSubdirectory("phase8-redact").FullName;
        try
        {
            var traces = new ProtocolTraceBuffer(directory);
            traces.Start("cnc-1", 60, 64_000, ["super-secret"]);
            var raw = "password=super-secret unit=1"u8.ToArray();
            traces.Write("cnc-1", "tx", raw, "password=super-secret unit=1");
            var view = traces.Read("cnc-1", 10);
            Assert.Single(view.Lines);
            Assert.DoesNotContain("super-secret", view.Lines[0].Text, StringComparison.Ordinal);
            Assert.Contains("***", view.Lines[0].Text, StringComparison.Ordinal);
            var secretHex = Convert.ToHexString(Encoding.UTF8.GetBytes("super-secret")).ToLowerInvariant();
            Assert.DoesNotContain(secretHex, view.Lines[0].Hex, StringComparison.Ordinal);

            var store = new ConfigStore(directory);
            store.EnsureInitialized();
            store.UpsertDevice("cnc-1", new Studio.Contracts.DeviceDocument
            {
                Metadata = new Studio.Contracts.DeviceMetadata { Id = "cnc-1", DisplayName = "红线" },
                Spec = new Studio.Contracts.DeviceSpec
                {
                    Adapter = "fanuc.fake",
                    Connection = new Studio.Contracts.DeviceConnection { Host = "10.0.0.8", Port = 8193, Password = "super-secret" }
                }
            });
            var configuration = new Microsoft.Extensions.Configuration.ConfigurationBuilder().Build();
            var licensing = new Studio.Host.Licensing.LicenseService(store.Database, configuration, directory);
            var zipPath = DiagnosticBundle.WriteZip(Path.Combine(directory, "out.zip"), store, licensing, traces);
            using var archive = ZipFile.OpenRead(zipPath);
            var names = archive.Entries.Select(entry => entry.FullName).ToList();
            Assert.Contains("license-summary.json", names);
            Assert.Contains("config/draft.json", names);
            Assert.Contains("versions.json", names);
            Assert.Contains("runtime/self-tests.json", names);
            Assert.DoesNotContain(names, name => name.Contains("state.key", StringComparison.Ordinal) || name.Contains("accounts.json", StringComparison.Ordinal));
            var draft = ReadZip(archive, "config/draft.json");
            Assert.DoesNotContain("super-secret", draft, StringComparison.Ordinal);
            Assert.Contains("***", draft, StringComparison.Ordinal);
            var license = ReadZip(archive, "license-summary.json");
            Assert.DoesNotContain("signature", license, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void Https_self_signed_certificate_includes_san()
    {
        var directory = Directory.CreateTempSubdirectory("phase8-https").FullName;
        try
        {
            var binding = HttpsCertificates.CreateSelfSigned(directory, ["gateway.local"], ["10.1.2.3"], 5080, 5443, true, false);
            Assert.Equal("self-signed", binding.Mode);
            Assert.True(binding.RedirectHttp);
            using var certificate = System.Security.Cryptography.X509Certificates.X509CertificateLoader.LoadPkcs12FromFile(binding.CertificatePath, binding.PfxPassword);
            var san = certificate.Extensions.OfType<System.Security.Cryptography.X509Certificates.X509SubjectAlternativeNameExtension>().Single();
            var text = san.Format(false);
            Assert.Contains("gateway.local", text, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("10.1.2.3", text, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("localhost", binding.DnsNames, StringComparer.OrdinalIgnoreCase);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void Protocol_hints_name_the_field_checks()
    {
        Assert.Contains("Fwlib", ProtocolHints.For("focas", "fanuc", null, "missing"), StringComparison.OrdinalIgnoreCase);
        Assert.Contains("unitId", ProtocolHints.For("modbus", "delta", null, null), StringComparison.Ordinal);
        Assert.Contains("证书", ProtocolHints.For("opcua", "generic", null, null), StringComparison.Ordinal);
        Assert.Contains("rack", ProtocolHints.For("s7", "siemens", null, null), StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Agent", ProtocolHints.For("mtconnect", "okuma", null, null), StringComparison.Ordinal);
    }

    [Fact]
    public void Empty_admin_role_is_migrated()
    {
        var directory = Directory.CreateTempSubdirectory("phase8-admin").FullName;
        try
        {
            var auth = Path.Combine(directory, "auth");
            Directory.CreateDirectory(auth);
            File.WriteAllText(Path.Combine(auth, "accounts.json"), """
                {"mode":"demo","users":[{"username":"admin","role":"","passwordHash":"x","mustChangePassword":false}]}
                """);
            var accounts = new AccountStore(directory, new Microsoft.Extensions.Configuration.ConfigurationBuilder().Build(), NullLogger<AccountStore>.Instance);
            var admin = accounts.ListUsers().Single();
            Assert.Equal("admin", admin.Role);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void Session_rules_honor_absolute_and_idle_timeouts()
    {
        Assert.True(SessionRules.Expired(100, 100, 90, 30));
        Assert.True(SessionRules.Expired(200, 500, 10, 1));
        Assert.False(SessionRules.Expired(200, 500, 150, 1));
        Assert.False(SessionRules.Expired(200, 500, 10, 0));
    }

    private static string ReadZip(ZipArchive archive, string name)
    {
        var entry = archive.GetEntry(name);
        Assert.NotNull(entry);
        using var reader = new StreamReader(entry.Open());
        return reader.ReadToEnd();
    }
}

[Collection("studio-host")]
public sealed class Phase8ApiTests : IClassFixture<Phase8Factory>
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private readonly Phase8Factory _factory;

    public Phase8ApiTests(Phase8Factory factory) => _factory = factory;

    [Fact]
    public async Task Self_test_stages_pass_for_simulator_and_hint_on_closed_port()
    {
        using var client = _factory.CreateClient();
        await Authorize(client, "engineer", "engineer");
        await client.PutAsJsonAsync("/api/v1/config/devices/sim-1", Device("sim-1", "fanuc.fake", "127.0.0.1", 8193), Json);
        var passed = await client.PostAsync("/api/v1/devices/sim-1/self-test", new StringContent("{}", Encoding.UTF8, "application/json"));
        var report = await passed.Content.ReadFromJsonAsync<JsonElement>(Json);
        Assert.Equal(HttpStatusCode.OK, passed.StatusCode);
        Assert.True(report.GetProperty("passed").GetBoolean());
        var ids = report.GetProperty("stages").EnumerateArray().Select(stage => stage.GetProperty("id").GetString()!).ToArray();
        Assert.Equal(["dns", "tcp", "handshake", "sample", "timing"], ids);
        Assert.All(report.GetProperty("stages").EnumerateArray(), stage => Assert.Equal("pass", stage.GetProperty("status").GetString()));

        await client.PutAsJsonAsync("/api/v1/config/devices/mb-1", Device("mb-1", "delta.modbus", "127.0.0.1", 1), Json);
        var failed = await client.PostAsync("/api/v1/devices/mb-1/self-test", new StringContent("{}", Encoding.UTF8, "application/json"));
        var body = await failed.Content.ReadFromJsonAsync<JsonElement>(Json);
        Assert.False(body.GetProperty("passed").GetBoolean());
        var tcp = body.GetProperty("stages").EnumerateArray().Single(stage => stage.GetProperty("id").GetString() == "tcp");
        Assert.Equal("fail", tcp.GetProperty("status").GetString());
        Assert.Contains("unitId", tcp.GetProperty("hint").GetString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Roles_cover_every_studio_endpoint()
    {
        using var viewer = _factory.CreateClient();
        using var engineer = _factory.CreateClient();
        using var floor = _factory.CreateClient();
        await Authorize(viewer, "viewer", "viewer");
        await Authorize(engineer, "engineer", "engineer");
        await Authorize(floor, "operator", "operator");
        var data = _factory.Services.GetRequiredService<EndpointDataSource>();
        var seen = 0;
        foreach (var endpoint in data.Endpoints.OfType<RouteEndpoint>())
        {
            var raw = endpoint.RoutePattern.RawText ?? "";
            if (raw.Length == 0)
            {
                continue;
            }

            var path = raw.StartsWith('/') ? raw : "/" + raw;
            path = Regex.Replace(path, "\\{[^}]+\\}", "probe");
            if (path.Contains("/live/stream", StringComparison.Ordinal))
            {
                Assert.Equal(ApiPolicy.Read, ApiPolicy.Required("GET", path));
                continue;
            }

            var methods = endpoint.Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods ?? ["GET"];
            foreach (var method in methods)
            {
                if (method is not ("GET" or "POST" or "PUT" or "DELETE" or "HEAD"))
                {
                    continue;
                }

                if (!path.StartsWith("/api", StringComparison.Ordinal) && path != "/healthz")
                {
                    continue;
                }

                seen++;
                var policy = ApiPolicy.Required(method, path);
                using var request = new HttpRequestMessage(new HttpMethod(method), path);
                HttpClient client = policy is ApiPolicy.Anonymous or ApiPolicy.ApiKey
                    ? _factory.CreateClient()
                    : policy == ApiPolicy.Write
                        ? viewer
                        : viewer;
                if (policy is ApiPolicy.Write or ApiPolicy.Admin or ApiPolicy.Read or ApiPolicy.Operate)
                {
                    request.Headers.Authorization = viewer.DefaultRequestHeaders.Authorization;
                    client = viewer;
                }

                if (policy == ApiPolicy.Admin)
                {
                    using var engineerRequest = new HttpRequestMessage(new HttpMethod(method), path);
                    engineerRequest.Headers.Authorization = engineer.DefaultRequestHeaders.Authorization;
                    var engineerResponse = await engineer.SendAsync(engineerRequest);
                    var engineerBody = await engineerResponse.Content.ReadAsStringAsync();
                    Assert.True(
                        engineerResponse.StatusCode == HttpStatusCode.Forbidden,
                        method + " " + path + " engineer -> " + (int)engineerResponse.StatusCode + " " + Trim(engineerBody));
                    Assert.Contains("\"code\":\"forbidden\"", engineerBody, StringComparison.Ordinal);
                }

                var response = await client.SendAsync(request);
                var text = await response.Content.ReadAsStringAsync();
                if (policy == ApiPolicy.Anonymous)
                {
                    Assert.NotEqual(HttpStatusCode.Forbidden, response.StatusCode);
                }
                else if (policy == ApiPolicy.ApiKey)
                {
                    Assert.DoesNotContain("\"code\":\"forbidden\"", text, StringComparison.Ordinal);
                }
                else if (policy is ApiPolicy.Write or ApiPolicy.Admin or ApiPolicy.Operate)
                {
                    Assert.True(
                        response.StatusCode == HttpStatusCode.Forbidden,
                        method + " " + path + " viewer -> " + (int)response.StatusCode + " " + Trim(text));
                    Assert.Contains("\"code\":\"forbidden\"", text, StringComparison.Ordinal);
                    if (policy == ApiPolicy.Operate)
                    {
                        using var operatorRequest = new HttpRequestMessage(new HttpMethod(method), path);
                        operatorRequest.Headers.Authorization = floor.DefaultRequestHeaders.Authorization;
                        var operatorResponse = await floor.SendAsync(operatorRequest);
                        var operatorBody = await operatorResponse.Content.ReadAsStringAsync();
                        Assert.DoesNotContain(
                            "\"code\":\"forbidden\"",
                            operatorBody,
                            StringComparison.Ordinal);
                    }
                }
                else
                {
                    Assert.NotEqual(HttpStatusCode.Unauthorized, response.StatusCode);
                    Assert.DoesNotContain("\"code\":\"forbidden\"", text, StringComparison.Ordinal);
                }
            }
        }

        Assert.True(seen >= 40, "enumerated " + seen);

        var created = await engineer.PostAsJsonAsync("/api/v1/users", new { username = "temp1", role = "viewer", password = "Temp1234" }, Json);
        Assert.Equal(HttpStatusCode.Forbidden, created.StatusCode);
        using var admin = _factory.CreateClient();
        await Authorize(admin, "admin", "admin");
        var ok = await admin.PostAsJsonAsync("/api/v1/users", new { username = "temp1", role = "operator", password = "Temp1234" }, Json);
        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
        var users = await ok.Content.ReadFromJsonAsync<JsonElement>(Json);
        Assert.Contains("operator", users.GetProperty("users").EnumerateArray().Select(user => user.GetProperty("role").GetString()));
    }

    [Fact]
    public async Task Lockout_blocks_the_next_good_password()
    {
        using var admin = _factory.CreateClient();
        await Authorize(admin, "admin", "admin");
        var created = await admin.PostAsJsonAsync("/api/v1/users", new { username = "lockme", role = "viewer", password = "Lock1234" }, Json);
        Assert.Equal(HttpStatusCode.OK, created.StatusCode);
        for (var i = 0; i < 3; i++)
        {
            var denied = await admin.PostAsJsonAsync("/api/v1/auth/login", new { username = "lockme", password = "wrong-pass" }, Json);
            Assert.Equal(HttpStatusCode.Unauthorized, denied.StatusCode);
        }

        var locked = await admin.PostAsJsonAsync("/api/v1/auth/login", new { username = "lockme", password = "Lock1234" }, Json);
        var body = await locked.Content.ReadFromJsonAsync<JsonElement>(Json);
        Assert.Equal(HttpStatusCode.Unauthorized, locked.StatusCode);
        Assert.Equal("locked", body.GetProperty("code").GetString());
        var audit = await admin.GetFromJsonAsync<JsonElement>("/api/v1/audit?limit=20", Json);
        Assert.Contains("auth.lockout", audit.GetRawText(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Clock_rollback_blocks_config_and_keeps_health()
    {
        using var factory = new Phase8Factory();
        using var client = factory.CreateClient();
        await Authorize(client, "admin", "admin");
        await client.GetAsync("/api/v1/license");
        var key = File.ReadAllBytes(Path.Combine(factory.DataDirectory, "security", "state.key"));
        var future = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() + 3_600_000;
        var document = LicenseStateSeal.Create(key, future, LicenseStateSeal.DocumentHash(""), "");
        File.WriteAllText(Path.Combine(factory.DataDirectory, "security", "clock.json"), LicenseStateSeal.Serialize(document));

        var license = await client.GetFromJsonAsync<JsonElement>("/api/v1/license", Json);
        Assert.Equal("clock_rollback", license.GetProperty("tamperCode").GetString());
        Assert.True(license.GetProperty("blocksConfig").GetBoolean());
        Assert.True(license.GetProperty("collectionContinues").GetBoolean());
        Assert.Contains("时钟回拨", license.GetProperty("banner").GetString(), StringComparison.Ordinal);

        var blocked = await client.PutAsJsonAsync("/api/v1/config/devices/blocked-1", Device("blocked-1", "fanuc.fake", "127.0.0.1", 8193), Json);
        var error = await blocked.Content.ReadFromJsonAsync<JsonElement>(Json);
        Assert.Equal(HttpStatusCode.Forbidden, blocked.StatusCode);
        Assert.Equal("security_block", error.GetProperty("code").GetString());

        var health = await client.GetAsync("/healthz");
        Assert.Equal(HttpStatusCode.OK, health.StatusCode);
    }

    [Fact]
    public async Task Zero_minute_session_expires_immediately()
    {
        using var factory = new Phase8Factory();
        factory.SessionMinutes = 0;
        using var client = factory.CreateClient();
        var login = await client.PostAsJsonAsync("/api/v1/auth/login", new { username = "admin", password = "admin" }, Json);
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        var token = (await login.Content.ReadFromJsonAsync<JsonElement>(Json)).GetProperty("token").GetString();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var me = await client.GetAsync("/api/v1/auth/me");
        Assert.Equal(HttpStatusCode.Unauthorized, me.StatusCode);
    }

    private static object Device(string id, string adapter, string host, int port) => new
    {
        apiVersion = "daq.gateway/v1",
        kind = "Device",
        metadata = new { id, displayName = id },
        spec = new
        {
            adapter,
            enabled = true,
            intervalMs = 1000,
            connection = new { host, port, focasTimeoutMs = 3000 }
        }
    };

    private static string Trim(string text) => text.Length <= 240 ? text : text[..240];

    private static async Task Authorize(HttpClient client, string username, string password)
    {
        var response = await client.PostAsJsonAsync("/api/v1/auth/login", new { username, password }, Json);
        var json = await response.Content.ReadAsStringAsync();
        Assert.True(response.IsSuccessStatusCode, json);
        var login = JsonSerializer.Deserialize<JsonElement>(json, Json);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", login.GetProperty("token").GetString());
    }
}

public sealed class Phase8Factory : WebApplicationFactory<Program>
{
    public string DataDirectory { get; } = Directory.CreateTempSubdirectory("phase8-api").FullName;

    public int SessionMinutes { get; set; } = 720;

    public Phase8Factory()
    {
        Environment.SetEnvironmentVariable("STUDIO_DATA", DataDirectory);
    }

    protected override void ConfigureWebHost(Microsoft.AspNetCore.Hosting.IWebHostBuilder builder)
    {
        builder.UseSetting("Studio:DataDirectory", DataDirectory);
        builder.UseSetting("Host:DataDirectory", DataDirectory);
        builder.UseSetting("Host:Acquisition", "off");
        builder.UseSetting("Studio:LockoutThreshold", "3");
        builder.UseSetting("Studio:SessionMinutes", SessionMinutes.ToString(System.Globalization.CultureInfo.InvariantCulture));
        builder.UseSetting("Studio:IdleMinutes", "0");
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
