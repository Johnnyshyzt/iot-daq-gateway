using System.Globalization;
using Microsoft.AspNetCore.Routing;
using Gateway.Abstractions.Contracts;
using Gateway.Abstractions.Reliability;
using IotDaq.Persistence;
using IotDaq.Persistence.Visualization;
using IotDaq.Licensing;
using Studio.Host.Auth;
using Studio.Host.Endpoints;
using Studio.Host.Licensing;

namespace Studio.Host.Notifications;

public static class NotificationEndpoints
{
    private static readonly HashSet<string> Kinds = new(StringComparer.OrdinalIgnoreCase)
    {
        "wecom", "dingtalk", "feishu", "smtp", "webhook"
    };

    public static void Map(RouteGroupBuilder api)
    {
        api.MapGet("/notifications/channels", (GatewayPersistence database) =>
            ApiResults.Ok(new { channels = database.ListNotificationChannels().Select(ToView).ToList() }));
        api.MapPut("/notifications/channels", (ChannelWrite? body, HttpContext http, GatewayPersistence database, LicenseService licensing) =>
        {
            if (!licensing.Allows(LicenseFeatures.AlarmNotifications))
            {
                return ApiResults.Error(StatusCodes.Status403Forbidden, "license_feature", licensing.Denial(LicenseFeatures.AlarmNotifications));
            }

            var error = Validate(body);
            if (error is not null)
            {
                return ApiResults.Error(StatusCodes.Status400BadRequest, "invalid_channel", error);
            }

            var id = string.IsNullOrWhiteSpace(body!.Id) ? Guid.NewGuid().ToString("N") : body.Id.Trim();
            var existing = database.FindNotificationChannel(id);
            var url = body.WebhookUrl?.Trim() ?? "";
            if (existing is not null && (string.IsNullOrWhiteSpace(url) || ChannelPayloads.LooksRedacted(url)))
            {
                url = existing.WebhookUrl;
            }

            var secret = body.ClearSecret ? "" : string.IsNullOrEmpty(body.Secret) ? existing?.Secret ?? "" : body.Secret;
            var row = new NotificationChannelRow
            {
                Id = id,
                Name = body.Name!.Trim(),
                Kind = body.Kind!.Trim().ToLowerInvariant(),
                Enabled = body.Enabled,
                WebhookUrl = url,
                Secret = secret,
                SecretFromEnv = (body.SecretFromEnv ?? "").Trim(),
                SmtpHost = (body.SmtpHost ?? "").Trim(),
                SmtpPort = body.SmtpPort <= 0 ? 25 : Math.Clamp(body.SmtpPort, 1, 65535),
                SmtpUser = (body.SmtpUser ?? "").Trim(),
                MailFrom = (body.MailFrom ?? "").Trim(),
                MailTo = (body.MailTo ?? "").Trim(),
                SmtpSsl = body.SmtpSsl,
                UpdatedUnixMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
            };
            database.SaveNotificationChannel(row);
            ConfigAudit.Write(http, database, "notify.channel", row.Id, "保存通知通道 " + row.Name + " (" + row.Kind + ")");
            return ApiResults.Ok(ToView(database.FindNotificationChannel(id)!));
        }).RequireWriter();
        api.MapDelete("/notifications/channels/{id}", (string id, HttpContext http, GatewayPersistence database) =>
        {
            if (!database.DeleteNotificationChannel(id))
            {
                return ApiResults.Error(StatusCodes.Status404NotFound, "not_found", "通知通道不存在");
            }

            ConfigAudit.Write(http, database, "notify.channel.delete", id, "删除通知通道");
            return ApiResults.Ok(new { deleted = true });
        }).RequireWriter();
        api.MapPost("/notifications/channels/{id}/test", async (string id, HttpContext http, NotificationDispatcher dispatcher, GatewayPersistence database, LicenseService licensing, CancellationToken cancellationToken) =>
        {
            if (!licensing.Allows(LicenseFeatures.AlarmNotifications))
            {
                return ApiResults.Error(StatusCodes.Status403Forbidden, "license_feature", licensing.Denial(LicenseFeatures.AlarmNotifications));
            }

            try
            {
                var delivery = await dispatcher.SendTestAsync(id, cancellationToken);
                ConfigAudit.Write(http, database, "notify.test", id, "发送测试通知，结果 " + delivery.Status);
                return ApiResults.Ok(ToDelivery(delivery));
            }
            catch (InvalidOperationException ex)
            {
                return ApiResults.Error(StatusCodes.Status400BadRequest, "test_failed", ex.Message);
            }
        }).RequireWriter();

        api.MapGet("/notifications/rules", (GatewayPersistence database) =>
            ApiResults.Ok(new { rules = database.ListNotificationRules().Select(ToRule).ToList() }));
        api.MapPut("/notifications/rules", (RuleWrite? body, HttpContext http, GatewayPersistence database, LicenseService licensing) =>
        {
            if (!licensing.Allows(LicenseFeatures.AlarmNotifications))
            {
                return ApiResults.Error(StatusCodes.Status403Forbidden, "license_feature", licensing.Denial(LicenseFeatures.AlarmNotifications));
            }

            var error = Validate(body, database);
            if (error is not null)
            {
                return ApiResults.Error(StatusCodes.Status400BadRequest, "invalid_rule", error);
            }

            var id = string.IsNullOrWhiteSpace(body!.Id) ? Guid.NewGuid().ToString("N") : body.Id.Trim();
            var row = new NotificationRuleRow
            {
                Id = id,
                Name = body.Name!.Trim(),
                Enabled = body.Enabled,
                ChannelId = body.ChannelId!.Trim(),
                EscalationChannelId = (body.EscalationChannelId ?? "").Trim(),
                EscalationMinutes = Math.Clamp(body.EscalationMinutes, 0, 24 * 60),
                DeviceIdsJson = NotificationRules.WriteList(body.DeviceIds),
                GroupsJson = NotificationRules.WriteList(body.Groups),
                SeveritiesJson = NotificationRules.WriteList(body.Severities),
                CodeFilter = (body.CodeFilter ?? "").Trim(),
                OnRaise = body.OnRaise,
                OnClear = body.OnClear,
                QuietStart = (body.QuietStart ?? "").Trim(),
                QuietEnd = (body.QuietEnd ?? "").Trim(),
                DedupSeconds = Math.Clamp(body.DedupSeconds, 0, 86_400),
                RatePerHour = Math.Clamp(body.RatePerHour, 0, 10_000),
                UpdatedUnixMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
            };
            database.SaveNotificationRule(row);
            ConfigAudit.Write(http, database, "notify.rule", row.Id, "保存通知规则 " + row.Name);
            return ApiResults.Ok(ToRule(row));
        }).RequireWriter();
        api.MapDelete("/notifications/rules/{id}", (string id, HttpContext http, GatewayPersistence database) =>
        {
            if (!database.DeleteNotificationRule(id))
            {
                return ApiResults.Error(StatusCodes.Status404NotFound, "not_found", "通知规则不存在");
            }

            ConfigAudit.Write(http, database, "notify.rule.delete", id, "删除通知规则");
            return ApiResults.Ok(new { deleted = true });
        }).RequireWriter();

        api.MapGet("/notifications/deliveries", (int? limit, GatewayPersistence database) =>
            ApiResults.Ok(new { deliveries = database.ListDeliveries(limit ?? 100).Select(ToDelivery).ToList() }));
        api.MapPost("/notifications/deliveries/{id}/retry", async (long id, HttpContext http, NotificationDispatcher dispatcher, GatewayPersistence database, CancellationToken cancellationToken) =>
        {
            var delivery = await dispatcher.RetryAsync(id, cancellationToken);
            if (delivery is null)
            {
                return ApiResults.Error(StatusCodes.Status404NotFound, "not_found", "通知记录不存在");
            }

            ConfigAudit.Write(http, database, "notify.retry", id.ToString(CultureInfo.InvariantCulture), "重试通知，结果 " + delivery.Status);
            return ApiResults.Ok(ToDelivery(delivery));
        }).RequireWriter();

        api.MapGet("/notifications/reports", (GatewayPersistence database) => ApiResults.Ok(ToSchedule(database.GetReportSchedule())));
        api.MapPut("/notifications/reports", (ScheduleWrite? body, HttpContext http, GatewayPersistence database, LicenseService licensing) =>
        {
            if (body?.Enabled == true && !licensing.Allows(LicenseFeatures.ScheduledReports))
            {
                return ApiResults.Error(StatusCodes.Status403Forbidden, "license_feature", licensing.Denial(LicenseFeatures.ScheduledReports));
            }

            if (body is null)
            {
                return ApiResults.Error(StatusCodes.Status400BadRequest, "invalid_report", "缺少报表设置");
            }

            if (!string.IsNullOrWhiteSpace(body.DailyTime) && !ShiftCalendar.TryClock(body.DailyTime, out _))
            {
                return ApiResults.Error(StatusCodes.Status400BadRequest, "invalid_report", "日报时间需为 HH:mm");
            }

            var current = database.GetReportSchedule();
            current.Enabled = body.Enabled;
            current.DailyEnabled = body.DailyEnabled;
            current.DailyTime = string.IsNullOrWhiteSpace(body.DailyTime) ? "08:00" : body.DailyTime.Trim();
            current.ShiftEnabled = body.ShiftEnabled;
            current.ChannelIdsJson = NotificationRules.WriteList(body.ChannelIds);
            current.UpdatedUnixMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            database.SaveReportSchedule(current);
            ConfigAudit.Write(http, database, "notify.report", "default", "保存稼动报表计划");
            return ApiResults.Ok(ToSchedule(current));
        }).RequireWriter();
        api.MapGet("/notifications/reports/preview", (string? cadence, NotificationDispatcher dispatcher) =>
            ApiResults.Ok(dispatcher.Preview(cadence ?? "daily")));

        api.MapGet("/ops/mqtt-spool", (IEnumerable<IMqttBufferStatus> buffers, GatewayPersistence database) =>
        {
            var buffer = buffers.FirstOrDefault();
            var options = new ReliabilityOptions();
            options.OverlayJson(database.GetSetting("reliability"));
            return ApiResults.Ok(new
            {
                connected = buffer?.Connected ?? false,
                depth = buffer?.Depth ?? 0,
                dropped = buffer?.Dropped ?? 0,
                maxMessages = options.SpoolMessageCap,
                maxAgeHours = options.SpoolMaxAgeHours,
                maxMegabytes = options.SpoolMaxMegabytes
            });
        });
        api.MapGet("/ops/reliability", (GatewayPersistence database) =>
        {
            var options = new ReliabilityOptions();
            options.OverlayJson(database.GetSetting("reliability"));
            options.SpoolDirectory = "";
            return ApiResults.Ok(options);
        });
        api.MapPut("/ops/reliability", (ReliabilityOptions? body, HttpContext http, GatewayPersistence database, IServiceProvider services) =>
        {
            if (body is null)
            {
                return ApiResults.Error(StatusCodes.Status400BadRequest, "invalid_reliability", "缺少可靠性设置");
            }

            body.Normalize();
            var live = services.GetService<ReliabilityOptions>();
            if (live is not null)
            {
                var directory = live.SpoolDirectory;
                live.ReconnectInitialSeconds = body.ReconnectInitialSeconds;
                live.ReconnectMultiplier = body.ReconnectMultiplier;
                live.ReconnectCapSeconds = body.ReconnectCapSeconds;
                live.ReconnectJitter = body.ReconnectJitter;
                live.StallSeconds = body.StallSeconds;
                live.SpoolMaxMessages = body.SpoolMaxMessages;
                live.SpoolMaxAgeHours = body.SpoolMaxAgeHours;
                live.SpoolMaxMegabytes = body.SpoolMaxMegabytes;
                live.SpoolDirectory = directory;
                live.Normalize();
                database.SetSetting("reliability", live.ToJson());
            }
            else
            {
                body.SpoolDirectory = "";
                database.SetSetting("reliability", body.ToJson());
            }

            ConfigAudit.Write(http, database, "reliability.update", "reliability", "更新重连与 MQTT 缓冲设置");
            body.SpoolDirectory = "";
            return ApiResults.Ok(body);
        }).RequireWriter();
    }

    private static string? Validate(ChannelWrite? body)
    {
        if (body is null || string.IsNullOrWhiteSpace(body.Name))
        {
            return "请填写通道名称";
        }

        if (body.Name.Trim().Length > 80)
        {
            return "通道名称不超过 80 个字符";
        }

        if (string.IsNullOrWhiteSpace(body.Kind) || !Kinds.Contains(body.Kind.Trim()))
        {
            return "通道类型必须是企业微信、钉钉、飞书、邮件或 Webhook";
        }

        var kind = body.Kind.Trim().ToLowerInvariant();
        if (kind == "smtp")
        {
            if (string.IsNullOrWhiteSpace(body.SmtpHost) || string.IsNullOrWhiteSpace(body.MailFrom) || string.IsNullOrWhiteSpace(body.MailTo))
            {
                return "邮件通道需要 SMTP 主机、发件人和收件人";
            }
        }
        else if (string.IsNullOrWhiteSpace(body.WebhookUrl) || !body.WebhookUrl.Contains("://", StringComparison.Ordinal))
        {
            if (string.IsNullOrWhiteSpace(body.Id))
            {
                return "请填写 Webhook 地址";
            }
        }

        if (!string.IsNullOrWhiteSpace(body.SecretFromEnv)
            && body.SecretFromEnv.Any(ch => char.IsWhiteSpace(ch) || ch is '=' or '\n'))
        {
            return "密钥环境变量名不能包含空格";
        }

        return null;
    }

    private static string? Validate(RuleWrite? body, GatewayPersistence database)
    {
        if (body is null || string.IsNullOrWhiteSpace(body.Name))
        {
            return "请填写规则名称";
        }

        if (string.IsNullOrWhiteSpace(body.ChannelId) || database.FindNotificationChannel(body.ChannelId.Trim()) is null)
        {
            return "请选择已有的通知通道";
        }

        if (body.EscalationMinutes > 0)
        {
            if (string.IsNullOrWhiteSpace(body.EscalationChannelId)
                || database.FindNotificationChannel(body.EscalationChannelId.Trim()) is null)
            {
                return "升级需要另一个已有的通知通道";
            }
        }

        if ((!string.IsNullOrWhiteSpace(body.QuietStart) || !string.IsNullOrWhiteSpace(body.QuietEnd))
            && (!ShiftCalendar.TryClock(body.QuietStart, out _) || !ShiftCalendar.TryClock(body.QuietEnd, out _)))
        {
            return "免打扰时间需为 HH:mm";
        }

        if (!body.OnRaise && !body.OnClear)
        {
            return "至少选择发生或恢复其中一种";
        }

        return null;
    }

    public static ChannelView ToView(NotificationChannelRow row) => new()
    {
        Id = row.Id,
        Name = row.Name,
        Kind = row.Kind,
        Enabled = row.Enabled,
        WebhookUrl = ChannelPayloads.RedactUrl(row.WebhookUrl),
        HasSecret = !string.IsNullOrEmpty(row.Secret) || !string.IsNullOrEmpty(row.SecretFromEnv),
        SecretFromEnv = row.SecretFromEnv,
        SmtpHost = row.SmtpHost,
        SmtpPort = row.SmtpPort,
        SmtpUser = row.SmtpUser,
        MailFrom = row.MailFrom,
        MailTo = row.MailTo,
        SmtpSsl = row.SmtpSsl
    };

    private static RuleView ToRule(NotificationRuleRow row) => new()
    {
        Id = row.Id,
        Name = row.Name,
        Enabled = row.Enabled,
        ChannelId = row.ChannelId,
        EscalationChannelId = row.EscalationChannelId,
        EscalationMinutes = row.EscalationMinutes,
        DeviceIds = NotificationRules.ReadList(row.DeviceIdsJson),
        Groups = NotificationRules.ReadList(row.GroupsJson),
        Severities = NotificationRules.ReadList(row.SeveritiesJson),
        CodeFilter = row.CodeFilter,
        OnRaise = row.OnRaise,
        OnClear = row.OnClear,
        QuietStart = row.QuietStart,
        QuietEnd = row.QuietEnd,
        DedupSeconds = row.DedupSeconds,
        RatePerHour = row.RatePerHour
    };

    private static object ToDelivery(NotificationDeliveryRow row) => new
    {
        id = row.Id,
        channelId = row.ChannelId,
        ruleId = row.RuleId,
        alarmId = row.AlarmId,
        kind = row.Kind,
        deviceId = row.DeviceId,
        code = row.Code,
        severity = row.Severity,
        summary = row.Summary,
        status = row.Status,
        attempts = row.Attempts,
        lastError = row.LastError,
        createdUnixMs = row.CreatedUnixMs,
        sentUnixMs = row.SentUnixMs
    };

    private static object ToSchedule(ReportScheduleRow row) => new
    {
        enabled = row.Enabled,
        dailyEnabled = row.DailyEnabled,
        dailyTime = row.DailyTime,
        shiftEnabled = row.ShiftEnabled,
        channelIds = NotificationRules.ReadList(row.ChannelIdsJson),
        lastDailyKey = row.LastDailyKey,
        lastShiftKey = row.LastShiftKey
    };

    private static RouteHandlerBuilder RequireWriter(this RouteHandlerBuilder builder) =>
        builder.AddEndpointFilter(async (context, next) =>
        {
            var role = context.HttpContext.Items["studio.role"] as string;
            if (role is not ("admin" or "engineer"))
            {
                return ApiResults.Error(StatusCodes.Status403Forbidden, "forbidden", "当前角色无权修改通知配置");
            }

            return await next(context);
        });
}

public sealed class ChannelWrite
{
    public string? Id { get; set; }

    public string? Name { get; set; }

    public string? Kind { get; set; }

    public bool Enabled { get; set; } = true;

    public string? WebhookUrl { get; set; }

    public string? Secret { get; set; }

    public bool ClearSecret { get; set; }

    public string? SecretFromEnv { get; set; }

    public string? SmtpHost { get; set; }

    public int SmtpPort { get; set; } = 25;

    public string? SmtpUser { get; set; }

    public string? MailFrom { get; set; }

    public string? MailTo { get; set; }

    public bool SmtpSsl { get; set; } = true;
}

public sealed class ChannelView
{
    public string Id { get; set; } = "";

    public string Name { get; set; } = "";

    public string Kind { get; set; } = "";

    public bool Enabled { get; set; }

    public string WebhookUrl { get; set; } = "";

    public bool HasSecret { get; set; }

    public string SecretFromEnv { get; set; } = "";

    public string SmtpHost { get; set; } = "";

    public int SmtpPort { get; set; }

    public string SmtpUser { get; set; } = "";

    public string MailFrom { get; set; } = "";

    public string MailTo { get; set; } = "";

    public bool SmtpSsl { get; set; }
}

public sealed class RuleWrite
{
    public string? Id { get; set; }

    public string? Name { get; set; }

    public bool Enabled { get; set; } = true;

    public string? ChannelId { get; set; }

    public string? EscalationChannelId { get; set; }

    public int EscalationMinutes { get; set; }

    public List<string>? DeviceIds { get; set; }

    public List<string>? Groups { get; set; }

    public List<string>? Severities { get; set; }

    public string? CodeFilter { get; set; }

    public bool OnRaise { get; set; } = true;

    public bool OnClear { get; set; }

    public string? QuietStart { get; set; }

    public string? QuietEnd { get; set; }

    public int DedupSeconds { get; set; } = 300;

    public int RatePerHour { get; set; } = 30;
}

public sealed class RuleView
{
    public string Id { get; set; } = "";

    public string Name { get; set; } = "";

    public bool Enabled { get; set; }

    public string ChannelId { get; set; } = "";

    public string EscalationChannelId { get; set; } = "";

    public int EscalationMinutes { get; set; }

    public List<string> DeviceIds { get; set; } = [];

    public List<string> Groups { get; set; } = [];

    public List<string> Severities { get; set; } = [];

    public string CodeFilter { get; set; } = "";

    public bool OnRaise { get; set; }

    public bool OnClear { get; set; }

    public string QuietStart { get; set; } = "";

    public string QuietEnd { get; set; } = "";

    public int DedupSeconds { get; set; }

    public int RatePerHour { get; set; }
}

public sealed class ScheduleWrite
{
    public bool Enabled { get; set; }

    public bool DailyEnabled { get; set; } = true;

    public string? DailyTime { get; set; }

    public bool ShiftEnabled { get; set; }

    public List<string>? ChannelIds { get; set; }
}
