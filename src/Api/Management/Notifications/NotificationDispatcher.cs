using System.Globalization;
using System.Net;
using System.Net.Mail;
using System.Text;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using IotDaq.Persistence;
using IotDaq.Persistence.Visualization;
using Studio.Contracts;
using Studio.Host.Config;
using Studio.Host.Visualization;

namespace Studio.Host.Notifications;

public sealed class NotificationDispatcher
{
    public const string EpochKey = "notify.epochUnixMs";
    public const string RaiseCursorKey = "notify.raiseCursor";
    public const string ClearCursorKey = "notify.clearCursor";

    private readonly GatewayPersistence _database;
    private readonly ConfigStore _store;
    private readonly VisualizationService _visualization;
    private readonly ILogger<NotificationDispatcher> _logger;
    private readonly HttpClient _http;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public NotificationDispatcher(
        GatewayPersistence database,
        ConfigStore store,
        VisualizationService visualization,
        ILogger<NotificationDispatcher> logger)
    {
        _database = database;
        _store = store;
        _visualization = visualization;
        _logger = logger;
        _http = new HttpClient { Timeout = TimeSpan.FromSeconds(8) };
    }

    public async Task TickAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            EnsureCursors();
            ScanRaises();
            ScanClears();
            ScanEscalations();
            ScanReports();
            await FlushPendingAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<NotificationDeliveryRow> SendTestAsync(string channelId, CancellationToken cancellationToken)
    {
        var channel = _database.FindNotificationChannel(channelId)
            ?? throw new InvalidOperationException("通知通道不存在");
        var delivery = new NotificationDeliveryRow
        {
            ChannelId = channel.Id,
            Kind = "test",
            Summary = "【采集网关】这是一条测试通知。如果通道配置正确，你会在对应的群或邮箱里看到它。",
            Status = "pending",
            CreatedUnixMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            NextAttemptUnixMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
        };
        delivery.Id = _database.AddDelivery(delivery);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await SendOneAsync(delivery, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }

        return _database.FindDelivery(delivery.Id) ?? delivery;
    }

    public async Task<NotificationDeliveryRow?> RetryAsync(long id, CancellationToken cancellationToken)
    {
        var delivery = _database.FindDelivery(id);
        if (delivery is null)
        {
            return null;
        }

        delivery.Status = "pending";
        delivery.NextAttemptUnixMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        _database.UpdateDelivery(delivery.Id, "pending", delivery.LastError, delivery.Attempts, delivery.SentUnixMs, delivery.NextAttemptUnixMs);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await SendOneAsync(delivery, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }

        return _database.FindDelivery(id);
    }

    public ReportPreview Preview(string cadence)
    {
        var calendar = ShiftCalendar.ParseOrDefault(_database.GetSetting("shiftCalendar"));
        var zone = calendar.ResolveZone();
        var now = DateTimeOffset.UtcNow;
        DateTimeOffset from;
        DateTimeOffset to;
        string title;
        if (string.Equals(cadence, "shift", StringComparison.OrdinalIgnoreCase))
        {
            var due = ReportScheduleMath.DueShift(new ReportScheduleRow { Enabled = true, ShiftEnabled = true }, calendar, now);
            if (due is null)
            {
                var windows = calendar.Windows(now.AddHours(-18), now.AddMinutes(1));
                var current = windows.LastOrDefault(window => window.Start <= now && window.End > now);
                if (current.Name is null && windows.Count > 0)
                {
                    current = windows[^1];
                }

                from = current.Start == default ? now.AddHours(-8) : current.Start;
                to = current.End == default || current.End > now ? now : current.End;
                title = "当前班次预览" + (string.IsNullOrEmpty(current.Name) ? "" : " · " + current.Name);
            }
            else
            {
                from = due.Value.From;
                to = due.Value.To;
                title = "班次报表 · " + due.Value.Key;
            }
        }
        else
        {
            (from, to) = ReportScheduleMath.PreviousLocalDay(now, zone);
            title = "日报 · " + DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(from, zone).DateTime).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        }

        return BuildReport(title, from, to);
    }

    private void EnsureCursors()
    {
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString(CultureInfo.InvariantCulture);
        if (string.IsNullOrWhiteSpace(_database.GetSetting(EpochKey)))
        {
            _database.SetSetting(EpochKey, now);
            _database.SetSetting(RaiseCursorKey, now);
            _database.SetSetting(ClearCursorKey, now);
        }
    }

    private void ScanRaises()
    {
        var cursor = ReadCursor(RaiseCursorKey);
        var alarms = _database.AlarmsRaisedAfter(cursor, 100);
        if (alarms.Count == 0)
        {
            return;
        }

        var rules = _database.ListNotificationRules();
        var devices = DeviceMap();
        foreach (var alarm in alarms)
        {
            var notice = ToNotice(alarm, "raise", devices);
            QueueMatches(rules, notice, alarm.RaisedUnixMs);
        }

        _database.SetSetting(RaiseCursorKey, alarms.Max(row => row.RaisedUnixMs).ToString(CultureInfo.InvariantCulture));
    }

    private void ScanClears()
    {
        var cursor = ReadCursor(ClearCursorKey);
        var alarms = _database.AlarmsClearedAfter(cursor, 100);
        if (alarms.Count == 0)
        {
            return;
        }

        var rules = _database.ListNotificationRules();
        var devices = DeviceMap();
        foreach (var alarm in alarms)
        {
            var notice = ToNotice(alarm, "clear", devices);
            QueueMatches(rules, notice, alarm.ClearedUnixMs ?? alarm.RaisedUnixMs);
        }

        var max = alarms.Max(row => row.ClearedUnixMs ?? 0);
        _database.SetSetting(ClearCursorKey, max.ToString(CultureInfo.InvariantCulture));
    }

    private void ScanEscalations()
    {
        var rules = _database.ListNotificationRules().Where(rule => rule.Enabled && rule.EscalationMinutes > 0).ToList();
        if (rules.Count == 0)
        {
            return;
        }

        var epoch = ReadCursor(EpochKey);
        var now = DateTimeOffset.UtcNow;
        var zone = Zone();
        var devices = DeviceMap();
        var alarms = _database.ActiveUnacknowledgedBefore(now.ToUnixTimeMilliseconds(), 200)
            .Where(alarm => alarm.RaisedUnixMs >= epoch)
            .ToList();
        foreach (var alarm in alarms)
        {
            var notice = ToNotice(alarm, "raise", devices);
            foreach (var rule in rules)
            {
                if (!NotificationRules.Matches(rule, notice))
                {
                    continue;
                }

                var quiet = NotificationRules.InQuietHours(rule.QuietStart, rule.QuietEnd, now, zone);
                if (!NotificationRules.ShouldEscalate(rule, notice, now, _database.HasEscalation(alarm.Id), quiet))
                {
                    continue;
                }

                var escalated = new AlarmNotice
                {
                    AlarmId = notice.AlarmId,
                    DeviceId = notice.DeviceId,
                    Workshop = notice.Workshop,
                    Line = notice.Line,
                    Severity = notice.Severity,
                    Code = notice.Code,
                    Message = notice.Message,
                    Kind = "escalation",
                    Active = true,
                    Acknowledged = false,
                    Raised = notice.Raised
                };
                Enqueue(rule.EscalationChannelId, rule.Id, escalated, "未确认，升级通知");
            }
        }
    }

    private void ScanReports()
    {
        var schedule = _database.GetReportSchedule();
        if (!schedule.Enabled)
        {
            return;
        }

        var calendar = ShiftCalendar.ParseOrDefault(_database.GetSetting("shiftCalendar"));
        var zone = calendar.ResolveZone();
        var now = DateTimeOffset.UtcNow;
        var changed = false;
        var dailyKey = ReportScheduleMath.DueDaily(schedule, now, zone);
        if (dailyKey is not null)
        {
            var (from, to) = ReportScheduleMath.PreviousLocalDay(now, zone);
            var report = BuildReport("日报 · " + dailyKey, from, to);
            EnqueueReport(schedule, report);
            schedule.LastDailyKey = dailyKey;
            changed = true;
        }

        var shift = ReportScheduleMath.DueShift(schedule, calendar, now);
        if (shift is not null)
        {
            var report = BuildReport("班次报表 · " + shift.Value.Key, shift.Value.From, shift.Value.To);
            EnqueueReport(schedule, report);
            schedule.LastShiftKey = shift.Value.Key;
            changed = true;
        }

        if (changed)
        {
            schedule.UpdatedUnixMs = now.ToUnixTimeMilliseconds();
            _database.SaveReportSchedule(schedule);
        }
    }

    private void EnqueueReport(ReportScheduleRow schedule, ReportPreview report)
    {
        foreach (var channelId in NotificationRules.ReadList(schedule.ChannelIdsJson))
        {
            var row = new NotificationDeliveryRow
            {
                ChannelId = channelId,
                Kind = "report",
                Summary = report.Text,
                Status = "pending",
                CreatedUnixMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                NextAttemptUnixMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
            };
            row.Id = _database.AddDelivery(row);
        }
    }

    private void QueueMatches(IReadOnlyList<NotificationRuleRow> rules, AlarmNotice notice, long eventUnixMs)
    {
        var now = DateTimeOffset.FromUnixTimeMilliseconds(eventUnixMs);
        var zone = Zone();
        foreach (var rule in rules)
        {
            if (!NotificationRules.Matches(rule, notice))
            {
                continue;
            }

            if (NotificationRules.InQuietHours(rule.QuietStart, rule.QuietEnd, now, zone))
            {
                continue;
            }

            var dedupSince = eventUnixMs - Math.Max(0, rule.DedupSeconds) * 1000L;
            var recent = _database.CountDeliveriesSince(rule.Id, notice.DeviceId, notice.Code, notice.Kind, dedupSince);
            var hour = _database.CountRuleDeliveriesSince(rule.Id, eventUnixMs - 3_600_000);
            if (!NotificationRules.AllowSend(recent, rule.DedupSeconds, hour, rule.RatePerHour))
            {
                continue;
            }

            Enqueue(rule.ChannelId, rule.Id, notice, null);
        }
    }

    private void Enqueue(string channelId, string ruleId, AlarmNotice notice, string? extra)
    {
        if (string.IsNullOrWhiteSpace(channelId))
        {
            return;
        }

        var text = ChannelPayloads.FormatAlarm(notice, SiteName());
        if (!string.IsNullOrWhiteSpace(extra))
        {
            text += "\n" + extra;
        }

        var row = new NotificationDeliveryRow
        {
            ChannelId = channelId,
            RuleId = ruleId,
            AlarmId = notice.AlarmId,
            Kind = notice.Kind,
            DeviceId = notice.DeviceId,
            Code = notice.Code,
            Severity = notice.Severity,
            Summary = text,
            Status = "pending",
            CreatedUnixMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            NextAttemptUnixMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
        };
        row.Id = _database.AddDelivery(row);
    }

    private async Task FlushPendingAsync(CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        foreach (var delivery in _database.DueDeliveries(now, 20))
        {
            cancellationToken.ThrowIfCancellationRequested();
            await SendOneAsync(delivery, cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task SendOneAsync(NotificationDeliveryRow delivery, CancellationToken cancellationToken)
    {
        var channel = _database.FindNotificationChannel(delivery.ChannelId);
        var attempts = delivery.Attempts + 1;
        if (channel is null || !channel.Enabled)
        {
            _database.UpdateDelivery(delivery.Id, "failed", "通道不存在或已停用", attempts, null, delivery.NextAttemptUnixMs);
            return;
        }

        var secret = ChannelPayloads.ResolveSecret(channel);
        var message = new OutboundMessage
        {
            Title = delivery.Kind == "report" ? "稼动报表" : "采集网关报警",
            Text = delivery.Summary,
            Kind = KindLabel(delivery.Kind),
            DeviceId = delivery.DeviceId,
            Severity = delivery.Severity,
            Code = delivery.Code,
            UnixMs = delivery.CreatedUnixMs
        };
        try
        {
            if (string.Equals(channel.Kind, "smtp", StringComparison.OrdinalIgnoreCase))
            {
                await SendSmtpAsync(channel, secret, message, cancellationToken).ConfigureAwait(false);
            }
            else
            {
                var prepared = ChannelPayloads.Prepare(channel, secret, message, DateTimeOffset.UtcNow)
                    ?? throw new InvalidOperationException("不支持的通道类型");
                await PostAsync(prepared, secret, cancellationToken).ConfigureAwait(false);
            }

            _database.UpdateDelivery(delivery.Id, "sent", "", attempts, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), 0);
            _logger.LogInformation("Notification {Id} sent via {Kind}", delivery.Id, channel.Kind);
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            var error = Scrub(ex.Message, secret);
            var status = attempts >= 4 ? "failed" : "pending";
            var next = status == "failed"
                ? delivery.NextAttemptUnixMs
                : DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() + DelayMs(attempts);
            _database.UpdateDelivery(delivery.Id, status, error, attempts, null, next);
            _logger.LogWarning("Notification {Id} {Status}: {Error}", delivery.Id, status, error);
        }
    }

    private async Task PostAsync(PreparedRequest prepared, string secret, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, prepared.Url)
        {
            Content = new StringContent(prepared.Body, Encoding.UTF8, "application/json")
        };
        foreach (var header in prepared.Headers)
        {
            request.Headers.TryAddWithoutValidation(header.Key, header.Value);
        }

        using var response = await _http.SendAsync(request, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            if (body.Length > 180)
            {
                body = body[..180];
            }

            throw new InvalidOperationException("HTTP " + ((int)response.StatusCode).ToString(CultureInfo.InvariantCulture) + " " + Scrub(body, secret));
        }
    }

    private static async Task SendSmtpAsync(NotificationChannelRow channel, string secret, OutboundMessage message, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(channel.SmtpHost))
        {
            throw new InvalidOperationException("未填写 SMTP 主机");
        }

        if (string.IsNullOrWhiteSpace(channel.MailFrom) || string.IsNullOrWhiteSpace(channel.MailTo))
        {
            throw new InvalidOperationException("未填写发件人或收件人");
        }

        var port = channel.SmtpPort <= 0 ? 25 : channel.SmtpPort;
#pragma warning disable SYSLIB0014
        using var client = new SmtpClient(channel.SmtpHost, port)
        {
            EnableSsl = channel.SmtpSsl,
            Timeout = 8000,
            DeliveryMethod = SmtpDeliveryMethod.Network
        };
#pragma warning restore SYSLIB0014
        if (!string.IsNullOrWhiteSpace(channel.SmtpUser))
        {
            client.Credentials = new NetworkCredential(channel.SmtpUser, secret);
        }

        using var mail = new MailMessage
        {
            From = new MailAddress(channel.MailFrom),
            Subject = message.Title,
            Body = message.Text
        };
        foreach (var address in channel.MailTo.Split([',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            mail.To.Add(address);
        }

        await client.SendMailAsync(mail, cancellationToken).ConfigureAwait(false);
    }

    public ReportPreview BuildReport(string title, DateTimeOffset from, DateTimeOffset to)
    {
        var utilization = _visualization.Utilization(from.ToUnixTimeMilliseconds(), to.ToUnixTimeMilliseconds(), null);
        var alarms = _database.QueryAlarms(null, null, null, null, from.ToUnixTimeMilliseconds(), to.ToUnixTimeMilliseconds(), 2000);
        var lines = utilization.Shifts
            .GroupBy(row => row.DeviceId, StringComparer.OrdinalIgnoreCase)
            .Select(group =>
            {
                var planned = group.Sum(row => row.PlannedMs);
                var run = group.Sum(row => row.RunMs);
                var first = group.First();
                var top = alarms
                    .Where(alarm => string.Equals(alarm.DeviceId, group.Key, StringComparison.OrdinalIgnoreCase))
                    .GroupBy(alarm => string.IsNullOrWhiteSpace(alarm.Code) ? alarm.Message : alarm.Code)
                    .OrderByDescending(bucket => bucket.Count())
                    .Take(3)
                    .Select(bucket => bucket.Key + "×" + bucket.Count().ToString(CultureInfo.InvariantCulture))
                    .ToList();
                return new ReportLine
                {
                    DeviceId = group.Key,
                    DisplayName = first.DisplayName,
                    Workshop = first.Workshop,
                    Line = first.Line,
                    Utilization = planned <= 0 ? 0 : Math.Round((double)run / planned, 4),
                    PartCount = group.Sum(row => row.PartCount),
                    TopAlarms = top
                };
            })
            .OrderBy(line => line.Workshop, StringComparer.Ordinal)
            .ThenBy(line => line.Line, StringComparer.Ordinal)
            .ThenBy(line => line.DeviceId, StringComparer.Ordinal)
            .Take(40)
            .ToList();
        var builder = new StringBuilder();
        builder.Append("【采集网关】").AppendLine(title);
        builder.Append("区间：").Append(from.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture))
            .Append(" – ").AppendLine(to.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture));
        if (lines.Count == 0)
        {
            builder.AppendLine("这个区间没有设备稼动数据。");
        }

        foreach (var line in lines)
        {
            builder.Append(line.DisplayName).Append(" (").Append(line.DeviceId).Append(") ")
                .Append(line.Workshop).Append('/').Append(line.Line)
                .Append(" 稼动 ").Append((line.Utilization * 100).ToString("0.#", CultureInfo.InvariantCulture)).Append('%')
                .Append(" 件数 ").Append(line.PartCount.ToString("0.###", CultureInfo.InvariantCulture));
            if (line.TopAlarms.Count > 0)
            {
                builder.Append(" 报警 ").Append(string.Join("，", line.TopAlarms));
            }

            builder.AppendLine();
        }

        var text = builder.ToString().Trim();
        if (text.Length > 3500)
        {
            text = text[..3500] + "\n…";
        }

        return new ReportPreview
        {
            Title = title,
            FromUnixMs = from.ToUnixTimeMilliseconds(),
            ToUnixMs = to.ToUnixTimeMilliseconds(),
            Text = text,
            Lines = lines
        };
    }

    private Dictionary<string, DeviceDocument> DeviceMap()
    {
        try
        {
            return _store.ReadPublished().Devices
                .Where(device => !string.IsNullOrWhiteSpace(device.Metadata?.Id))
                .GroupBy(device => device.Metadata.Id, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogDebug(ex, "Published devices were not available for notification matching");
            return new Dictionary<string, DeviceDocument>(StringComparer.OrdinalIgnoreCase);
        }
    }

    private static AlarmNotice ToNotice(AlarmRow alarm, string kind, IReadOnlyDictionary<string, DeviceDocument> devices)
    {
        devices.TryGetValue(alarm.DeviceId, out var device);
        return new AlarmNotice
        {
            AlarmId = alarm.Id,
            DeviceId = alarm.DeviceId,
            Workshop = device?.Spec.Workshop ?? "",
            Line = device?.Spec.Line ?? "",
            Severity = alarm.Severity,
            Code = alarm.Code,
            Message = alarm.Message,
            Kind = kind,
            Active = alarm.Active,
            Acknowledged = alarm.Acknowledged,
            Raised = DateTimeOffset.FromUnixTimeMilliseconds(alarm.RaisedUnixMs)
        };
    }

    private string? SiteName()
    {
        try
        {
            return _store.ReadPublished().Gateway.Metadata.Name;
        }
        catch (Exception)
        {
            return null;
        }
    }

    private TimeZoneInfo Zone() => ShiftCalendar.ParseOrDefault(_database.GetSetting("shiftCalendar")).ResolveZone();

    private long ReadCursor(string key)
    {
        var text = _database.GetSetting(key);
        return long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) ? value : 0;
    }

    private static string KindLabel(string kind) => kind switch
    {
        "clear" => "alarm.cleared",
        "escalation" => "alarm.escalated",
        "report" => "report.summary",
        "test" => "notify.test",
        _ => "alarm.raised"
    };

    private static long DelayMs(int attempts) => attempts switch
    {
        1 => 30_000,
        2 => 120_000,
        _ => 600_000
    };

    private static string Scrub(string text, string secret)
    {
        var value = text ?? "";
        if (!string.IsNullOrEmpty(secret) && secret.Length >= 4)
        {
            value = value.Replace(secret, "***", StringComparison.Ordinal);
        }

        return value.Replace('\r', ' ').Replace('\n', ' ').Trim();
    }
}

public sealed class ReportPreview
{
    public string Title { get; set; } = "";

    public long FromUnixMs { get; set; }

    public long ToUnixMs { get; set; }

    public string Text { get; set; } = "";

    public List<ReportLine> Lines { get; set; } = [];
}

public sealed class ReportLine
{
    public string DeviceId { get; set; } = "";

    public string DisplayName { get; set; } = "";

    public string Workshop { get; set; } = "";

    public string Line { get; set; } = "";

    public double Utilization { get; set; }

    public double PartCount { get; set; }

    public List<string> TopAlarms { get; set; } = [];
}

public sealed class NotificationWorker(NotificationDispatcher dispatcher, ILogger<NotificationWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await dispatcher.TickAsync(stoppingToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning(ex, "Notification tick failed");
            }

            try
            {
                await Task.Delay(TimeSpan.FromSeconds(2), stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }
}
