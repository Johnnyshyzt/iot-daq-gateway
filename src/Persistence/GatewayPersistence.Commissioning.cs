using Microsoft.EntityFrameworkCore;

namespace IotDaq.Persistence;

public sealed partial class GatewayPersistence
{
    public string? ReadSecurityPayload()
    {
        EnsureReady();
        lock (_gate)
        {
            using var db = CreateContext();
            return db.SecurityState.FirstOrDefault(row => row.Id == 1)?.Payload;
        }
    }

    public void WriteSecurityPayload(string payload, long unixMs)
    {
        EnsureReady();
        lock (_gate)
        {
            using var db = CreateContext();
            var row = db.SecurityState.FirstOrDefault(item => item.Id == 1);
            if (row is null)
            {
                row = new SecurityStateRow { Id = 1 };
                db.SecurityState.Add(row);
            }

            row.Payload = payload;
            row.UpdatedUnixMs = unixMs;
            db.SaveChanges();
        }
    }

    public long InsertSelfTest(string deviceId, bool passed, string summary, string stagesJson, long startedUnixMs, long finishedUnixMs)
    {
        EnsureReady();
        lock (_gate)
        {
            using var db = CreateContext();
            var row = new SelfTestRunRow
            {
                DeviceId = deviceId,
                Passed = passed,
                Summary = summary.Length > 400 ? summary[..400] : summary,
                StagesJson = stagesJson,
                StartedUnixMs = startedUnixMs,
                FinishedUnixMs = finishedUnixMs
            };
            db.SelfTestRuns.Add(row);
            db.SaveChanges();
            var stale = db.SelfTestRuns.OrderByDescending(item => item.Id).Skip(200).Select(item => item.Id).FirstOrDefault();
            if (stale > 0)
            {
                db.SelfTestRuns.Where(item => item.Id <= stale).ExecuteDelete();
            }

            return row.Id;
        }
    }

    public SelfTestRunRow? LatestSelfTest(string deviceId)
    {
        EnsureReady();
        lock (_gate)
        {
            using var db = CreateContext();
            return db.SelfTestRuns.AsNoTracking()
                .Where(row => row.DeviceId == deviceId)
                .OrderByDescending(row => row.Id)
                .FirstOrDefault();
        }
    }

    public IReadOnlyList<SelfTestRunRow> ListSelfTests(int limit)
    {
        EnsureReady();
        limit = Math.Clamp(limit, 1, 100);
        lock (_gate)
        {
            using var db = CreateContext();
            return db.SelfTestRuns.AsNoTracking().OrderByDescending(row => row.Id).Take(limit).ToList();
        }
    }
}
