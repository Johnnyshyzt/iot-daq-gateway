using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using IotDaq.Persistence;
using IotDaq.Persistence.Visualization;
using Microsoft.EntityFrameworkCore;
using Studio.Host.Notifications;
using Xunit;

namespace Studio.Tests;

public sealed class NotificationRuleTests
{
    [Fact]
    public void Escalation_waits_for_the_deadline_and_stops_when_quiet_or_acked()
    {
        var rule = new NotificationRuleRow
        {
            Enabled = true,
            EscalationMinutes = 10,
            EscalationChannelId = "ops",
            OnRaise = true
        };
        var raised = new DateTimeOffset(2026, 9, 27, 8, 0, 0, TimeSpan.Zero);
        var notice = new AlarmNotice
        {
            Kind = "raise",
            Active = true,
            Acknowledged = false,
            Raised = raised,
            Severity = "alarm",
            Code = "SV0401",
            DeviceId = "cnc-01"
        };
        Assert.False(NotificationRules.ShouldEscalate(rule, notice, raised.AddMinutes(9), false, false));
        Assert.True(NotificationRules.ShouldEscalate(rule, notice, raised.AddMinutes(10), false, false));
        Assert.False(NotificationRules.ShouldEscalate(rule, notice, raised.AddMinutes(20), alreadyEscalated: true, quiet: false));
        var acked = new AlarmNotice
        {
            Kind = "raise",
            Active = true,
            Acknowledged = true,
            Raised = raised
        };
        Assert.False(NotificationRules.ShouldEscalate(rule, acked, raised.AddMinutes(20), false, false));
        Assert.False(NotificationRules.ShouldEscalate(rule, notice, raised.AddMinutes(20), false, quiet: true));
    }

    [Fact]
    public void Filters_cover_devices_codes_quiet_hours_and_rate_limits()
    {
        var rule = new NotificationRuleRow
        {
            Enabled = true,
            OnRaise = true,
            OnClear = false,
            DeviceIdsJson = "[\"cnc-01\"]",
            SeveritiesJson = "[\"alarm\"]",
            CodeFilter = "SV*,100"
        };
        var match = new AlarmNotice { Kind = "raise", DeviceId = "cnc-01", Severity = "alarm", Code = "SV0401" };
        var other = new AlarmNotice { Kind = "raise", DeviceId = "cnc-02", Severity = "alarm", Code = "SV0401" };
        var clear = new AlarmNotice { Kind = "clear", DeviceId = "cnc-01", Severity = "alarm", Code = "100" };
        Assert.True(NotificationRules.Matches(rule, match));
        Assert.False(NotificationRules.Matches(rule, other));
        Assert.False(NotificationRules.Matches(rule, clear));
        Assert.False(NotificationRules.CodeMatches("SV*", "AL1"));
        Assert.True(NotificationRules.CodeMatches("SV*", "sv9"));

        var zone = TimeZoneInfo.Utc;
        Assert.True(NotificationRules.InQuietHours("22:00", "07:00", new DateTimeOffset(2026, 9, 27, 23, 0, 0, TimeSpan.Zero), zone));
        Assert.True(NotificationRules.InQuietHours("22:00", "07:00", new DateTimeOffset(2026, 9, 27, 6, 0, 0, TimeSpan.Zero), zone));
        Assert.False(NotificationRules.InQuietHours("22:00", "07:00", new DateTimeOffset(2026, 9, 27, 12, 0, 0, TimeSpan.Zero), zone));
        Assert.False(NotificationRules.AllowSend(recentSame: 1, dedupSeconds: 300, sentThisHour: 0, ratePerHour: 30));
        Assert.False(NotificationRules.AllowSend(0, 0, sentThisHour: 30, ratePerHour: 30));
        Assert.True(NotificationRules.AllowSend(0, 300, 1, 30));
    }

    [Fact]
    public void Daily_report_is_due_once_after_the_local_time()
    {
        var schedule = new ReportScheduleRow { Enabled = true, DailyEnabled = true, DailyTime = "08:00" };
        var before = new DateTimeOffset(2026, 9, 27, 7, 59, 0, TimeSpan.Zero);
        var after = new DateTimeOffset(2026, 9, 27, 8, 0, 0, TimeSpan.Zero);
        Assert.Null(ReportScheduleMath.DueDaily(schedule, before, TimeZoneInfo.Utc));
        Assert.Equal("2026-09-26", ReportScheduleMath.DueDaily(schedule, after, TimeZoneInfo.Utc));
        schedule.LastDailyKey = "2026-09-26";
        Assert.Null(ReportScheduleMath.DueDaily(schedule, after.AddHours(1), TimeZoneInfo.Utc));
    }
}

public sealed class ChannelPayloadTests
{
    [Fact]
    public void DingTalk_and_Feishu_signatures_match_the_documented_hmac()
    {
        const string secret = "SEC123456";
        Assert.Equal("VVc2P02eklfHdTAByVmwhBjVhaWvwMbBD/AMgLBhl84=", ChannelPayloads.DingTalkSign(secret, 1620000000000));
        Assert.Equal("EB0+zHG+oTnASFWe2hgcwyWasfFk6NtDlkxGT89tp10=", ChannelPayloads.FeishuSign(secret, 1620000000));

        var now = DateTimeOffset.FromUnixTimeMilliseconds(1620000000000);
        var channel = new NotificationChannelRow
        {
            Kind = "dingtalk",
            WebhookUrl = "https://oapi.dingtalk.com/robot/send?access_token=token",
            Secret = secret
        };
        var prepared = ChannelPayloads.Prepare(channel, secret, new OutboundMessage { Title = "采集网关", Text = "hello" }, now);
        Assert.NotNull(prepared);
        Assert.Contains("timestamp=1620000000000", prepared!.Url, StringComparison.Ordinal);
        Assert.Contains("sign=", prepared.Url, StringComparison.Ordinal);
        Assert.Contains("\"msgtype\":\"markdown\"", prepared.Body, StringComparison.Ordinal);
        Assert.Contains("\"title\":\"采集网关\"", prepared.Body, StringComparison.Ordinal);

        var feishu = ChannelPayloads.Prepare(
            new NotificationChannelRow { Kind = "feishu", WebhookUrl = "https://open.feishu.cn/open-apis/bot/v2/hook/abc", Secret = secret },
            secret,
            new OutboundMessage { Text = "hello" },
            DateTimeOffset.FromUnixTimeSeconds(1620000000));
        Assert.Contains("\"msg_type\":\"text\"", feishu!.Body, StringComparison.Ordinal);
        Assert.Contains("\"timestamp\":\"1620000000\"", feishu.Body, StringComparison.Ordinal);
        Assert.Contains(ChannelPayloads.FeishuSign(secret, 1620000000), feishu.Body, StringComparison.Ordinal);

        var wecom = ChannelPayloads.Prepare(
            new NotificationChannelRow { Kind = "wecom", WebhookUrl = "https://qyapi.weixin.qq.com/cgi-bin/webhook/send?key=abc" },
            "",
            new OutboundMessage { Text = "车间报警" },
            now);
        Assert.Contains("\"msgtype\":\"markdown\"", wecom!.Body, StringComparison.Ordinal);
        Assert.Contains("车间报警", wecom.Body, StringComparison.Ordinal);

        var generic = ChannelPayloads.Prepare(
            new NotificationChannelRow { Kind = "webhook", WebhookUrl = "https://example.test/hook" },
            secret,
            new OutboundMessage { Kind = "alarm.raised", DeviceId = "cnc-01", Code = "100", Text = "报警", UnixMs = 5 },
            now);
        Assert.Equal("sha256=" + ChannelPayloads.GenericSignature(secret, generic!.Body), generic.Headers["X-Daq-Signature"]);
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
        var expected = Convert.ToHexString(hmac.ComputeHash(Encoding.UTF8.GetBytes(generic.Body))).ToLowerInvariant();
        Assert.Equal("sha256=" + expected, generic.Headers["X-Daq-Signature"]);
    }

    [Fact]
    public void Webhook_urls_hide_query_secrets()
    {
        var redacted = ChannelPayloads.RedactUrl("https://qyapi.weixin.qq.com/cgi-bin/webhook/send?key=super-secret");
        Assert.Contains("key=***", redacted, StringComparison.Ordinal);
        Assert.DoesNotContain("super-secret", redacted, StringComparison.Ordinal);
        Assert.True(ChannelPayloads.LooksRedacted(redacted));
    }
}

public sealed class SchemaUpgradeTests
{
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void Startup_upgrades_previous_schemas_to_v4(int fromVersion)
    {
        var directory = Directory.CreateTempSubdirectory("schema-" + fromVersion).FullName;
        try
        {
            var store = new Studio.Host.Config.ConfigStore(directory);
            store.EnsureInitialized();
            store.Database.AppendAudit("admin", "admin", "device.upsert", "cnc-01", "升级前");
            using (var db = store.Database.CreateContext())
            {
                if (fromVersion < 4)
                {
                    db.Database.ExecuteSqlRaw("DROP TABLE IF EXISTS notification_channels");
                    db.Database.ExecuteSqlRaw("DROP TABLE IF EXISTS notification_rules");
                    db.Database.ExecuteSqlRaw("DROP TABLE IF EXISTS notification_deliveries");
                    db.Database.ExecuteSqlRaw("DROP TABLE IF EXISTS report_schedules");
                }

                if (fromVersion < 3)
                {
                    db.Database.ExecuteSqlRaw("DROP TABLE IF EXISTS audit_events");
                }

                if (fromVersion < 2)
                {
                    db.Database.ExecuteSqlRaw("DROP TABLE IF EXISTS state_transitions");
                    db.Database.ExecuteSqlRaw("DROP TABLE IF EXISTS app_settings");
                    db.Database.ExecuteSqlRaw("""
                        CREATE TABLE alarms_v1 (
                          Id TEXT NOT NULL PRIMARY KEY,
                          DeviceId TEXT NOT NULL,
                          PointId TEXT NOT NULL,
                          Message TEXT NOT NULL,
                          Severity TEXT NOT NULL,
                          Active INTEGER NOT NULL,
                          RaisedUnixMs INTEGER NOT NULL
                        )
                        """);
                    db.Database.ExecuteSqlRaw("""
                        INSERT INTO alarms_v1 (Id, DeviceId, PointId, Message, Severity, Active, RaisedUnixMs)
                        SELECT Id, DeviceId, PointId, Message, Severity, Active, RaisedUnixMs FROM alarms
                        """);
                    db.Database.ExecuteSqlRaw("DROP TABLE alarms");
                    db.Database.ExecuteSqlRaw("ALTER TABLE alarms_v1 RENAME TO alarms");
                }

                db.Database.ExecuteSqlRaw("UPDATE schema_info SET Version = {0} WHERE Id = 1", fromVersion);
            }

            store.Database.EnsureReady();
            using var check = store.Database.CreateContext();
            Assert.Equal(GatewayPersistence.SchemaVersion, check.SchemaInfo.AsNoTracking().Single().Version);
            Assert.True(TableExists(check, "notification_channels"));
            Assert.True(TableExists(check, "notification_rules"));
            Assert.True(TableExists(check, "notification_deliveries"));
            Assert.True(TableExists(check, "report_schedules"));
            Assert.True(TableExists(check, "audit_events"));
            Assert.True(ColumnExists(check, "alarms", "Code"));
            Assert.True(store.Database.HasSlot("published"));
            if (fromVersion >= 3)
            {
                Assert.Contains(store.Database.ListAudit(20), row => row.Action == "device.upsert");
            }
        }
        finally
        {
            try
            {
                Directory.Delete(directory, recursive: true);
            }
            catch (IOException)
            {
                // SQLite may still be releasing the file.
            }
        }
    }

    private static bool TableExists(GatewayDbContext db, string name)
    {
        var connection = db.Database.GetDbConnection();
        var shouldClose = connection.State != System.Data.ConnectionState.Open;
        if (shouldClose)
        {
            connection.Open();
        }

        try
        {
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT 1 FROM sqlite_master WHERE type = 'table' AND name = $name";
            var parameter = command.CreateParameter();
            parameter.ParameterName = "$name";
            parameter.Value = name;
            command.Parameters.Add(parameter);
            return command.ExecuteScalar() is not null;
        }
        finally
        {
            if (shouldClose)
            {
                connection.Close();
            }
        }
    }

    private static bool ColumnExists(GatewayDbContext db, string table, string column)
    {
        var connection = db.Database.GetDbConnection();
        var shouldClose = connection.State != System.Data.ConnectionState.Open;
        if (shouldClose)
        {
            connection.Open();
        }

        try
        {
            using var command = connection.CreateCommand();
            command.CommandText = $"SELECT 1 FROM pragma_table_info('{table}') WHERE name = $name";
            var parameter = command.CreateParameter();
            parameter.ParameterName = "$name";
            parameter.Value = column;
            command.Parameters.Add(parameter);
            return command.ExecuteScalar() is not null;
        }
        finally
        {
            if (shouldClose)
            {
                connection.Close();
            }
        }
    }
}

[Collection("studio-host")]
public sealed class Phase5ApiTests : IClassFixture<StudioApiFactory>
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private readonly StudioApiFactory _factory;

    public Phase5ApiTests(StudioApiFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Channel_secret_is_not_returned_and_test_webhook_is_signed()
    {
        var port = FreePort();
        using var listener = new HttpListener();
        listener.Prefixes.Add($"http://127.0.0.1:{port}/hook/");
        listener.Start();
        using var client = _factory.CreateClient();
        await Authorize(client, "engineer", "engineer");
        const string secret = "phase5-webhook-secret";
        var created = await Read<ChannelView>(await client.PutAsJsonAsync("/api/v1/notifications/channels", new ChannelWrite
        {
            Name = "测试 Webhook",
            Kind = "webhook",
            Enabled = true,
            WebhookUrl = $"http://127.0.0.1:{port}/hook/",
            Secret = secret
        }, Json));
        Assert.True(created.HasSecret);
        var listed = await client.GetStringAsync("/api/v1/notifications/channels");
        Assert.DoesNotContain(secret, listed, StringComparison.Ordinal);

        var bodyBox = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        var signatureBox = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        var serve = Task.Run(async () =>
        {
            var context = await listener.GetContextAsync();
            using var reader = new StreamReader(context.Request.InputStream, context.Request.ContentEncoding);
            bodyBox.TrySetResult(await reader.ReadToEndAsync());
            signatureBox.TrySetResult(context.Request.Headers["X-Daq-Signature"] ?? "");
            context.Response.StatusCode = 200;
            context.Response.Close();
        });
        var delivery = await Read<DeliveryView>(await client.PostAsync($"/api/v1/notifications/channels/{created.Id}/test", null));
        Assert.Equal("sent", delivery.Status);
        var body = await bodyBox.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var signature = await signatureBox.Task;
        Assert.Contains("notify.test", body, StringComparison.Ordinal);
        Assert.Equal("sha256=" + ChannelPayloads.GenericSignature(secret, body), signature);
        await serve;

        var rule = await Read<RuleView>(await client.PutAsJsonAsync("/api/v1/notifications/rules", new RuleWrite
        {
            Name = "全部报警",
            Enabled = true,
            ChannelId = created.Id,
            OnRaise = true,
            EscalationMinutes = 5,
            EscalationChannelId = created.Id,
            DedupSeconds = 60,
            RatePerHour = 10
        }, Json));
        Assert.Equal(5, rule.EscalationMinutes);

        var audit = await client.GetStringAsync("/api/v1/audit?limit=20");
        Assert.Contains("notify.channel", audit, StringComparison.Ordinal);
        Assert.Contains("notify.rule", audit, StringComparison.Ordinal);
        Assert.DoesNotContain(secret, audit, StringComparison.Ordinal);

        await client.DeleteAsync($"/api/v1/notifications/rules/{rule.Id}");
        await client.DeleteAsync($"/api/v1/notifications/channels/{created.Id}");
    }

    [Fact]
    public async Task Smtp_test_message_reaches_a_local_server()
    {
        var port = FreePort();
        using var server = new TcpListener(IPAddress.Loopback, port);
        server.Start();
        var accepted = server.AcceptTcpClientAsync();
        using var client = _factory.CreateClient();
        await Authorize(client, "admin", "admin");
        var created = await Read<ChannelView>(await client.PutAsJsonAsync("/api/v1/notifications/channels", new ChannelWrite
        {
            Name = "测试邮件",
            Kind = "smtp",
            Enabled = true,
            SmtpHost = "127.0.0.1",
            SmtpPort = port,
            SmtpSsl = false,
            MailFrom = "gateway@localhost",
            MailTo = "ops@localhost"
        }, Json));
        var testTask = client.PostAsync($"/api/v1/notifications/channels/{created.Id}/test", null);
        using var tcp = await accepted.WaitAsync(TimeSpan.FromSeconds(5));
        using var stream = tcp.GetStream();
        using var reader = new StreamReader(stream, Encoding.ASCII, leaveOpen: true);
        using var writer = new StreamWriter(stream, Encoding.ASCII, leaveOpen: true) { AutoFlush = true, NewLine = "\r\n" };
        await writer.WriteLineAsync("220 localhost");
        var data = false;
        var body = new StringBuilder();
        while (true)
        {
            var line = await reader.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(5));
            if (line is null)
            {
                break;
            }

            if (data)
            {
                if (line == ".")
                {
                    await writer.WriteLineAsync("250 ok");
                    data = false;
                }
                else
                {
                    body.AppendLine(line);
                }

                continue;
            }

            if (line.StartsWith("DATA", StringComparison.OrdinalIgnoreCase))
            {
                await writer.WriteLineAsync("354 go");
                data = true;
            }
            else if (line.StartsWith("QUIT", StringComparison.OrdinalIgnoreCase))
            {
                await writer.WriteLineAsync("221 bye");
                break;
            }
            else
            {
                await writer.WriteLineAsync("250 ok");
            }
        }

        var delivery = await Read<DeliveryView>(await testTask);
        Assert.Equal("sent", delivery.Status);
        var raw = body.ToString().Replace("\r", "", StringComparison.Ordinal);
        Assert.Contains("gateway@localhost", raw, StringComparison.Ordinal);
        var decoded = raw;
        var marker = raw.IndexOf("\n\n", StringComparison.Ordinal);
        if (marker >= 0)
        {
            var payload = raw[(marker + 2)..].Replace("\n", "", StringComparison.Ordinal);
            try
            {
                decoded += Encoding.UTF8.GetString(Convert.FromBase64String(payload));
            }
            catch (FormatException)
            {
                decoded += payload;
            }
        }

        Assert.Contains("测试通知", decoded, StringComparison.Ordinal);
        await client.DeleteAsync($"/api/v1/notifications/channels/{created.Id}");
    }

    private static int FreePort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    private static async Task Authorize(HttpClient client, string username, string password)
    {
        var response = await client.PostAsJsonAsync("/api/v1/auth/login", new Studio.Contracts.LoginRequest
        {
            Username = username,
            Password = password
        }, Json);
        var login = await Read<Studio.Contracts.LoginResponse>(response);
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

    private sealed class ChannelView
    {
        public string Id { get; set; } = "";

        public bool HasSecret { get; set; }
    }

    private sealed class RuleView
    {
        public string Id { get; set; } = "";

        public int EscalationMinutes { get; set; }
    }

    private sealed class DeliveryView
    {
        public string Status { get; set; } = "";
    }
}
