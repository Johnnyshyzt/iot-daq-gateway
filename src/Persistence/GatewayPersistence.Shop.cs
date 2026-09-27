using IotDaq.Persistence.Shop;
using Microsoft.EntityFrameworkCore;

namespace IotDaq.Persistence;

public sealed partial class GatewayPersistence
{
    public IReadOnlyList<ToolRow> ListTools()
    {
        EnsureReady();
        lock (_gate)
        {
            using var db = CreateContext();
            return db.Tools.AsNoTracking().OrderBy(row => row.ToolNumber).ToList();
        }
    }

    public IReadOnlyList<ToolPocketRow> ListPockets(string? deviceId)
    {
        EnsureReady();
        lock (_gate)
        {
            using var db = CreateContext();
            var query = db.ToolPockets.AsNoTracking().AsQueryable();
            if (!string.IsNullOrWhiteSpace(deviceId))
            {
                query = query.Where(row => row.DeviceId == deviceId);
            }

            return query.OrderBy(row => row.DeviceId).ThenBy(row => row.Pocket).ToList();
        }
    }

    public IReadOnlyList<ToolLifeRow> ListToolLife(string? deviceId)
    {
        EnsureReady();
        lock (_gate)
        {
            using var db = CreateContext();
            var query = db.ToolLives.AsNoTracking().AsQueryable();
            if (!string.IsNullOrWhiteSpace(deviceId))
            {
                query = query.Where(row => row.DeviceId == deviceId);
            }

            return query.OrderBy(row => row.DeviceId).ThenBy(row => row.ToolNumber).ToList();
        }
    }

    public IReadOnlyList<ToolChangeRow> ListToolChanges(string? deviceId, int limit)
    {
        EnsureReady();
        limit = Math.Clamp(limit, 1, 500);
        lock (_gate)
        {
            using var db = CreateContext();
            var query = db.ToolChanges.AsNoTracking().AsQueryable();
            if (!string.IsNullOrWhiteSpace(deviceId))
            {
                query = query.Where(row => row.DeviceId == deviceId);
            }

            return query.OrderByDescending(row => row.UnixMs).Take(limit).ToList();
        }
    }

    public ToolRow SaveTool(ToolRow row)
    {
        EnsureReady();
        lock (_gate)
        {
            using var db = CreateContext();
            var existing = db.Tools.FirstOrDefault(item => item.Id == row.Id);
            if (existing is null)
            {
                db.Tools.Add(row);
            }
            else
            {
                existing.ToolNumber = row.ToolNumber;
                existing.Description = row.Description;
                existing.LifeLimitCount = row.LifeLimitCount;
                existing.LifeLimitMinutes = row.LifeLimitMinutes;
                existing.WarningPercent = row.WarningPercent;
                existing.Enabled = row.Enabled;
                existing.UpdatedBy = row.UpdatedBy;
                existing.UpdatedUnixMs = row.UpdatedUnixMs;
                row = existing;
            }

            db.SaveChanges();
            return row;
        }
    }

    public bool DeleteTool(string id)
    {
        EnsureReady();
        lock (_gate)
        {
            using var db = CreateContext();
            var row = db.Tools.FirstOrDefault(item => item.Id == id);
            if (row is null)
            {
                return false;
            }

            db.Tools.Remove(row);
            db.SaveChanges();
            return true;
        }
    }

    public ToolPocketRow SavePocket(ToolPocketRow row)
    {
        EnsureReady();
        lock (_gate)
        {
            using var db = CreateContext();
            var existing = db.ToolPockets.FirstOrDefault(item => item.Id == row.Id)
                ?? db.ToolPockets.FirstOrDefault(item => item.DeviceId == row.DeviceId && item.Pocket == row.Pocket);
            if (existing is null)
            {
                db.ToolPockets.Add(row);
            }
            else
            {
                existing.DeviceId = row.DeviceId;
                existing.Pocket = row.Pocket;
                existing.ToolNumber = row.ToolNumber;
                row = existing;
            }

            db.SaveChanges();
            return row;
        }
    }

    public ToolAdvanceResult AdvanceTool(string deviceId, ToolSignal signal)
    {
        EnsureReady();
        lock (_gate)
        {
            using var db = CreateContext();
            var cursorRow = db.ToolCursors.FirstOrDefault(row => row.DeviceId == deviceId);
            if (cursorRow is null)
            {
                cursorRow = new ToolCursorRow { DeviceId = deviceId };
                db.ToolCursors.Add(cursorRow);
            }

            var cursor = new ToolCursor
            {
                ToolNumber = cursorRow.ToolNumber,
                LastPartCount = cursorRow.LastPartCount,
                LastCycleSeconds = cursorRow.LastCycleSeconds,
                LastUnixMs = cursorRow.LastUnixMs,
                WasCutting = cursorRow.WasCutting
            };
            var toolNumber = ToolLifeMath.NormalizeTool(signal.ToolNumber);
            ToolLifeRow? lifeRow = null;
            var totals = new ToolLifeTotals();
            if (toolNumber.Length > 0)
            {
                lifeRow = db.ToolLives.FirstOrDefault(row => row.DeviceId == deviceId && row.ToolNumber == toolNumber);
                if (lifeRow is null)
                {
                    lifeRow = new ToolLifeRow
                    {
                        Id = Guid.NewGuid().ToString("N"),
                        DeviceId = deviceId,
                        ToolNumber = toolNumber
                    };
                    db.ToolLives.Add(lifeRow);
                }

                totals.UsedCount = lifeRow.UsedCount;
                totals.UsedCuttingMs = lifeRow.UsedCuttingMs;
                totals.Source = lifeRow.Source;
                totals.Level = lifeRow.Level;
            }

            var master = toolNumber.Length == 0
                ? null
                : db.Tools.AsNoTracking().FirstOrDefault(row => row.ToolNumber == toolNumber && row.Enabled);
            var limits = new ToolLimits
            {
                Count = master?.LifeLimitCount,
                CuttingMinutes = master?.LifeLimitMinutes,
                WarningPercent = master?.WarningPercent ?? 80
            };
            var advance = ToolLifeMath.Advance(cursor, totals, limits, signal);
            cursorRow.ToolNumber = cursor.ToolNumber;
            cursorRow.LastPartCount = cursor.LastPartCount;
            cursorRow.LastCycleSeconds = cursor.LastCycleSeconds;
            cursorRow.LastUnixMs = cursor.LastUnixMs;
            cursorRow.WasCutting = cursor.WasCutting;
            if (lifeRow is not null && advance.Updated)
            {
                lifeRow.UsedCount = totals.UsedCount;
                lifeRow.UsedCuttingMs = totals.UsedCuttingMs;
                lifeRow.Source = totals.Source;
                lifeRow.Level = totals.Level;
                lifeRow.UpdatedUnixMs = signal.UnixMs;
                WriteToolAlarm(db, deviceId, lifeRow, master, signal.UnixMs);
            }

            db.SaveChanges();
            return new ToolAdvanceResult(advance, master is not null);
        }
    }

    public ToolLifeRow AddManualCount(string deviceId, string toolNumber, double count, long unixMs)
    {
        EnsureReady();
        var normalized = ToolLifeMath.NormalizeTool(toolNumber);
        if (normalized.Length == 0)
        {
            throw new InvalidOperationException("请填写刀号。");
        }

        if (count <= 0)
        {
            throw new InvalidOperationException("手动计数必须大于 0。");
        }

        lock (_gate)
        {
            using var db = CreateContext();
            var life = db.ToolLives.FirstOrDefault(row => row.DeviceId == deviceId && row.ToolNumber == normalized);
            if (life is null)
            {
                life = new ToolLifeRow
                {
                    Id = Guid.NewGuid().ToString("N"),
                    DeviceId = deviceId,
                    ToolNumber = normalized
                };
                db.ToolLives.Add(life);
            }

            life.UsedCount += count;
            life.Source = life.Source is "" or "manual" ? "manual" : "mixed";
            var master = db.Tools.AsNoTracking().FirstOrDefault(row => row.ToolNumber == normalized && row.Enabled);
            life.Level = ToolLifeMath.Level(new ToolLimits
            {
                Count = master?.LifeLimitCount,
                CuttingMinutes = master?.LifeLimitMinutes,
                WarningPercent = master?.WarningPercent ?? 80
            }, life.UsedCount, life.UsedCuttingMs);
            life.UpdatedUnixMs = unixMs;
            WriteToolAlarm(db, deviceId, life, master, unixMs);
            db.SaveChanges();
            return life;
        }
    }

    public ToolChangeRow RecordToolChange(ToolChangeRow change, bool resetLife)
    {
        EnsureReady();
        lock (_gate)
        {
            using var db = CreateContext();
            change.ResetLife = resetLife;
            db.ToolChanges.Add(change);
            var number = ToolLifeMath.NormalizeTool(string.IsNullOrWhiteSpace(change.NewToolNumber) ? change.OldToolNumber : change.NewToolNumber);
            if (resetLife && number.Length > 0)
            {
                var life = db.ToolLives.FirstOrDefault(row => row.DeviceId == change.DeviceId && row.ToolNumber == number);
                if (life is null)
                {
                    life = new ToolLifeRow
                    {
                        Id = Guid.NewGuid().ToString("N"),
                        DeviceId = change.DeviceId,
                        ToolNumber = number
                    };
                    db.ToolLives.Add(life);
                }

                life.UsedCount = 0;
                life.UsedCuttingMs = 0;
                life.Level = "ok";
                life.UpdatedUnixMs = change.UnixMs;
                var master = db.Tools.AsNoTracking().FirstOrDefault(row => row.ToolNumber == number);
                WriteToolAlarm(db, change.DeviceId, life, master, change.UnixMs);
            }

            if (change.Pocket is int pocket && number.Length > 0)
            {
                var mapped = db.ToolPockets.FirstOrDefault(row => row.DeviceId == change.DeviceId && row.Pocket == pocket);
                if (mapped is null)
                {
                    db.ToolPockets.Add(new ToolPocketRow
                    {
                        Id = Guid.NewGuid().ToString("N"),
                        DeviceId = change.DeviceId,
                        Pocket = pocket,
                        ToolNumber = number
                    });
                }
                else
                {
                    mapped.ToolNumber = number;
                }
            }

            db.SaveChanges();
            return change;
        }
    }

    public IReadOnlyList<NcProgramRow> ListPrograms()
    {
        EnsureReady();
        lock (_gate)
        {
            using var db = CreateContext();
            return db.NcPrograms.AsNoTracking().OrderBy(row => row.Name).ToList();
        }
    }

    public NcProgramRow? FindProgram(string id)
    {
        EnsureReady();
        lock (_gate)
        {
            using var db = CreateContext();
            return db.NcPrograms.AsNoTracking().FirstOrDefault(row => row.Id == id);
        }
    }

    public IReadOnlyList<NcProgramVersionRow> ListProgramVersions(string programId)
    {
        EnsureReady();
        lock (_gate)
        {
            using var db = CreateContext();
            return db.NcProgramVersions.AsNoTracking().Where(row => row.ProgramId == programId).OrderBy(row => row.Version).ToList();
        }
    }

    public NcProgramVersionRow? FindProgramVersion(string id)
    {
        EnsureReady();
        lock (_gate)
        {
            using var db = CreateContext();
            return db.NcProgramVersions.AsNoTracking().FirstOrDefault(row => row.Id == id);
        }
    }

    public IReadOnlyList<string> ListProgramDevices(string programId)
    {
        EnsureReady();
        lock (_gate)
        {
            using var db = CreateContext();
            return db.NcProgramDevices.AsNoTracking().Where(row => row.ProgramId == programId).Select(row => row.DeviceId).ToList();
        }
    }

    public IReadOnlyList<NcTransferRow> ListTransfers(string? programId, int limit)
    {
        EnsureReady();
        limit = Math.Clamp(limit, 1, 500);
        lock (_gate)
        {
            using var db = CreateContext();
            var query = db.NcTransfers.AsNoTracking().AsQueryable();
            if (!string.IsNullOrWhiteSpace(programId))
            {
                query = query.Where(row => row.ProgramId == programId);
            }

            return query.OrderByDescending(row => row.UnixMs).Take(limit).ToList();
        }
    }

    public NcProgramRow SaveProgram(NcProgramRow row)
    {
        EnsureReady();
        lock (_gate)
        {
            using var db = CreateContext();
            var existing = db.NcPrograms.FirstOrDefault(item => item.Id == row.Id);
            if (existing is null)
            {
                db.NcPrograms.Add(row);
            }
            else
            {
                existing.Name = row.Name;
                existing.Comment = row.Comment;
                existing.Status = row.Status;
                existing.UpdatedUnixMs = row.UpdatedUnixMs;
                row = existing;
            }

            db.SaveChanges();
            return row;
        }
    }

    public NcProgramVersionRow AddProgramVersion(NcProgramVersionRow row)
    {
        EnsureReady();
        lock (_gate)
        {
            using var db = CreateContext();
            var next = db.NcProgramVersions.Where(item => item.ProgramId == row.ProgramId).Select(item => (int?)item.Version).Max() ?? 0;
            row.Version = next + 1;
            db.NcProgramVersions.Add(row);
            var program = db.NcPrograms.FirstOrDefault(item => item.Id == row.ProgramId);
            if (program is not null)
            {
                program.Status = row.Status;
                program.UpdatedUnixMs = row.UploadedUnixMs;
            }

            db.SaveChanges();
            return row;
        }
    }

    public NcProgramVersionRow? SetProgramVersionStatus(string versionId, string status, string? actor, long unixMs)
    {
        EnsureReady();
        lock (_gate)
        {
            using var db = CreateContext();
            var row = db.NcProgramVersions.FirstOrDefault(item => item.Id == versionId);
            if (row is null)
            {
                return null;
            }

            row.Status = status;
            if (status == "approved")
            {
                row.ApprovedBy = actor;
                row.ApprovedUnixMs = unixMs;
            }

            var program = db.NcPrograms.FirstOrDefault(item => item.Id == row.ProgramId);
            if (program is not null)
            {
                var latest = db.NcProgramVersions.Where(item => item.ProgramId == row.ProgramId).OrderByDescending(item => item.Version).First();
                program.Status = latest.Id == row.Id ? status : latest.Status;
                program.UpdatedUnixMs = unixMs;
            }

            db.SaveChanges();
            return row;
        }
    }

    public void SetProgramDevices(string programId, IReadOnlyList<string> deviceIds)
    {
        EnsureReady();
        lock (_gate)
        {
            using var db = CreateContext();
            var existing = db.NcProgramDevices.Where(row => row.ProgramId == programId).ToList();
            db.NcProgramDevices.RemoveRange(existing);
            foreach (var deviceId in deviceIds.Where(id => !string.IsNullOrWhiteSpace(id)).Distinct(StringComparer.Ordinal))
            {
                db.NcProgramDevices.Add(new NcProgramDeviceRow { ProgramId = programId, DeviceId = deviceId });
            }

            db.SaveChanges();
        }
    }

    public void AddTransfer(NcTransferRow row)
    {
        EnsureReady();
        lock (_gate)
        {
            using var db = CreateContext();
            db.NcTransfers.Add(row);
            db.SaveChanges();
        }
    }

    private static void WriteToolAlarm(GatewayDbContext db, string deviceId, ToolLifeRow life, ToolRow? master, long unixMs)
    {
        var pointId = "tool-life:" + life.ToolNumber;
        var open = db.Alarms.Where(row => row.DeviceId == deviceId && row.PointId == pointId && row.Active).ToList();
        if (life.Level is not ("warning" or "eol"))
        {
            foreach (var alarm in open)
            {
                alarm.Active = false;
                alarm.ClearedUnixMs = unixMs;
                alarm.DurationMs = Math.Max(0, unixMs - alarm.RaisedUnixMs);
            }

            return;
        }

        var code = life.Level == "eol" ? "TOOL-EOL" : "TOOL-WARN";
        var message = ToolAlarmMessage(life, master);
        var severity = life.Level == "eol" ? "critical" : "warning";
        if (open.Count == 1 && open[0].Code == code && open[0].Message == message)
        {
            return;
        }

        foreach (var alarm in open)
        {
            alarm.Active = false;
            alarm.ClearedUnixMs = unixMs;
            alarm.DurationMs = Math.Max(0, unixMs - alarm.RaisedUnixMs);
        }

        db.Alarms.Add(new AlarmRow
        {
            Id = Guid.NewGuid().ToString("N"),
            DeviceId = deviceId,
            PointId = pointId,
            Code = code,
            Message = message,
            Severity = severity,
            Active = true,
            RaisedUnixMs = unixMs
        });
    }

    private static string ToolAlarmMessage(ToolLifeRow life, ToolRow? master)
    {
        var parts = new List<string>();
        if (master?.LifeLimitCount is > 0)
        {
            parts.Add($"件数 {life.UsedCount.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture)} / {master.LifeLimitCount.Value.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture)}");
        }

        if (master?.LifeLimitMinutes is > 0)
        {
            var minutes = life.UsedCuttingMs / 60000d;
            parts.Add($"切削 {minutes.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture)} / {master.LifeLimitMinutes.Value.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture)} 分钟");
        }

        var detail = parts.Count == 0 ? "" : "：" + string.Join("，", parts);
        return life.Level == "eol"
            ? $"刀具 {life.ToolNumber} 寿命已到{detail}"
            : $"刀具 {life.ToolNumber} 寿命预警{detail}";
    }
}

public sealed record ToolAdvanceResult(ToolAdvance Advance, bool HasMaster);
