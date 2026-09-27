using IotDaq.Persistence.Rules;
using Microsoft.EntityFrameworkCore;

namespace IotDaq.Persistence;

public sealed partial class GatewayPersistence
{
    public IReadOnlyList<ComputedPointRow> ListComputedPoints()
    {
        EnsureReady();
        lock (_gate)
        {
            using var db = CreateContext();
            return db.ComputedPoints.AsNoTracking().OrderBy(row => row.OwnerId).ThenBy(row => row.PointId).ToList();
        }
    }

    public ComputedPointRow? FindComputedPoint(string id)
    {
        EnsureReady();
        lock (_gate)
        {
            using var db = CreateContext();
            return db.ComputedPoints.AsNoTracking().FirstOrDefault(row => row.Id == id);
        }
    }

    public void SaveComputedPoint(ComputedPointRow row)
    {
        EnsureReady();
        lock (_gate)
        {
            using var db = CreateContext();
            var existing = db.ComputedPoints.FirstOrDefault(item => item.Id == row.Id);
            if (existing is null)
            {
                db.ComputedPoints.Add(row);
            }
            else
            {
                existing.Scope = row.Scope;
                existing.OwnerId = row.OwnerId;
                existing.PointId = row.PointId;
                existing.Name = row.Name;
                existing.Unit = row.Unit;
                existing.Expression = row.Expression;
                existing.Enabled = row.Enabled;
                existing.UpdatedUnixMs = row.UpdatedUnixMs;
                existing.UpdatedBy = row.UpdatedBy;
            }

            db.SaveChanges();
        }
    }

    public bool DeleteComputedPoint(string id)
    {
        EnsureReady();
        lock (_gate)
        {
            using var db = CreateContext();
            var row = db.ComputedPoints.FirstOrDefault(item => item.Id == id);
            if (row is null)
            {
                return false;
            }

            db.ComputedPoints.Remove(row);
            db.SaveChanges();
            return true;
        }
    }

    public IReadOnlyList<EdgeRuleRow> ListEdgeRules()
    {
        EnsureReady();
        lock (_gate)
        {
            using var db = CreateContext();
            return db.EdgeRules.AsNoTracking().OrderBy(row => row.Name).ToList();
        }
    }

    public EdgeRuleRow? FindEdgeRule(string id)
    {
        EnsureReady();
        lock (_gate)
        {
            using var db = CreateContext();
            return db.EdgeRules.AsNoTracking().FirstOrDefault(row => row.Id == id);
        }
    }

    public void SaveEdgeRule(EdgeRuleRow row)
    {
        EnsureReady();
        lock (_gate)
        {
            using var db = CreateContext();
            var existing = db.EdgeRules.FirstOrDefault(item => item.Id == row.Id);
            if (existing is null)
            {
                db.EdgeRules.Add(row);
            }
            else
            {
                existing.Name = row.Name;
                existing.Enabled = row.Enabled;
                existing.Scope = row.Scope;
                existing.OwnerId = row.OwnerId;
                existing.Expression = row.Expression;
                existing.DurationMs = row.DurationMs;
                existing.DebounceMs = row.DebounceMs;
                existing.ActionsJson = row.ActionsJson;
                existing.TemplateKey = row.TemplateKey;
                existing.UpdatedUnixMs = row.UpdatedUnixMs;
                existing.UpdatedBy = row.UpdatedBy;
            }

            db.SaveChanges();
        }
    }

    public bool DeleteEdgeRule(string id)
    {
        EnsureReady();
        lock (_gate)
        {
            using var db = CreateContext();
            var row = db.EdgeRules.FirstOrDefault(item => item.Id == id);
            if (row is null)
            {
                return false;
            }

            db.EdgeRules.Remove(row);
            db.SaveChanges();
            return true;
        }
    }

    public void AppendRuleLog(string ruleId, string deviceId, bool fired, string message, string detail)
    {
        EnsureReady();
        lock (_gate)
        {
            using var db = CreateContext();
            db.RuleLogs.Add(new RuleLogRow
            {
                RuleId = TrimAudit(ruleId, 80),
                DeviceId = TrimAudit(deviceId, 80),
                UnixMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                Fired = fired,
                Message = TrimAudit(message, 200),
                Detail = TrimAudit(detail, 400)
            });
            db.SaveChanges();
            var stale = db.RuleLogs.OrderByDescending(row => row.Id).Skip(2000).Select(row => row.Id).FirstOrDefault();
            if (stale > 0)
            {
                db.RuleLogs.Where(row => row.Id <= stale).ExecuteDelete();
            }
        }
    }

    public IReadOnlyList<RuleLogRow> ListRuleLogs(string? ruleId, int limit)
    {
        EnsureReady();
        limit = Math.Clamp(limit, 1, 200);
        lock (_gate)
        {
            using var db = CreateContext();
            var query = db.RuleLogs.AsNoTracking().AsQueryable();
            if (!string.IsNullOrWhiteSpace(ruleId))
            {
                query = query.Where(row => row.RuleId == ruleId);
            }

            return query.OrderByDescending(row => row.Id).Take(limit).ToList();
        }
    }

    public long AppendRuleEvent(string ruleId, string deviceId, string name, string message)
    {
        EnsureReady();
        lock (_gate)
        {
            using var db = CreateContext();
            var row = new RuleEventRow
            {
                RuleId = ruleId,
                DeviceId = deviceId,
                Name = TrimAudit(name, 80),
                Message = TrimAudit(message, 200),
                UnixMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
            };
            db.RuleEvents.Add(row);
            db.SaveChanges();
            return row.Id;
        }
    }

    public IReadOnlyList<RuleEventRow> PendingRuleEvents(int limit)
    {
        EnsureReady();
        lock (_gate)
        {
            using var db = CreateContext();
            return db.RuleEvents.AsNoTracking()
                .Where(row => !row.Published)
                .OrderBy(row => row.Id)
                .Take(Math.Clamp(limit, 1, 200))
                .ToList();
        }
    }

    public void MarkRuleEventsPublished(IReadOnlyCollection<long> ids)
    {
        if (ids.Count == 0)
        {
            return;
        }

        EnsureReady();
        lock (_gate)
        {
            using var db = CreateContext();
            foreach (var row in db.RuleEvents.Where(item => ids.Contains(item.Id)))
            {
                row.Published = true;
            }

            db.SaveChanges();
        }
    }

    public IReadOnlyList<DowntimeReasonRow> ListReasons()
    {
        EnsureReady();
        lock (_gate)
        {
            using var db = CreateContext();
            return db.DowntimeReasons.AsNoTracking().OrderBy(row => row.Sort).ThenBy(row => row.Name).ToList();
        }
    }

    public void SaveReason(DowntimeReasonRow row)
    {
        EnsureReady();
        lock (_gate)
        {
            using var db = CreateContext();
            var existing = db.DowntimeReasons.FirstOrDefault(item => item.Id == row.Id);
            if (existing is null)
            {
                db.DowntimeReasons.Add(row);
            }
            else
            {
                existing.ParentId = row.ParentId;
                existing.Code = row.Code;
                existing.Name = row.Name;
                existing.Sort = row.Sort;
                existing.Enabled = row.Enabled;
            }

            db.SaveChanges();
        }
    }

    public bool DeleteReason(string id)
    {
        EnsureReady();
        lock (_gate)
        {
            using var db = CreateContext();
            if (db.DowntimeReasons.Any(row => row.ParentId == id) || db.DowntimeEvents.Any(row => row.ReasonId == id))
            {
                return false;
            }

            var row = db.DowntimeReasons.FirstOrDefault(item => item.Id == id);
            if (row is null)
            {
                return false;
            }

            db.DowntimeReasons.Remove(row);
            db.SaveChanges();
            return true;
        }
    }

    public IReadOnlyList<DowntimeEventRow> ListDowntime(string? deviceId, long? fromUnixMs, long? toUnixMs, bool openOnly, int limit)
    {
        EnsureReady();
        limit = Math.Clamp(limit, 1, 500);
        lock (_gate)
        {
            using var db = CreateContext();
            var query = db.DowntimeEvents.AsNoTracking().AsQueryable();
            if (!string.IsNullOrWhiteSpace(deviceId))
            {
                query = query.Where(row => row.DeviceId == deviceId);
            }

            if (fromUnixMs is long from)
            {
                query = query.Where(row => (row.EndedUnixMs ?? row.StartedUnixMs) >= from);
            }

            if (toUnixMs is long to)
            {
                query = query.Where(row => row.StartedUnixMs <= to);
            }

            if (openOnly)
            {
                query = query.Where(row => row.EndedUnixMs == null);
            }

            return query.OrderByDescending(row => row.StartedUnixMs).Take(limit).ToList();
        }
    }

    public int AssignDowntime(IReadOnlyCollection<string> eventIds, string? reasonId, string note, string source, string user, string? ruleId)
    {
        if (eventIds.Count == 0)
        {
            return 0;
        }

        EnsureReady();
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        lock (_gate)
        {
            using var db = CreateContext();
            var rows = db.DowntimeEvents.Where(row => eventIds.Contains(row.Id)).ToList();
            foreach (var row in rows)
            {
                row.ReasonId = string.IsNullOrWhiteSpace(reasonId) ? null : reasonId;
                row.Note = note ?? "";
                row.Source = source;
                row.AssignedBy = user;
                row.AssignedUnixMs = now;
                row.RuleId = ruleId;
            }

            db.SaveChanges();
            return rows.Count;
        }
    }

    public string? AssignOpenDowntime(string deviceId, string reasonId, string ruleId)
    {
        EnsureReady();
        lock (_gate)
        {
            using var db = CreateContext();
            var row = db.DowntimeEvents
                .Where(item => item.DeviceId == deviceId && item.EndedUnixMs == null)
                .OrderByDescending(item => item.StartedUnixMs)
                .FirstOrDefault();
            if (row is null || !string.IsNullOrEmpty(row.ReasonId))
            {
                return null;
            }

            row.ReasonId = reasonId;
            row.Source = "rule";
            row.RuleId = ruleId;
            row.AssignedUnixMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            db.SaveChanges();
            return row.Id;
        }
    }

    public IReadOnlyList<StateMapRow> ListStateMaps()
    {
        EnsureReady();
        lock (_gate)
        {
            using var db = CreateContext();
            return db.StateMaps.AsNoTracking().OrderBy(row => row.Scope).ThenBy(row => row.OwnerId).ToList();
        }
    }

    public void SaveStateMap(StateMapRow row)
    {
        EnsureReady();
        lock (_gate)
        {
            using var db = CreateContext();
            var existing = db.StateMaps.FirstOrDefault(item => item.Id == row.Id);
            if (existing is null)
            {
                db.StateMaps.Add(row);
            }
            else
            {
                existing.Scope = row.Scope;
                existing.OwnerId = row.OwnerId;
                existing.RawValue = row.RawValue;
                existing.State = row.State;
            }

            db.SaveChanges();
        }
    }

    public bool DeleteStateMap(string id)
    {
        EnsureReady();
        lock (_gate)
        {
            using var db = CreateContext();
            var row = db.StateMaps.FirstOrDefault(item => item.Id == id);
            if (row is null)
            {
                return false;
            }

            db.StateMaps.Remove(row);
            db.SaveChanges();
            return true;
        }
    }

    public IReadOnlyList<PlannedStopRow> ListPlannedStops()
    {
        EnsureReady();
        lock (_gate)
        {
            using var db = CreateContext();
            return db.PlannedStops.AsNoTracking().OrderByDescending(row => row.StartUnixMs).Take(200).ToList();
        }
    }

    public void SavePlannedStop(PlannedStopRow row)
    {
        EnsureReady();
        lock (_gate)
        {
            using var db = CreateContext();
            var existing = db.PlannedStops.FirstOrDefault(item => item.Id == row.Id);
            if (existing is null)
            {
                db.PlannedStops.Add(row);
            }
            else
            {
                existing.Scope = row.Scope;
                existing.OwnerId = row.OwnerId;
                existing.Name = row.Name;
                existing.StartUnixMs = row.StartUnixMs;
                existing.EndUnixMs = row.EndUnixMs;
            }

            db.SaveChanges();
        }
    }

    public bool DeletePlannedStop(string id)
    {
        EnsureReady();
        lock (_gate)
        {
            using var db = CreateContext();
            var row = db.PlannedStops.FirstOrDefault(item => item.Id == id);
            if (row is null)
            {
                return false;
            }

            db.PlannedStops.Remove(row);
            db.SaveChanges();
            return true;
        }
    }

    public IReadOnlyList<CycleTimeRow> ListCycleTimes()
    {
        EnsureReady();
        lock (_gate)
        {
            using var db = CreateContext();
            return db.CycleTimes.AsNoTracking().OrderBy(row => row.OwnerId).ThenBy(row => row.Program).ToList();
        }
    }

    public void SaveCycleTime(CycleTimeRow row)
    {
        EnsureReady();
        lock (_gate)
        {
            using var db = CreateContext();
            var existing = db.CycleTimes.FirstOrDefault(item => item.Id == row.Id);
            if (existing is null)
            {
                db.CycleTimes.Add(row);
            }
            else
            {
                existing.Scope = row.Scope;
                existing.OwnerId = row.OwnerId;
                existing.Program = row.Program;
                existing.IdealSeconds = row.IdealSeconds;
            }

            db.SaveChanges();
        }
    }

    public bool DeleteCycleTime(string id)
    {
        EnsureReady();
        lock (_gate)
        {
            using var db = CreateContext();
            var row = db.CycleTimes.FirstOrDefault(item => item.Id == id);
            if (row is null)
            {
                return false;
            }

            db.CycleTimes.Remove(row);
            db.SaveChanges();
            return true;
        }
    }

    public void AddScrap(ScrapEntryRow row)
    {
        EnsureReady();
        lock (_gate)
        {
            using var db = CreateContext();
            db.ScrapEntries.Add(row);
            db.SaveChanges();
        }
    }

    public IReadOnlyList<ScrapEntryRow> ListScrap(string? deviceId, long fromUnixMs, long toUnixMs)
    {
        EnsureReady();
        lock (_gate)
        {
            using var db = CreateContext();
            var query = db.ScrapEntries.AsNoTracking().Where(row => row.UnixMs >= fromUnixMs && row.UnixMs <= toUnixMs);
            if (!string.IsNullOrWhiteSpace(deviceId))
            {
                query = query.Where(row => row.DeviceId == deviceId);
            }

            return query.OrderBy(row => row.UnixMs).Take(2000).ToList();
        }
    }

    public IReadOnlyList<HistoryFrame> HistoryFrames(string deviceId, IReadOnlyCollection<string> points, long fromUnixMs, long toUnixMs, int limit)
    {
        EnsureReady();
        limit = Math.Clamp(limit, 1, 5000);
        lock (_gate)
        {
            using var db = CreateContext();
            var query = db.SampleHistory.AsNoTracking()
                .Where(row => row.DeviceId == deviceId && row.TimestampUnixMs >= fromUnixMs && row.TimestampUnixMs <= toUnixMs);
            if (points.Count > 0)
            {
                query = query.Where(row => points.Contains(row.PointId));
            }

            var rows = query.OrderBy(row => row.TimestampUnixMs).Take(limit).ToList();
            return rows
                .GroupBy(row => row.TimestampUnixMs)
                .OrderBy(group => group.Key)
                .Select(group =>
                {
                    var frame = new HistoryFrame { UnixMs = group.Key };
                    foreach (var row in group)
                    {
                        frame.Numbers[row.PointId] = row.NumericValue;
                        frame.Texts[row.PointId] = row.ValueText;
                    }

                    return frame;
                })
                .ToList();
        }
    }

    public void UpsertRuleAlarm(string deviceId, string ruleId, string code, string message, string severity, bool active, long unixMs)
    {
        EnsureReady();
        var pointId = "rule:" + ruleId;
        lock (_gate)
        {
            using var db = CreateContext();
            var open = db.Alarms.FirstOrDefault(row => row.DeviceId == deviceId && row.PointId == pointId && row.Active);
            if (!active)
            {
                if (open is not null)
                {
                    open.Active = false;
                    open.ClearedUnixMs = unixMs;
                    open.DurationMs = Math.Max(0, unixMs - open.RaisedUnixMs);
                    db.SaveChanges();
                }

                return;
            }

            if (open is not null && open.Message == message && open.Code == code)
            {
                return;
            }

            if (open is not null)
            {
                open.Active = false;
                open.ClearedUnixMs = unixMs;
                open.DurationMs = Math.Max(0, unixMs - open.RaisedUnixMs);
            }

            db.Alarms.Add(new AlarmRow
            {
                Id = Guid.NewGuid().ToString("N"),
                DeviceId = deviceId,
                PointId = pointId,
                Code = code,
                Message = message,
                Severity = string.IsNullOrWhiteSpace(severity) ? "warning" : severity,
                Active = true,
                RaisedUnixMs = unixMs
            });
            db.SaveChanges();
        }
    }
}
