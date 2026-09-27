using Microsoft.EntityFrameworkCore;

namespace IotDaq.Persistence;

public sealed partial class GatewayPersistence
{
    public IReadOnlyList<NotificationChannelRow> ListNotificationChannels()
    {
        EnsureReady();
        lock (_gate)
        {
            using var db = CreateContext();
            return db.NotificationChannels.AsNoTracking().OrderBy(row => row.Name).ToList();
        }
    }

    public NotificationChannelRow? FindNotificationChannel(string id)
    {
        EnsureReady();
        lock (_gate)
        {
            using var db = CreateContext();
            return db.NotificationChannels.AsNoTracking().FirstOrDefault(row => row.Id == id);
        }
    }

    public void SaveNotificationChannel(NotificationChannelRow row)
    {
        EnsureReady();
        lock (_gate)
        {
            using var db = CreateContext();
            var existing = db.NotificationChannels.FirstOrDefault(item => item.Id == row.Id);
            if (existing is null)
            {
                db.NotificationChannels.Add(row);
            }
            else
            {
                existing.Name = row.Name;
                existing.Kind = row.Kind;
                existing.Enabled = row.Enabled;
                existing.WebhookUrl = row.WebhookUrl;
                existing.Secret = row.Secret;
                existing.SecretFromEnv = row.SecretFromEnv;
                existing.SmtpHost = row.SmtpHost;
                existing.SmtpPort = row.SmtpPort;
                existing.SmtpUser = row.SmtpUser;
                existing.MailFrom = row.MailFrom;
                existing.MailTo = row.MailTo;
                existing.SmtpSsl = row.SmtpSsl;
                existing.UpdatedUnixMs = row.UpdatedUnixMs;
            }

            db.SaveChanges();
        }
    }

    public bool DeleteNotificationChannel(string id)
    {
        EnsureReady();
        lock (_gate)
        {
            using var db = CreateContext();
            var existing = db.NotificationChannels.FirstOrDefault(item => item.Id == id);
            if (existing is null)
            {
                return false;
            }

            db.NotificationChannels.Remove(existing);
            db.SaveChanges();
            return true;
        }
    }

    public IReadOnlyList<NotificationRuleRow> ListNotificationRules()
    {
        EnsureReady();
        lock (_gate)
        {
            using var db = CreateContext();
            return db.NotificationRules.AsNoTracking().OrderBy(row => row.Name).ToList();
        }
    }

    public NotificationRuleRow? FindNotificationRule(string id)
    {
        EnsureReady();
        lock (_gate)
        {
            using var db = CreateContext();
            return db.NotificationRules.AsNoTracking().FirstOrDefault(row => row.Id == id);
        }
    }

    public void SaveNotificationRule(NotificationRuleRow row)
    {
        EnsureReady();
        lock (_gate)
        {
            using var db = CreateContext();
            var existing = db.NotificationRules.FirstOrDefault(item => item.Id == row.Id);
            if (existing is null)
            {
                db.NotificationRules.Add(row);
            }
            else
            {
                db.Entry(existing).CurrentValues.SetValues(row);
            }

            db.SaveChanges();
        }
    }

    public bool DeleteNotificationRule(string id)
    {
        EnsureReady();
        lock (_gate)
        {
            using var db = CreateContext();
            var existing = db.NotificationRules.FirstOrDefault(item => item.Id == id);
            if (existing is null)
            {
                return false;
            }

            db.NotificationRules.Remove(existing);
            db.SaveChanges();
            return true;
        }
    }

    public ReportScheduleRow GetReportSchedule()
    {
        EnsureReady();
        lock (_gate)
        {
            using var db = CreateContext();
            return db.ReportSchedules.AsNoTracking().FirstOrDefault(row => row.Id == "default")
                ?? new ReportScheduleRow { Id = "default", DailyTime = "08:00" };
        }
    }

    public void SaveReportSchedule(ReportScheduleRow row)
    {
        EnsureReady();
        row.Id = "default";
        lock (_gate)
        {
            using var db = CreateContext();
            var existing = db.ReportSchedules.FirstOrDefault(item => item.Id == "default");
            if (existing is null)
            {
                db.ReportSchedules.Add(row);
            }
            else
            {
                existing.Enabled = row.Enabled;
                existing.DailyEnabled = row.DailyEnabled;
                existing.DailyTime = row.DailyTime;
                existing.ShiftEnabled = row.ShiftEnabled;
                existing.ChannelIdsJson = row.ChannelIdsJson;
                existing.LastDailyKey = row.LastDailyKey;
                existing.LastShiftKey = row.LastShiftKey;
                existing.UpdatedUnixMs = row.UpdatedUnixMs;
            }

            db.SaveChanges();
        }
    }

    public long AddDelivery(NotificationDeliveryRow row)
    {
        EnsureReady();
        lock (_gate)
        {
            using var db = CreateContext();
            db.NotificationDeliveries.Add(row);
            db.SaveChanges();
            var stale = db.NotificationDeliveries.OrderByDescending(item => item.Id).Skip(2000).Select(item => item.Id).FirstOrDefault();
            if (stale > 0)
            {
                db.NotificationDeliveries.Where(item => item.Id <= stale).ExecuteDelete();
            }

            return row.Id;
        }
    }

    public IReadOnlyList<NotificationDeliveryRow> ListDeliveries(int limit)
    {
        EnsureReady();
        limit = Math.Clamp(limit, 1, 500);
        lock (_gate)
        {
            using var db = CreateContext();
            return db.NotificationDeliveries.AsNoTracking()
                .OrderByDescending(row => row.Id)
                .Take(limit)
                .ToList();
        }
    }

    public NotificationDeliveryRow? FindDelivery(long id)
    {
        EnsureReady();
        lock (_gate)
        {
            using var db = CreateContext();
            return db.NotificationDeliveries.AsNoTracking().FirstOrDefault(row => row.Id == id);
        }
    }

    public void UpdateDelivery(long id, string status, string error, int attempts, long? sentUnixMs, long nextAttemptUnixMs)
    {
        EnsureReady();
        lock (_gate)
        {
            using var db = CreateContext();
            var row = db.NotificationDeliveries.FirstOrDefault(item => item.Id == id);
            if (row is null)
            {
                return;
            }

            row.Status = status;
            row.LastError = error.Length > 400 ? error[..400] : error;
            row.Attempts = attempts;
            row.SentUnixMs = sentUnixMs;
            row.NextAttemptUnixMs = nextAttemptUnixMs;
            db.SaveChanges();
        }
    }

    public IReadOnlyList<NotificationDeliveryRow> DueDeliveries(long nowUnixMs, int limit)
    {
        EnsureReady();
        lock (_gate)
        {
            using var db = CreateContext();
            return db.NotificationDeliveries.AsNoTracking()
                .Where(row => row.Status == "pending" && row.NextAttemptUnixMs <= nowUnixMs)
                .OrderBy(row => row.Id)
                .Take(limit)
                .ToList();
        }
    }

    public int CountDeliveriesSince(string ruleId, string deviceId, string code, string kind, long sinceUnixMs)
    {
        EnsureReady();
        lock (_gate)
        {
            using var db = CreateContext();
            return db.NotificationDeliveries.Count(row =>
                row.RuleId == ruleId
                && row.DeviceId == deviceId
                && row.Code == code
                && row.Kind == kind
                && row.CreatedUnixMs >= sinceUnixMs
                && row.Status != "failed");
        }
    }

    public int CountRuleDeliveriesSince(string ruleId, long sinceUnixMs)
    {
        EnsureReady();
        lock (_gate)
        {
            using var db = CreateContext();
            return db.NotificationDeliveries.Count(row =>
                row.RuleId == ruleId && row.CreatedUnixMs >= sinceUnixMs && row.Kind != "test");
        }
    }

    public bool HasEscalation(string alarmId)
    {
        EnsureReady();
        lock (_gate)
        {
            using var db = CreateContext();
            return db.NotificationDeliveries.Any(row => row.AlarmId == alarmId && row.Kind == "escalation" && row.Status != "failed");
        }
    }

    public IReadOnlyList<AlarmRow> AlarmsRaisedAfter(long unixMs, int limit)
    {
        EnsureReady();
        lock (_gate)
        {
            using var db = CreateContext();
            return db.Alarms.AsNoTracking()
                .Where(row => row.RaisedUnixMs > unixMs)
                .OrderBy(row => row.RaisedUnixMs)
                .Take(limit)
                .ToList();
        }
    }

    public IReadOnlyList<AlarmRow> AlarmsClearedAfter(long unixMs, int limit)
    {
        EnsureReady();
        lock (_gate)
        {
            using var db = CreateContext();
            return db.Alarms.AsNoTracking()
                .Where(row => row.ClearedUnixMs != null && row.ClearedUnixMs > unixMs)
                .OrderBy(row => row.ClearedUnixMs)
                .Take(limit)
                .ToList();
        }
    }

    public IReadOnlyList<AlarmRow> ActiveUnacknowledgedBefore(long raisedBeforeUnixMs, int limit)
    {
        EnsureReady();
        lock (_gate)
        {
            using var db = CreateContext();
            return db.Alarms.AsNoTracking()
                .Where(row => row.Active && !row.Acknowledged && row.RaisedUnixMs <= raisedBeforeUnixMs)
                .OrderBy(row => row.RaisedUnixMs)
                .Take(limit)
                .ToList();
        }
    }
}
