using System.Text.Json;
using Microsoft.EntityFrameworkCore;

namespace IotDaq.Persistence;

public sealed partial class GatewayPersistence
{
    public EnrollmentTokenRow AddEnrollmentToken(EnrollmentTokenRow row)
    {
        EnsureReady();
        lock (_gate)
        {
            using var db = CreateContext();
            db.EnrollmentTokens.Add(row);
            db.SaveChanges();
            return row;
        }
    }

    public EnrollmentTokenRow? TakeEnrollmentToken(string tokenHash, long nowUnixMs)
    {
        EnsureReady();
        lock (_gate)
        {
            using var db = CreateContext();
            var row = db.EnrollmentTokens.FirstOrDefault(item => item.TokenHash == tokenHash && !item.Revoked);
            if (row is null || row.ExpiresUnixMs < nowUnixMs || row.Uses >= row.MaxUses)
            {
                return null;
            }

            row.Uses += 1;
            db.SaveChanges();
            return row;
        }
    }

    public IReadOnlyList<EnrollmentTokenRow> ListEnrollmentTokens()
    {
        EnsureReady();
        lock (_gate)
        {
            using var db = CreateContext();
            return db.EnrollmentTokens.AsNoTracking().OrderByDescending(row => row.CreatedUnixMs).ToList();
        }
    }

    public bool RevokeEnrollmentToken(string id)
    {
        EnsureReady();
        lock (_gate)
        {
            using var db = CreateContext();
            var row = db.EnrollmentTokens.FirstOrDefault(item => item.Id == id);
            if (row is null)
            {
                return false;
            }

            row.Revoked = true;
            db.SaveChanges();
            return true;
        }
    }

    public FleetGatewayRow UpsertFleetGateway(FleetGatewayRow row)
    {
        EnsureReady();
        lock (_gate)
        {
            using var db = CreateContext();
            var existing = db.FleetGateways.FirstOrDefault(item => item.Id == row.Id);
            if (existing is null)
            {
                db.FleetGateways.Add(row);
            }
            else
            {
                existing.Name = string.IsNullOrWhiteSpace(row.Name) ? existing.Name : row.Name;
                existing.GroupId = row.GroupId.Length == 0 ? existing.GroupId : row.GroupId;
                existing.Site = row.Site.Length == 0 ? existing.Site : row.Site;
                if (row.SessionHash.Length > 0)
                {
                    existing.SessionHash = row.SessionHash;
                }

                existing.Version = row.Version;
                existing.LicenseSummary = row.LicenseSummary;
                existing.DeviceCount = row.DeviceCount;
                existing.OnlineLinks = row.OnlineLinks;
                existing.OfflineLinks = row.OfflineLinks;
                existing.WorkingSetMb = row.WorkingSetMb;
                existing.SummaryJson = row.SummaryJson;
                existing.Status = row.Status;
                existing.LastSeenUnixMs = row.LastSeenUnixMs;
                if (existing.EnrolledUnixMs == 0)
                {
                    existing.EnrolledUnixMs = row.EnrolledUnixMs;
                }

                row = existing;
            }

            db.SaveChanges();
            return row;
        }
    }

    public FleetGatewayRow? FindFleetGateway(string id)
    {
        EnsureReady();
        lock (_gate)
        {
            using var db = CreateContext();
            return db.FleetGateways.AsNoTracking().FirstOrDefault(row => row.Id == id);
        }
    }

    public FleetGatewayRow? FindFleetGatewayBySession(string sessionHash)
    {
        EnsureReady();
        lock (_gate)
        {
            using var db = CreateContext();
            return db.FleetGateways.AsNoTracking().FirstOrDefault(row => row.SessionHash == sessionHash);
        }
    }

    public IReadOnlyList<FleetGatewayRow> ListFleetGateways()
    {
        EnsureReady();
        lock (_gate)
        {
            using var db = CreateContext();
            return db.FleetGateways.AsNoTracking().OrderBy(row => row.Name).ToList();
        }
    }

    public void SetFleetGroup(string gatewayId, string groupId)
    {
        EnsureReady();
        lock (_gate)
        {
            using var db = CreateContext();
            var row = db.FleetGateways.FirstOrDefault(item => item.Id == gatewayId);
            if (row is null)
            {
                return;
            }

            row.GroupId = groupId;
            db.SaveChanges();
        }
    }

    public FleetGroupRow SaveFleetGroup(FleetGroupRow row)
    {
        EnsureReady();
        lock (_gate)
        {
            using var db = CreateContext();
            var existing = db.FleetGroups.FirstOrDefault(item => item.Id == row.Id);
            if (existing is null)
            {
                db.FleetGroups.Add(row);
            }
            else
            {
                existing.Name = row.Name;
                row = existing;
            }

            db.SaveChanges();
            return row;
        }
    }

    public IReadOnlyList<FleetGroupRow> ListFleetGroups()
    {
        EnsureReady();
        lock (_gate)
        {
            using var db = CreateContext();
            return db.FleetGroups.AsNoTracking().OrderBy(row => row.Name).ToList();
        }
    }

    public CentralTemplateRow AddTemplateVersion(CentralTemplateRow row)
    {
        EnsureReady();
        lock (_gate)
        {
            using var db = CreateContext();
            var next = db.CentralTemplates.Where(item => item.Key == row.Key).Select(item => (int?)item.Version).Max() ?? 0;
            row.Version = next + 1;
            row.Id = row.Key + "-v" + row.Version.ToString(System.Globalization.CultureInfo.InvariantCulture);
            db.CentralTemplates.Add(row);
            db.SaveChanges();
            return row;
        }
    }

    public IReadOnlyList<CentralTemplateRow> ListTemplates()
    {
        EnsureReady();
        lock (_gate)
        {
            using var db = CreateContext();
            return db.CentralTemplates.AsNoTracking().OrderBy(row => row.Key).ThenBy(row => row.Version).ToList();
        }
    }

    public CentralTemplateRow? FindTemplate(string key, int version)
    {
        EnsureReady();
        lock (_gate)
        {
            using var db = CreateContext();
            return db.CentralTemplates.AsNoTracking().FirstOrDefault(row => row.Key == key && row.Version == version);
        }
    }

    public ConfigPushRow AddConfigPush(ConfigPushRow row, IReadOnlyList<string> gatewayIds)
    {
        EnsureReady();
        lock (_gate)
        {
            using var db = CreateContext();
            row.GatewayIdsJson = JsonSerializer.Serialize(gatewayIds);
            db.ConfigPushes.Add(row);
            foreach (var gatewayId in gatewayIds)
            {
                db.ConfigPushTargets.Add(new ConfigPushTargetRow
                {
                    Id = Guid.NewGuid().ToString("N"),
                    PushId = row.Id,
                    GatewayId = gatewayId,
                    Status = "pending",
                    UpdatedUnixMs = row.CreatedUnixMs
                });
            }

            db.SaveChanges();
            return row;
        }
    }

    public IReadOnlyList<ConfigPushRow> ListConfigPushes(int limit)
    {
        EnsureReady();
        lock (_gate)
        {
            using var db = CreateContext();
            return db.ConfigPushes.AsNoTracking().OrderByDescending(row => row.CreatedUnixMs).Take(Math.Clamp(limit, 1, 200)).ToList();
        }
    }

    public ConfigPushRow? FindConfigPush(string id)
    {
        EnsureReady();
        lock (_gate)
        {
            using var db = CreateContext();
            return db.ConfigPushes.AsNoTracking().FirstOrDefault(row => row.Id == id);
        }
    }

    public IReadOnlyList<ConfigPushTargetRow> ListPushTargets(string? pushId, string? gatewayId, string? status)
    {
        EnsureReady();
        lock (_gate)
        {
            using var db = CreateContext();
            var query = db.ConfigPushTargets.AsNoTracking().AsQueryable();
            if (!string.IsNullOrWhiteSpace(pushId))
            {
                query = query.Where(row => row.PushId == pushId);
            }

            if (!string.IsNullOrWhiteSpace(gatewayId))
            {
                query = query.Where(row => row.GatewayId == gatewayId);
            }

            if (!string.IsNullOrWhiteSpace(status))
            {
                query = query.Where(row => row.Status == status);
            }

            return query.ToList();
        }
    }

    public void SetPushTarget(string pushId, string gatewayId, string status, string message, long unixMs)
    {
        EnsureReady();
        lock (_gate)
        {
            using var db = CreateContext();
            var row = db.ConfigPushTargets.FirstOrDefault(item => item.PushId == pushId && item.GatewayId == gatewayId);
            if (row is null)
            {
                return;
            }

            row.Status = status;
            row.Message = message;
            row.UpdatedUnixMs = unixMs;
            var siblings = db.ConfigPushTargets.Where(item => item.PushId == pushId).ToList();
            var push = db.ConfigPushes.FirstOrDefault(item => item.Id == pushId);
            if (push is not null)
            {
                push.Status = siblings.All(item => item.Status is "applied" or "conflict" or "failed")
                    ? (siblings.Any(item => item.Status == "failed") ? "failed" : "applied")
                    : "applying";
            }

            db.SaveChanges();
        }
    }

    public RolloutRow AddRollout(RolloutRow row, IReadOnlyList<string> gatewayIds)
    {
        EnsureReady();
        lock (_gate)
        {
            using var db = CreateContext();
            db.Rollouts.Add(row);
            foreach (var gatewayId in gatewayIds)
            {
                db.RolloutTargets.Add(new RolloutTargetRow
                {
                    Id = Guid.NewGuid().ToString("N"),
                    RolloutId = row.Id,
                    GatewayId = gatewayId,
                    Status = "pending",
                    UpdatedUnixMs = row.CreatedUnixMs
                });
            }

            db.SaveChanges();
            return row;
        }
    }

    public IReadOnlyList<RolloutRow> ListRollouts()
    {
        EnsureReady();
        lock (_gate)
        {
            using var db = CreateContext();
            return db.Rollouts.AsNoTracking().OrderByDescending(row => row.CreatedUnixMs).ToList();
        }
    }

    public RolloutRow? FindRollout(string id)
    {
        EnsureReady();
        lock (_gate)
        {
            using var db = CreateContext();
            return db.Rollouts.AsNoTracking().FirstOrDefault(row => row.Id == id);
        }
    }

    public IReadOnlyList<RolloutTargetRow> ListRolloutTargets(string? rolloutId, string? gatewayId)
    {
        EnsureReady();
        lock (_gate)
        {
            using var db = CreateContext();
            var query = db.RolloutTargets.AsNoTracking().AsQueryable();
            if (!string.IsNullOrWhiteSpace(rolloutId))
            {
                query = query.Where(row => row.RolloutId == rolloutId);
            }

            if (!string.IsNullOrWhiteSpace(gatewayId))
            {
                query = query.Where(row => row.GatewayId == gatewayId);
            }

            return query.ToList();
        }
    }

    public void SetRolloutTarget(string rolloutId, string gatewayId, string status, string message, long unixMs)
    {
        EnsureReady();
        lock (_gate)
        {
            using var db = CreateContext();
            var row = db.RolloutTargets.FirstOrDefault(item => item.RolloutId == rolloutId && item.GatewayId == gatewayId);
            if (row is null)
            {
                return;
            }

            row.Status = status;
            row.Message = message;
            row.UpdatedUnixMs = unixMs;
            var rollout = db.Rollouts.FirstOrDefault(item => item.Id == rolloutId);
            if (rollout is not null)
            {
                var siblings = db.RolloutTargets.Where(item => item.RolloutId == rolloutId).ToList();
                rollout.Status = siblings.All(item => item.Status is "staged" or "failed")
                    ? (siblings.Any(item => item.Status == "failed") ? "failed" : "staged")
                    : "rolling";
            }

            db.SaveChanges();
        }
    }

    public void TouchFleetSeen(string gatewayId, FleetGatewayRow snapshot, long unixMs)
    {
        EnsureReady();
        lock (_gate)
        {
            using var db = CreateContext();
            var row = db.FleetGateways.FirstOrDefault(item => item.Id == gatewayId);
            if (row is null)
            {
                return;
            }

            row.Version = snapshot.Version;
            row.LicenseSummary = snapshot.LicenseSummary;
            row.DeviceCount = snapshot.DeviceCount;
            row.OnlineLinks = snapshot.OnlineLinks;
            row.OfflineLinks = snapshot.OfflineLinks;
            row.WorkingSetMb = snapshot.WorkingSetMb;
            row.SummaryJson = snapshot.SummaryJson;
            row.Status = "online";
            row.LastSeenUnixMs = unixMs;
            if (!string.IsNullOrWhiteSpace(snapshot.Name))
            {
                row.Name = snapshot.Name;
            }

            foreach (var alert in db.CentralAlerts.Where(item => item.GatewayId == gatewayId && item.Active && item.Kind == "offline"))
            {
                alert.Active = false;
                alert.ClearedUnixMs = unixMs;
            }

            db.SaveChanges();
        }
    }

    public IReadOnlyList<CentralAlertRow> ScanOfflineGateways(long nowUnixMs, long offlineAfterMs)
    {
        EnsureReady();
        lock (_gate)
        {
            using var db = CreateContext();
            var raised = new List<CentralAlertRow>();
            foreach (var gateway in db.FleetGateways.Where(row => row.LastSeenUnixMs > 0))
            {
                var stale = nowUnixMs - gateway.LastSeenUnixMs > offlineAfterMs;
                if (!stale)
                {
                    continue;
                }

                gateway.Status = "offline";
                var open = db.CentralAlerts.FirstOrDefault(row => row.GatewayId == gateway.Id && row.Kind == "offline" && row.Active);
                if (open is not null)
                {
                    continue;
                }

                var alert = new CentralAlertRow
                {
                    Id = Guid.NewGuid().ToString("N"),
                    GatewayId = gateway.Id,
                    Kind = "offline",
                    Message = $"网关 {gateway.Name} 已离线",
                    Active = true,
                    RaisedUnixMs = nowUnixMs
                };
                db.CentralAlerts.Add(alert);
                raised.Add(alert);
            }

            db.SaveChanges();
            return raised;
        }
    }

    public IReadOnlyList<CentralAlertRow> ListCentralAlerts(bool activeOnly)
    {
        EnsureReady();
        lock (_gate)
        {
            using var db = CreateContext();
            var query = db.CentralAlerts.AsNoTracking().AsQueryable();
            if (activeOnly)
            {
                query = query.Where(row => row.Active);
            }

            return query.OrderByDescending(row => row.RaisedUnixMs).Take(200).ToList();
        }
    }

    public void AddCentralAudits(IReadOnlyList<CentralAuditRow> rows)
    {
        if (rows.Count == 0)
        {
            return;
        }

        EnsureReady();
        lock (_gate)
        {
            using var db = CreateContext();
            db.CentralAudits.AddRange(rows);
            db.SaveChanges();
        }
    }

    public IReadOnlyList<CentralAuditRow> ListCentralAudits(int limit)
    {
        EnsureReady();
        lock (_gate)
        {
            using var db = CreateContext();
            return db.CentralAudits.AsNoTracking().OrderByDescending(row => row.UnixMs).Take(Math.Clamp(limit, 1, 500)).ToList();
        }
    }

    public CentralDocumentRow? FindCentralDocument(string kind)
    {
        EnsureReady();
        lock (_gate)
        {
            using var db = CreateContext();
            return db.CentralDocuments.AsNoTracking().FirstOrDefault(row => row.Kind == kind);
        }
    }

    public CentralDocumentRow SaveCentralDocument(CentralDocumentRow row)
    {
        EnsureReady();
        lock (_gate)
        {
            using var db = CreateContext();
            var existing = db.CentralDocuments.FirstOrDefault(item => item.Kind == row.Kind);
            if (existing is null)
            {
                db.CentralDocuments.Add(row);
            }
            else
            {
                existing.TemplateKey = row.TemplateKey;
                existing.Version = row.Version;
                existing.BodyJson = row.BodyJson;
                existing.Dirty = row.Dirty;
                existing.Conflict = row.Conflict;
                existing.UpdatedUnixMs = row.UpdatedUnixMs;
                row = existing;
            }

            db.SaveChanges();
            return row;
        }
    }

    public void MarkCentralDocumentDirty(string kind, bool dirty)
    {
        EnsureReady();
        lock (_gate)
        {
            using var db = CreateContext();
            var row = db.CentralDocuments.FirstOrDefault(item => item.Kind == kind);
            if (row is null)
            {
                db.CentralDocuments.Add(new CentralDocumentRow
                {
                    Kind = kind,
                    Dirty = dirty,
                    UpdatedUnixMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
                });
            }
            else
            {
                row.Dirty = dirty;
            }

            db.SaveChanges();
        }
    }

    public void ReplaceCentralRules(IReadOnlyList<EdgeRuleRow> rules, long unixMs)
    {
        EnsureReady();
        lock (_gate)
        {
            using var db = CreateContext();
            var managed = db.EdgeRules.Where(row => row.Id.StartsWith("central-")).ToList();
            db.EdgeRules.RemoveRange(managed);
            foreach (var rule in rules)
            {
                rule.UpdatedBy = "central";
                rule.UpdatedUnixMs = unixMs;
                if (!rule.Id.StartsWith("central-", StringComparison.Ordinal))
                {
                    rule.Id = "central-" + rule.Id;
                }

                db.EdgeRules.Add(rule);
            }

            db.SaveChanges();
        }
    }

    public bool CentralRulesDirty()
    {
        EnsureReady();
        lock (_gate)
        {
            using var db = CreateContext();
            return db.EdgeRules.Any(row => row.Id.StartsWith("central-") && row.UpdatedBy != "central");
        }
    }
}
