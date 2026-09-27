using Gateway.Abstractions.Contracts;
using Microsoft.EntityFrameworkCore;

namespace IotDaq.Persistence;

public sealed partial class GatewayPersistence
{
    public void Upsert(string deviceId, string status, string? message, DateTimeOffset timestamp)
    {
        if (string.IsNullOrWhiteSpace(deviceId))
        {
            return;
        }

        EnsureReady();
        lock (_gate)
        {
            using var db = CreateContext();
            var row = db.LinkStatus.FirstOrDefault(item => item.DeviceId == deviceId);
            if (row is null)
            {
                row = new LinkStatusRow { DeviceId = deviceId };
                db.LinkStatus.Add(row);
            }

            row.Status = status;
            row.Message = message ?? "";
            row.UnixMs = timestamp.ToUnixTimeMilliseconds();
            db.SaveChanges();
        }
    }

    public IReadOnlyList<LinkStatusRow> ListLinkStatus()
    {
        EnsureReady();
        lock (_gate)
        {
            using var db = CreateContext();
            return db.LinkStatus.AsNoTracking().OrderBy(row => row.DeviceId).ToList();
        }
    }

    public IReadOnlyList<SampleView> LatestSince(long unixMsExclusive, int limit)
    {
        EnsureReady();
        limit = Math.Clamp(limit, 1, 20_000);
        lock (_gate)
        {
            using var db = CreateContext();
            return db.SampleLatest.AsNoTracking()
                .Where(row => row.TimestampUnixMs > unixMsExclusive)
                .OrderBy(row => row.TimestampUnixMs)
                .Take(limit)
                .Select(row => new SampleView
                {
                    DeviceId = row.DeviceId,
                    PointId = row.PointId,
                    Value = row.ValueText,
                    NumericValue = row.NumericValue,
                    Quality = row.Quality,
                    Unit = row.Unit,
                    TimestampUnixMs = row.TimestampUnixMs,
                    Computed = row.Computed
                })
                .ToList();
        }
    }

    public IReadOnlyList<SampleView> LatestAll(int limit)
    {
        EnsureReady();
        limit = Math.Clamp(limit, 1, 20_000);
        lock (_gate)
        {
            using var db = CreateContext();
            return db.SampleLatest.AsNoTracking()
                .OrderBy(row => row.DeviceId)
                .ThenBy(row => row.PointId)
                .Take(limit)
                .Select(row => new SampleView
                {
                    DeviceId = row.DeviceId,
                    PointId = row.PointId,
                    Value = row.ValueText,
                    NumericValue = row.NumericValue,
                    Quality = row.Quality,
                    Unit = row.Unit,
                    TimestampUnixMs = row.TimestampUnixMs,
                    Computed = row.Computed
                })
                .ToList();
        }
    }

    public IReadOnlyList<HttpPushTargetRow> ListHttpPushTargets()
    {
        EnsureReady();
        lock (_gate)
        {
            using var db = CreateContext();
            return db.HttpPushTargets.AsNoTracking().OrderBy(row => row.Name).ToList();
        }
    }

    public HttpPushTargetRow? FindHttpPushTarget(string id)
    {
        EnsureReady();
        lock (_gate)
        {
            using var db = CreateContext();
            return db.HttpPushTargets.AsNoTracking().FirstOrDefault(row => row.Id == id);
        }
    }

    public void SaveHttpPushTarget(HttpPushTargetRow row)
    {
        EnsureReady();
        lock (_gate)
        {
            using var db = CreateContext();
            var existing = db.HttpPushTargets.FirstOrDefault(item => item.Id == row.Id);
            if (existing is null)
            {
                db.HttpPushTargets.Add(row);
            }
            else
            {
                existing.Name = row.Name;
                existing.Enabled = row.Enabled;
                existing.Url = row.Url;
                existing.Method = row.Method;
                existing.HeadersJson = row.HeadersJson;
                existing.AuthKind = row.AuthKind;
                existing.AuthUser = row.AuthUser;
                existing.AuthSecret = row.AuthSecret;
                existing.SignatureHeader = row.SignatureHeader;
                existing.SendValues = row.SendValues;
                existing.ValueMode = row.ValueMode;
                existing.PeriodicSeconds = row.PeriodicSeconds;
                existing.SendStatus = row.SendStatus;
                existing.SendAlarms = row.SendAlarms;
                existing.BatchMax = row.BatchMax;
                existing.BatchIntervalMs = row.BatchIntervalMs;
                existing.TimeoutMs = row.TimeoutMs;
                existing.MaxRetries = row.MaxRetries;
                existing.BackoffInitialMs = row.BackoffInitialMs;
                existing.BackoffMaxMs = row.BackoffMaxMs;
                existing.UpdatedUnixMs = row.UpdatedUnixMs;
            }

            db.SaveChanges();
        }
    }

    public bool DeleteHttpPushTarget(string id)
    {
        EnsureReady();
        lock (_gate)
        {
            using var db = CreateContext();
            var row = db.HttpPushTargets.FirstOrDefault(item => item.Id == id);
            if (row is null)
            {
                return false;
            }

            db.HttpPushTargets.Remove(row);
            db.SaveChanges();
            return true;
        }
    }

    public void NoteHttpPushAttempt(string id, bool success, string error, long spoolDropped)
    {
        EnsureReady();
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        lock (_gate)
        {
            using var db = CreateContext();
            var row = db.HttpPushTargets.FirstOrDefault(item => item.Id == id);
            if (row is null)
            {
                return;
            }

            row.LastAttemptUnixMs = now;
            row.LastError = error.Length > 300 ? error[..300] : error;
            row.SpoolDropped = spoolDropped;
            if (success)
            {
                row.Delivered++;
                row.LastSuccessUnixMs = now;
                row.LastError = "";
            }
            else
            {
                row.Failed++;
            }

            db.SaveChanges();
        }
    }

    public IReadOnlyList<ApiKeyRow> ListApiKeys()
    {
        EnsureReady();
        lock (_gate)
        {
            using var db = CreateContext();
            return db.ApiKeys.AsNoTracking().OrderByDescending(row => row.CreatedUnixMs).ToList();
        }
    }

    public ApiKeyRow? FindApiKeyByHash(string hash)
    {
        EnsureReady();
        lock (_gate)
        {
            using var db = CreateContext();
            return db.ApiKeys.AsNoTracking().FirstOrDefault(row => row.KeyHash == hash);
        }
    }

    public void InsertApiKey(ApiKeyRow row)
    {
        EnsureReady();
        lock (_gate)
        {
            using var db = CreateContext();
            db.ApiKeys.Add(row);
            db.SaveChanges();
        }
    }

    public bool RevokeApiKey(string id, long unixMs)
    {
        EnsureReady();
        lock (_gate)
        {
            using var db = CreateContext();
            var row = db.ApiKeys.FirstOrDefault(item => item.Id == id);
            if (row is null || row.RevokedUnixMs is not null)
            {
                return row is not null && row.RevokedUnixMs is not null;
            }

            row.RevokedUnixMs = unixMs;
            db.SaveChanges();
            return true;
        }
    }

    public void TouchApiKeyUsed(string id, long unixMs)
    {
        EnsureReady();
        lock (_gate)
        {
            using var db = CreateContext();
            var row = db.ApiKeys.FirstOrDefault(item => item.Id == id);
            if (row is null)
            {
                return;
            }

            if (row.LastUsedUnixMs is long previous && unixMs - previous < 60_000)
            {
                return;
            }

            row.LastUsedUnixMs = unixMs;
            db.SaveChanges();
        }
    }
}
