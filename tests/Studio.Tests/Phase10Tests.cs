using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Adapters.Cnc.Drivers;
using Gateway.Abstractions.Models;
using IotDaq.Licensing;
using IotDaq.Persistence;
using IotDaq.Persistence.Shop;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Studio.Host.Shop;
using Microsoft.Extensions.DependencyInjection;
using Studio.Host.Auth;
using Studio.Host.Central;
using Xunit;

namespace Studio.Tests;

public sealed class Phase10ToolLifeMathTests
{
    [Fact]
    public void Part_count_accumulates_after_a_baseline_and_a_tool_change_starts_over()
    {
        var cursor = new ToolCursor();
        var life = new ToolLifeTotals();
        var limits = new ToolLimits { Count = 10, WarningPercent = 80 };
        var first = ToolLifeMath.Advance(cursor, life, limits, new ToolSignal("T01", 100, true, null, 1_000, "points"));
        Assert.Equal(0, first.CountDelta);
        Assert.Equal("ok", life.Level);

        var second = ToolLifeMath.Advance(cursor, life, limits, new ToolSignal("T01", 108, true, null, 2_000, "points"));
        Assert.Equal(8, second.CountDelta);
        Assert.Equal("warning", second.Level);
        Assert.Equal(1_000, second.CuttingMs);

        var third = ToolLifeMath.Advance(cursor, life, limits, new ToolSignal("T02", 108, false, null, 3_000, "points"));
        Assert.True(third.Switched);
        Assert.Equal(0, third.CountDelta);
        var other = new ToolLifeTotals();
        var fourth = ToolLifeMath.Advance(cursor, other, limits, new ToolSignal("T02", 110, true, null, 4_000, "points"));
        Assert.Equal(2, fourth.UsedCount);
        Assert.Equal(8, life.UsedCount);
    }

    [Fact]
    public void Cutting_time_and_cycle_signal_reach_end_of_life_then_reset()
    {
        var cursor = new ToolCursor();
        var life = new ToolLifeTotals();
        var limits = new ToolLimits { CuttingMinutes = 1, WarningPercent = 50 };
        ToolLifeMath.Advance(cursor, life, limits, new ToolSignal("T01", null, true, 10, 1_000, "points"));
        var step = ToolLifeMath.Advance(cursor, life, limits, new ToolSignal("T01", null, true, 40, 31_000, "points"));
        Assert.Equal(60_000, step.CuttingMs);
        Assert.Equal("eol", step.Level);
        ToolLifeMath.Reset(life);
        Assert.Equal(0, life.UsedCuttingMs);
        Assert.Equal("ok", life.Level);
    }

    [Fact]
    public void Empty_tool_and_idle_do_not_add_life()
    {
        var cursor = new ToolCursor { ToolNumber = "T01", LastPartCount = 4, WasCutting = true, LastUnixMs = 1_000 };
        var life = new ToolLifeTotals { UsedCount = 4 };
        var step = ToolLifeMath.Advance(cursor, life, new ToolLimits { Count = 10 }, new ToolSignal("T00", 9, false, null, 2_000, "points"));
        Assert.False(step.Updated);
        Assert.Equal(4, life.UsedCount);
    }

    [Fact]
    public void Checksum_ignores_newline_style_and_diff_marks_the_edit()
    {
        Assert.Equal(ContentHash.Sha256("A\nB\n"), ContentHash.Sha256("A\r\nB\r\n"));
        var lines = TextDiff.Lines("G01 X10\n", "G01 X12\nM08\n");
        Assert.Contains(lines, line => line.Kind == "remove" && line.Text.Contains("X10", StringComparison.Ordinal));
        Assert.Contains(lines, line => line.Kind == "add" && line.Text.Contains("M08", StringComparison.Ordinal));
    }

    [Fact]
    public void Transfer_catalog_does_not_invent_lsv2_or_focas_file_calls()
    {
        var heidenhain = TransferCatalog.Describe("heidenhain");
        var lsv2 = Assert.Single(heidenhain.Channels, channel => channel.Channel == "lsv2");
        Assert.False(lsv2.CanTransfer);
        Assert.Contains("不向 TNC 写程序", lsv2.Note, StringComparison.Ordinal);

        var fanuc = TransferCatalog.Describe("fanuc");
        var focas = Assert.Single(fanuc.Channels, channel => channel.Channel == "focas");
        Assert.False(focas.CanTransfer);
        Assert.Contains(fanuc.ToolSignals, signal => signal.AdapterId.Contains("focas", StringComparison.OrdinalIgnoreCase) && signal.ItemId == "toolNumber" && signal.Level == DriverCatalog.NotAvailable);
        Assert.Contains(fanuc.Channels, channel => channel.Channel == "ftp" && channel.CanTransfer);
        Assert.Contains(fanuc.Channels, channel => channel.Channel == "dnc-folder" && channel.CanTransfer);
        Assert.Contains("手动计数", fanuc.Counting, StringComparison.Ordinal);
    }
}

public sealed class Phase10ApiTests : IClassFixture<Phase8Factory>
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly Phase8Factory _factory;

    public Phase10ApiTests(Phase8Factory factory) => _factory = factory;

    [Fact]
    public async Task Tool_life_accumulates_warns_and_resets()
    {
        using var engineer = _factory.CreateClient();
        using var floor = _factory.CreateClient();
        using var viewer = _factory.CreateClient();
        await Login(engineer, "engineer", "engineer");
        await Login(floor, "operator", "operator");
        await Login(viewer, "viewer", "viewer");
        var saved = await engineer.PutAsJsonAsync("/api/v1/tools/life-t01", new
        {
            toolNumber = "T01",
            description = "面铣刀",
            lifeLimitCount = 10,
            warningPercent = 80
        }, Json);
        Assert.Equal(HttpStatusCode.OK, saved.StatusCode);
        var denied = await viewer.PutAsJsonAsync("/api/v1/tools/life-t01", new { toolNumber = "T01" }, Json);
        Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        var operatorMaster = await floor.PutAsJsonAsync("/api/v1/tools/life-t9", new { toolNumber = "T09" }, Json);
        Assert.Equal(HttpStatusCode.Forbidden, operatorMaster.StatusCode);

        var database = _factory.Services.GetRequiredService<GatewayPersistence>();
        database.Write([Sample("cnc-life", "toolNumber", "T01", 1_000), Sample("cnc-life", "partCount", 0, 1_000), Sample("cnc-life", "state", "RUNNING", 1_000)]);
        Assert.Equal(HttpStatusCode.OK, (await engineer.PostAsync("/api/v1/tools/tick?deviceId=cnc-life", null)).StatusCode);
        database.Write([Sample("cnc-life", "toolNumber", "T01", 2_000), Sample("cnc-life", "partCount", 8, 2_000), Sample("cnc-life", "state", "RUNNING", 2_000)]);
        await engineer.PostAsync("/api/v1/tools/tick?deviceId=cnc-life", null);
        var warning = database.ListToolLife("cnc-life").Single(row => row.ToolNumber == "T01");
        Assert.Equal(8, warning.UsedCount);
        Assert.Equal("warning", warning.Level);
        Assert.Equal("TOOL-WARN", database.ListAlarms("cnc-life", 20).Single(row => row.Active).Code);

        database.Write([Sample("cnc-life", "toolNumber", "T01", 3_000), Sample("cnc-life", "partCount", 10, 3_000), Sample("cnc-life", "state", "IDLE", 3_000)]);
        await engineer.PostAsync("/api/v1/tools/tick?deviceId=cnc-life", null);
        Assert.Equal("eol", database.ListToolLife("cnc-life").Single(row => row.ToolNumber == "T01").Level);
        Assert.Equal("TOOL-EOL", database.ListAlarms("cnc-life", 20).Single(row => row.Active).Code);

        var viewerChange = await viewer.PostAsJsonAsync("/api/v1/tools/change", new { deviceId = "cnc-life", newToolNumber = "T01", resetLife = true }, Json);
        Assert.Equal(HttpStatusCode.Forbidden, viewerChange.StatusCode);
        var changed = await floor.PostAsJsonAsync("/api/v1/tools/change", new { deviceId = "cnc-life", pocket = 1, oldToolNumber = "T01", newToolNumber = "T01", note = "换刀片", resetLife = true }, Json);
        Assert.Equal(HttpStatusCode.OK, changed.StatusCode);
        Assert.Equal(0, database.ListToolLife("cnc-life").Single(row => row.ToolNumber == "T01").UsedCount);
        Assert.DoesNotContain(database.ListAlarms("cnc-life", 20), row => row.Active && row.Code.StartsWith("TOOL-", StringComparison.Ordinal));
        Assert.Contains("tool.change", (await engineer.GetFromJsonAsync<JsonElement>("/api/v1/audit?limit=20", Json)).GetRawText(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Manual_and_computed_counts_cover_brands_without_tool_data()
    {
        using var engineer = _factory.CreateClient();
        await Login(engineer, "engineer", "engineer");
        await engineer.PutAsJsonAsync("/api/v1/tools/life-manual", new { toolNumber = "M1", description = "手动", lifeLimitCount = 5, warningPercent = 80 }, Json);
        using var floor = _factory.CreateClient();
        await Login(floor, "operator", "operator");
        var counted = await floor.PostAsJsonAsync("/api/v1/tools/count", new { deviceId = "cnc-manual", toolNumber = "M1", count = 4 }, Json);
        Assert.Equal(HttpStatusCode.OK, counted.StatusCode);
        var database = _factory.Services.GetRequiredService<GatewayPersistence>();
        var manual = database.ListToolLife("cnc-manual").Single();
        Assert.Equal(4, manual.UsedCount);
        Assert.Equal("manual", manual.Source);
        Assert.Equal("warning", manual.Level);

        database.Write(
        [
            new Observation { DeviceId = "cnc-computed", Point = "toolNumber", Value = "C1", Timestamp = DateTimeOffset.UnixEpoch.AddMilliseconds(1_000), Computed = true },
            new Observation { DeviceId = "cnc-computed", Point = "partCount", Value = 0, Timestamp = DateTimeOffset.UnixEpoch.AddMilliseconds(1_000), Computed = true }
        ]);
        await engineer.PostAsync("/api/v1/tools/tick?deviceId=cnc-computed", null);
        database.Write(
        [
            new Observation { DeviceId = "cnc-computed", Point = "toolNumber", Value = "C1", Timestamp = DateTimeOffset.UnixEpoch.AddMilliseconds(2_000), Computed = true },
            new Observation { DeviceId = "cnc-computed", Point = "partCount", Value = 3, Timestamp = DateTimeOffset.UnixEpoch.AddMilliseconds(2_000), Computed = true }
        ]);
        await engineer.PostAsync("/api/v1/tools/tick?deviceId=cnc-computed", null);
        var computed = database.ListToolLife("cnc-computed").Single();
        Assert.Equal(3, computed.UsedCount);
        Assert.Equal("computed", computed.Source);
    }

    [Fact]
    public async Task Program_versions_require_approval_and_keep_checksums()
    {
        using var engineer = _factory.CreateClient();
        using var admin = _factory.CreateClient();
        using var viewer = _factory.CreateClient();
        await Login(engineer, "engineer", "engineer");
        await Login(admin, "admin", "admin");
        await Login(viewer, "viewer", "viewer");
        var created = await engineer.PostAsJsonAsync("/api/v1/programs", new
        {
            id = "o100",
            name = "O100",
            comment = "第一版",
            content = "G01 X10\n",
            deviceIds = new[] { "demo-fanuc" }
        }, Json);
        var createdBody = await created.Content.ReadFromJsonAsync<JsonElement>(Json);
        Assert.Equal(HttpStatusCode.OK, created.StatusCode);
        var checksum = createdBody.GetProperty("version").GetProperty("checksum").GetString();
        Assert.Equal(ContentHash.Sha256("G01 X10\n"), checksum);
        Assert.Equal(HttpStatusCode.Forbidden, (await viewer.PostAsJsonAsync("/api/v1/programs", new { name = "X", content = "M30\n" }, Json)).StatusCode);

        var second = await engineer.PostAsJsonAsync("/api/v1/programs/o100/versions", new { content = "G01 X12\nM08\n", comment = "冷却" }, Json);
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        var diff = await engineer.GetFromJsonAsync<JsonElement>("/api/v1/programs/o100/diff?from=1&to=2", Json);
        Assert.Contains(diff.GetProperty("lines").EnumerateArray(), line => line.GetProperty("kind").GetString() == "add");
        var versionId = (await second.Content.ReadFromJsonAsync<JsonElement>(Json)).GetProperty("id").GetString();

        var folder = Directory.CreateTempSubdirectory("nc-drop").FullName;
        try
        {
            var blocked = await engineer.PostAsJsonAsync("/api/v1/programs/o100/send", new { versionId, deviceId = "demo-fanuc", channel = "dnc-folder", folder }, Json);
            Assert.Equal(HttpStatusCode.Conflict, blocked.StatusCode);
            Assert.Contains("program_not_approved", await blocked.Content.ReadAsStringAsync(), StringComparison.Ordinal);
            Assert.Equal(HttpStatusCode.Forbidden, (await engineer.PostAsJsonAsync("/api/v1/programs/o100/approve", new { versionId }, Json)).StatusCode);
            var approved = await admin.PostAsJsonAsync("/api/v1/programs/o100/approve", new { versionId }, Json);
            Assert.Equal(HttpStatusCode.OK, approved.StatusCode);
            Assert.Equal(HttpStatusCode.BadRequest, (await engineer.PostAsJsonAsync("/api/v1/programs/o100/send", new { versionId, deviceId = "demo-fanuc", channel = "lsv2", folder }, Json)).StatusCode);
            var sent = await engineer.PostAsJsonAsync("/api/v1/programs/o100/send", new { versionId, deviceId = "demo-fanuc", channel = "dnc-folder", folder }, Json);
            var sentBody = await sent.Content.ReadFromJsonAsync<JsonElement>(Json);
            Assert.Equal(HttpStatusCode.OK, sent.StatusCode);
            Assert.Equal(checksum is null ? "" : ContentHash.Sha256("G01 X12\nM08\n"), sentBody.GetProperty("checksum").GetString());
            Assert.True(File.Exists(sentBody.GetProperty("path").GetString()));
            var audit = await admin.GetFromJsonAsync<JsonElement>("/api/v1/audit?limit=30", Json);
            var text = audit.GetRawText();
            Assert.Contains("program.upload", text, StringComparison.Ordinal);
            Assert.Contains("program.approve", text, StringComparison.Ordinal);
            Assert.Contains("program.send", text, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(folder, true);
        }
    }

    [Fact]
    public void Startup_upgrades_v8_tables()
    {
        var directory = Directory.CreateTempSubdirectory("phase10-schema").FullName;
        try
        {
            var store = new Studio.Host.Config.ConfigStore(directory);
            store.EnsureInitialized();
            using (var db = store.Database.CreateContext())
            {
                db.Database.ExecuteSqlRaw("DROP TABLE IF EXISTS tools");
                db.Database.ExecuteSqlRaw("DROP TABLE IF EXISTS tool_life");
                db.Database.ExecuteSqlRaw("DROP TABLE IF EXISTS nc_programs");
                db.Database.ExecuteSqlRaw("DROP TABLE IF EXISTS fleet_gateways");
                db.Database.ExecuteSqlRaw("DROP TABLE IF EXISTS central_templates");
                db.Database.ExecuteSqlRaw("DROP TABLE IF EXISTS rollouts");

                db.Database.ExecuteSqlRaw("UPDATE schema_info SET Version = 8 WHERE Id = 1");
            }

            store.Database.EnsureReady();
            using var check = store.Database.CreateContext();
            Assert.Equal(9, check.SchemaInfo.AsNoTracking().Single().Version);
            Assert.Empty(check.Tools.ToList());
            Assert.Empty(check.FleetGateways.ToList());
            Assert.Empty(check.NcPrograms.ToList());
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    private static Observation Sample(string deviceId, string point, object value, long unixMs) => new()
    {
        DeviceId = deviceId,
        Point = point,
        Value = value,
        Timestamp = DateTimeOffset.UnixEpoch.AddMilliseconds(unixMs)
    };

    private static async Task Login(HttpClient client, string username, string password)
    {
        var response = await client.PostAsJsonAsync("/api/v1/auth/login", new { username, password }, Json);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>(Json);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", json.GetProperty("token").GetString());
    }
}

public sealed class Phase10CentralTests
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    [Fact]
    public async Task Enrollment_push_rollback_and_staged_upgrade()
    {
        var keys = LicenseCrypto.Generate();
        await using var central = new CentralFactory(keys.PublicSpki, "central");
        using var centralClient = central.CreateClient();
        await Login(centralClient, "admin", "admin");
        var tokenResponse = await centralClient.PostAsJsonAsync("/api/v1/central/tokens", new { label = "车间", maxUses = 4, days = 2 }, Json);
        var tokenBody = await tokenResponse.Content.ReadFromJsonAsync<JsonElement>(Json);
        Assert.Equal(HttpStatusCode.OK, tokenResponse.StatusCode);
        var token = tokenBody.GetProperty("token").GetString()!;

        await using var edge1 = new EdgeFactory(central, keys.PublicSpki, "edge-1", "边缘 1", token);
        await using var edge2 = new EdgeFactory(central, keys.PublicSpki, "edge-2", "边缘 2", token);
        using var one = edge1.CreateClient();
        using var two = edge2.CreateClient();
        await Login(one, "admin", "admin");
        await Login(two, "admin", "admin");
        Assert.Equal(HttpStatusCode.OK, (await one.PostAsync("/api/v1/agent/pulse", null)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await two.PostAsync("/api/v1/agent/pulse", null)).StatusCode);
        var fleet = await centralClient.GetFromJsonAsync<JsonElement>("/api/v1/central/gateways", Json);
        var ids = fleet.GetProperty("gateways").EnumerateArray().Select(row => row.GetProperty("id").GetString()).ToArray();
        Assert.Contains("edge-1", ids);
        Assert.Contains("edge-2", ids);
        Assert.All(fleet.GetProperty("gateways").EnumerateArray(), row => Assert.False(string.IsNullOrWhiteSpace(row.GetProperty("version").GetString())));

        const string v1 = """{"rules":[{"id":"central-load","name":"主轴","expression":"spindleLoad > 90","enabled":true,"actionsJson":"[]"}]}""";
        const string v2 = """{"rules":[{"id":"central-load","name":"主轴","expression":"spindleLoad > 80","enabled":true,"durationMs":5000,"actionsJson":"[]"}]}""";
        Assert.Equal(HttpStatusCode.OK, (await centralClient.PutAsJsonAsync("/api/v1/central/templates/spindle", new { name = "主轴", kind = "rules", body = v1 }, Json)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await centralClient.PutAsJsonAsync("/api/v1/central/templates/spindle", new { name = "主轴", kind = "rules", body = v2, comment = "80" }, Json)).StatusCode);
        var diff = await centralClient.GetFromJsonAsync<JsonElement>("/api/v1/central/templates/spindle/diff?from=1&to=2", Json);
        Assert.Contains("80", diff.GetProperty("diff").GetString(), StringComparison.Ordinal);

        var push1 = await centralClient.PostAsJsonAsync("/api/v1/central/pushes", new { templateKey = "spindle", version = 1, gatewayIds = new[] { "edge-1", "edge-2" }, conflictPolicy = "central-wins" }, Json);
        Assert.Equal(HttpStatusCode.OK, push1.StatusCode);
        await one.PostAsync("/api/v1/agent/pulse", null);
        await two.PostAsync("/api/v1/agent/pulse", null);
        Assert.Equal(1, edge1.Database.FindCentralDocument("rules")!.Version);
        Assert.Equal("spindleLoad > 90", edge1.Database.ListEdgeRules().Single(row => row.Id == "central-load").Expression);

        var push2 = await centralClient.PostAsJsonAsync("/api/v1/central/pushes", new { templateKey = "spindle", version = 2, gatewayIds = new[] { "edge-1" }, conflictPolicy = "central-wins" }, Json);
        var push2Id = (await push2.Content.ReadFromJsonAsync<JsonElement>(Json)).GetProperty("id").GetString();
        await one.PostAsync("/api/v1/agent/pulse", null);
        Assert.Equal(2, edge1.Database.FindCentralDocument("rules")!.Version);
        Assert.Contains("5000", edge1.Database.FindCentralDocument("rules")!.BodyJson, StringComparison.Ordinal);

        var rollback = await centralClient.PostAsync("/api/v1/central/pushes/" + push2Id + "/rollback", null);
        Assert.Equal(HttpStatusCode.OK, rollback.StatusCode);
        Assert.True((await rollback.Content.ReadFromJsonAsync<JsonElement>(Json)).GetProperty("rollback").GetBoolean());
        await one.PostAsync("/api/v1/agent/pulse", null);
        Assert.Equal(1, edge1.Database.FindCentralDocument("rules")!.Version);

        Assert.Equal(HttpStatusCode.OK, (await one.PostAsJsonAsync("/api/v1/agent/dirty", new { kind = "rules" }, Json)).StatusCode);
        await centralClient.PostAsJsonAsync("/api/v1/central/pushes", new { templateKey = "spindle", version = 2, gatewayIds = new[] { "edge-1" }, conflictPolicy = "local-wins" }, Json);
        await one.PostAsync("/api/v1/agent/pulse", null);
        Assert.Equal(1, edge1.Database.FindCentralDocument("rules")!.Version);
        Assert.Contains("conflict", (await centralClient.GetFromJsonAsync<JsonElement>("/api/v1/central/pushes", Json)).GetRawText(), StringComparison.Ordinal);

        using var privateKey = LicenseCrypto.CreatePrivate(keys.PrivatePem);
        var zip = UpgradePackage.Create(
            [("Host.dll", Encoding.UTF8.GetBytes("host-bytes")), ("wwwroot/index.html", Encoding.UTF8.GetBytes("<html></html>"))],
            privateKey,
            "0.11.0");
        var staged = await centralClient.PostAsync("/api/v1/central/rollouts?gateways=edge-1", new ByteArrayContent(zip));
        Assert.Equal(HttpStatusCode.OK, staged.StatusCode);
        await one.PostAsync("/api/v1/agent/pulse", null);
        var rollouts = await centralClient.GetFromJsonAsync<JsonElement>("/api/v1/central/rollouts", Json);
        var edgeTarget = rollouts.GetProperty("targets").EnumerateArray().Single(row => row.GetProperty("gatewayId").GetString() == "edge-1");
        Assert.Equal("staged", edgeTarget.GetProperty("status").GetString());
        Assert.Equal("staged", Studio.Host.Licensing.UpgradeCoordinator.Read(edge1.DataDirectory)!.Phase);

        await Task.Delay(1200);
        var scan = await centralClient.PostAsync("/api/v1/central/scan", null);
        Assert.Equal(HttpStatusCode.OK, scan.StatusCode);
        var alerts = await centralClient.GetFromJsonAsync<JsonElement>("/api/v1/central/alerts", Json);
        Assert.Contains(alerts.GetProperty("alerts").EnumerateArray(), row => row.GetProperty("active").GetBoolean());

        await using var lonely = new LonelyFactory();
        using var lonelyClient = lonely.CreateClient();
        await Login(lonelyClient, "admin", "admin");
        var health = await lonelyClient.GetAsync("/healthz");
        Assert.Equal(HttpStatusCode.OK, health.StatusCode);
        var pulse = await lonelyClient.PostAsJsonAsync("/api/v1/agent/pulse", new { }, Json);
        var pulseBody = await pulse.Content.ReadFromJsonAsync<JsonElement>(Json);
        Assert.Equal(HttpStatusCode.OK, pulse.StatusCode);
        Assert.Contains("独立运行", pulseBody.GetProperty("message").GetString(), StringComparison.Ordinal);
    }

    private static async Task Login(HttpClient client, string username, string password)
    {
        var response = await client.PostAsJsonAsync("/api/v1/auth/login", new { username, password }, Json);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>(Json);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", json.GetProperty("token").GetString());
    }

    private sealed class CentralFactory : WebApplicationFactory<Program>
    {
        public CentralFactory(string publicKey, string name)
        {
            DataDirectory = Directory.CreateTempSubdirectory("phase10-" + name).FullName;
            PublicKey = publicKey;
        }

        public string DataDirectory { get; }

        private string PublicKey { get; }

        protected override void ConfigureWebHost(Microsoft.AspNetCore.Hosting.IWebHostBuilder builder)
        {
            builder.UseSetting("Host:DataDirectory", DataDirectory);
            builder.UseSetting("Host:IgnoreDataEnvironment", "true");
            builder.UseSetting("Host:Mode", "central");
            builder.UseSetting("Host:Acquisition", "off");
            builder.UseSetting("Central:AutoScan", "false");
            builder.UseSetting("Central:OfflineAfterSeconds", "1");
            builder.UseSetting("Licensing:PublicKeySpki", PublicKey);
        }

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            TryDelete(DataDirectory);
        }
    }

    private sealed class EdgeFactory : WebApplicationFactory<Program>
    {
        private readonly CentralFactory _central;

        public EdgeFactory(CentralFactory central, string publicKey, string gatewayId, string name, string token)
        {
            _central = central;
            DataDirectory = Directory.CreateTempSubdirectory("phase10-" + gatewayId).FullName;
            GatewayId = gatewayId;
            Name = name;
            Token = token;
            PublicKey = publicKey;
        }

        public string DataDirectory { get; }

        public GatewayPersistence Database => Services.GetRequiredService<GatewayPersistence>();

        private string GatewayId { get; }

        private string Name { get; }

        private string Token { get; }

        private string PublicKey { get; }

        protected override void ConfigureWebHost(Microsoft.AspNetCore.Hosting.IWebHostBuilder builder)
        {
            builder.UseSetting("Host:DataDirectory", DataDirectory);
            builder.UseSetting("Host:IgnoreDataEnvironment", "true");
            builder.UseSetting("Host:Acquisition", "off");
            builder.UseSetting("Central:Url", "http://central.test/");
            builder.UseSetting("Central:GatewayId", GatewayId);
            builder.UseSetting("Central:Name", Name);
            builder.UseSetting("Central:EnrollmentToken", Token);
            builder.UseSetting("Central:AutoHeartbeat", "false");
            builder.UseSetting("Licensing:PublicKeySpki", PublicKey);
            builder.ConfigureTestServices(services =>
            {
                services.AddSingleton<ICentralTransport>(_ => new LoopbackTransport(_central.Server.CreateHandler()));
            });
        }

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            TryDelete(DataDirectory);
        }
    }

    private sealed class LonelyFactory : WebApplicationFactory<Program>
    {
        public LonelyFactory() => DataDirectory = Directory.CreateTempSubdirectory("phase10-lonely").FullName;

        private string DataDirectory { get; }

        protected override void ConfigureWebHost(Microsoft.AspNetCore.Hosting.IWebHostBuilder builder)
        {
            builder.UseSetting("Host:DataDirectory", DataDirectory);
            builder.UseSetting("Host:IgnoreDataEnvironment", "true");
            builder.UseSetting("Host:Acquisition", "off");
            builder.UseSetting("Central:Url", "http://127.0.0.1:1");
            builder.UseSetting("Central:GatewayId", "lonely");
            builder.UseSetting("Central:EnrollmentToken", "missing");
            builder.UseSetting("Central:AutoHeartbeat", "false");
        }

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            TryDelete(DataDirectory);
        }
    }

    private sealed class LoopbackTransport(HttpMessageHandler handler) : ICentralTransport
    {
        private readonly HttpClient _client = new(handler, disposeHandler: false);

        public Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.RequestUri is { IsAbsoluteUri: false })
            {
                request.RequestUri = new Uri(new Uri("http://central.test/"), request.RequestUri);
            }

            return _client.SendAsync(request, cancellationToken);
        }
    }

    private static void TryDelete(string directory)
    {
        try
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, true);
            }
        }
        catch (IOException)
        {
        }
    }
}
