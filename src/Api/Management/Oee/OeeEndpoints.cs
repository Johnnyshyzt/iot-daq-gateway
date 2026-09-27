using System.Text;
using IotDaq.Licensing;
using IotDaq.Persistence;
using IotDaq.Persistence.Rules;
using IotDaq.Persistence.Visualization;
using Studio.Host.Endpoints;
using Studio.Host.Licensing;

namespace Studio.Host.Oee;

public static class OeeEndpoints
{
    public static void Map(RouteGroupBuilder api)
    {
        api.MapGet("/oee", (string? deviceId, string? line, string? from, string? to, OeeService oee, LicenseService licensing) =>
        {
            if (!licensing.Allows(LicenseFeatures.Oee))
            {
                return Results.Json(new { licensed = false, message = licensing.Denial(LicenseFeatures.Oee), formula = OeeService.Formula }, StudioJson.Options);
            }

            var (start, end) = Range(from, to);
            var report = oee.Build(start, end, deviceId, line);
            return Results.Json(new { licensed = true, report }, StudioJson.Options);
        });

        api.MapGet("/oee.csv", (string? deviceId, string? line, string? from, string? to, string? view, OeeService oee, LicenseService licensing) =>
        {
            if (!licensing.Allows(LicenseFeatures.Oee))
            {
                return ApiResults.Error(StatusCodes.Status403Forbidden, "license_feature", licensing.Denial(LicenseFeatures.Oee));
            }

            var (start, end) = Range(from, to);
            var report = oee.Build(start, end, deviceId, line);
            var rows = view == "day" ? report.Days : view == "line" ? report.Lines : report.Rows;
            return Results.File(Bom(oee.Csv(rows)), "text/csv; charset=utf-8", "oee.csv");
        });

        api.MapGet("/oee.xls", (string? deviceId, string? line, string? from, string? to, string? view, OeeService oee, LicenseService licensing) =>
        {
            if (!licensing.Allows(LicenseFeatures.Oee))
            {
                return ApiResults.Error(StatusCodes.Status403Forbidden, "license_feature", licensing.Denial(LicenseFeatures.Oee));
            }

            var (start, end) = Range(from, to);
            var report = oee.Build(start, end, deviceId, line);
            var rows = view == "day" ? report.Days : view == "line" ? report.Lines : report.Rows;
            return Results.File(oee.Excel(rows), "application/vnd.ms-excel", "oee.xls");
        });

        api.MapGet("/downtime", (string? deviceId, string? from, string? to, bool? openOnly, GatewayPersistence database) =>
        {
            var (start, end) = Range(from, to);
            return ApiResults.Ok(new
            {
                reasons = database.ListReasons(),
                events = database.ListDowntime(deviceId, string.IsNullOrWhiteSpace(from) && openOnly == true ? null : start, string.IsNullOrWhiteSpace(to) && openOnly == true ? null : end, openOnly == true, 300)
            });
        });

        api.MapPost("/downtime/assign", (AssignRequest? body, HttpContext http, GatewayPersistence database, LicenseService licensing) =>
            Assign(body?.EventIds, body, http, database, licensing, "operator"));

        api.MapPost("/downtime/bulk", (AssignRequest? body, HttpContext http, GatewayPersistence database, LicenseService licensing) =>
            Assign(body?.EventIds, body, http, database, licensing, "bulk"));

        api.MapPut("/oee/reasons/{id}", (string id, ReasonWrite? body, HttpContext http, GatewayPersistence database, LicenseService licensing) =>
        {
            var denied = Deny(licensing);
            if (denied is not null)
            {
                return denied;
            }

            if (body is null || string.IsNullOrWhiteSpace(body.Name) || string.IsNullOrWhiteSpace(body.Code))
            {
                return ApiResults.Error(StatusCodes.Status400BadRequest, "invalid_reason", "原因需要编码和名称");
            }

            var row = new DowntimeReasonRow
            {
                Id = string.IsNullOrWhiteSpace(id) ? Guid.NewGuid().ToString("N") : id.Trim(),
                ParentId = string.IsNullOrWhiteSpace(body.ParentId) ? null : body.ParentId.Trim(),
                Code = body.Code.Trim(),
                Name = body.Name.Trim(),
                Sort = body.Sort,
                Enabled = body.Enabled
            };
            database.SaveReason(row);
            ConfigAudit.Write(http, database, "oee.reason", row.Id, row.Name);
            return ApiResults.Ok(row);
        });

        api.MapDelete("/oee/reasons/{id}", (string id, HttpContext http, GatewayPersistence database, LicenseService licensing) =>
        {
            var denied = Deny(licensing);
            if (denied is not null)
            {
                return denied;
            }

            if (!database.DeleteReason(id))
            {
                return ApiResults.Error(StatusCodes.Status400BadRequest, "reason_in_use", "原因仍被引用或下面还有子项");
            }

            ConfigAudit.Write(http, database, "oee.reason.delete", id, "删除停机原因");
            return Results.NoContent();
        });

        api.MapPost("/oee/scrap", (ScrapWrite? body, HttpContext http, GatewayPersistence database, LicenseService licensing) =>
        {
            if (!licensing.Allows(LicenseFeatures.Oee))
            {
                return ApiResults.Error(StatusCodes.Status403Forbidden, "license_feature", licensing.Denial(LicenseFeatures.Oee));
            }

            if (body is null || string.IsNullOrWhiteSpace(body.DeviceId) || body.Quantity <= 0)
            {
                return ApiResults.Error(StatusCodes.Status400BadRequest, "invalid_scrap", "需要设备和大于 0 的报废数量");
            }

            var row = new ScrapEntryRow
            {
                Id = Guid.NewGuid().ToString("N"),
                DeviceId = body.DeviceId.Trim(),
                UnixMs = body.UnixMs > 0 ? body.UnixMs : DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                Quantity = body.Quantity,
                Note = (body.Note ?? "").Trim(),
                EnteredBy = http.Items["studio.user"] as string ?? "",
                Source = "manual"
            };
            database.AddScrap(row);
            ConfigAudit.Write(http, database, "oee.scrap", row.DeviceId, row.Quantity.ToString(System.Globalization.CultureInfo.InvariantCulture));
            return ApiResults.Ok(row);
        });

        api.MapGet("/oee/calendar", (GatewayPersistence database) => ApiResults.Ok(new
        {
            calendar = ShiftCalendar.ParseOrDefault(database.GetSetting("shiftCalendar")),
            planned = database.ListPlannedStops(),
            cycles = database.ListCycleTimes(),
            maps = database.ListStateMaps()
        }));

        api.MapPut("/oee/calendar", (CalendarWrite? body, HttpContext http, GatewayPersistence database, LicenseService licensing) =>
        {
            var denied = Deny(licensing);
            if (denied is not null)
            {
                return denied;
            }

            if (body?.Calendar is null)
            {
                return ApiResults.Error(StatusCodes.Status400BadRequest, "invalid_calendar", "需要班次");
            }

            var issues = body.Calendar.Validate();
            if (issues.Count > 0)
            {
                return ApiResults.Error(StatusCodes.Status400BadRequest, "invalid_calendar", string.Join("；", issues));
            }

            database.SetSetting("shiftCalendar", body.Calendar.ToJson());
            ConfigAudit.Write(http, database, "oee.calendar", "shifts", "更新班次、休息或节假日");
            return ApiResults.Ok(body.Calendar);
        });

        api.MapPut("/oee/planned/{id}", (string id, PlannedWrite? body, HttpContext http, GatewayPersistence database, LicenseService licensing) =>
        {
            var denied = Deny(licensing);
            if (denied is not null)
            {
                return denied;
            }

            if (body is null || string.IsNullOrWhiteSpace(body.OwnerId) || body.EndUnixMs <= body.StartUnixMs)
            {
                return ApiResults.Error(StatusCodes.Status400BadRequest, "invalid_planned", "计划停机需要对象和结束时间晚于开始时间");
            }

            var scope = string.Equals(body.Scope, "line", StringComparison.OrdinalIgnoreCase) ? "line" : "device";
            var row = new PlannedStopRow
            {
                Id = string.IsNullOrWhiteSpace(id) ? Guid.NewGuid().ToString("N") : id.Trim(),
                Scope = scope,
                OwnerId = body.OwnerId.Trim(),
                Name = string.IsNullOrWhiteSpace(body.Name) ? "计划停机" : body.Name.Trim(),
                StartUnixMs = body.StartUnixMs,
                EndUnixMs = body.EndUnixMs
            };
            database.SavePlannedStop(row);
            ConfigAudit.Write(http, database, "oee.planned", row.Id, row.Name);
            return ApiResults.Ok(row);
        });

        api.MapDelete("/oee/planned/{id}", (string id, HttpContext http, GatewayPersistence database, LicenseService licensing) =>
        {
            var denied = Deny(licensing);
            if (denied is not null)
            {
                return denied;
            }

            if (!database.DeletePlannedStop(id))
            {
                return ApiResults.Error(StatusCodes.Status404NotFound, "not_found", "计划停机不存在");
            }

            ConfigAudit.Write(http, database, "oee.planned.delete", id, "删除计划停机");
            return Results.NoContent();
        });

        api.MapPut("/oee/cycles/{id}", (string id, CycleWrite? body, HttpContext http, GatewayPersistence database, LicenseService licensing) =>
        {
            var denied = Deny(licensing);
            if (denied is not null)
            {
                return denied;
            }

            if (body is null || string.IsNullOrWhiteSpace(body.OwnerId) || body.IdealSeconds <= 0)
            {
                return ApiResults.Error(StatusCodes.Status400BadRequest, "invalid_cycle", "理想节拍需要设备和大于 0 的秒数");
            }

            var row = new CycleTimeRow
            {
                Id = string.IsNullOrWhiteSpace(id) ? Guid.NewGuid().ToString("N") : id.Trim(),
                Scope = "device",
                OwnerId = body.OwnerId.Trim(),
                Program = (body.Program ?? "").Trim(),
                IdealSeconds = body.IdealSeconds
            };
            database.SaveCycleTime(row);
            ConfigAudit.Write(http, database, "oee.cycle", row.Id, row.OwnerId + " " + row.Program);
            return ApiResults.Ok(row);
        });

        api.MapDelete("/oee/cycles/{id}", (string id, HttpContext http, GatewayPersistence database, LicenseService licensing) =>
        {
            var denied = Deny(licensing);
            if (denied is not null)
            {
                return denied;
            }

            if (!database.DeleteCycleTime(id))
            {
                return ApiResults.Error(StatusCodes.Status404NotFound, "not_found", "节拍不存在");
            }

            ConfigAudit.Write(http, database, "oee.cycle.delete", id, "删除理想节拍");
            return Results.NoContent();
        });

        api.MapPut("/oee/states/{id}", (string id, StateWrite? body, HttpContext http, GatewayPersistence database, LicenseService licensing) =>
        {
            var denied = Deny(licensing);
            if (denied is not null)
            {
                return denied;
            }

            if (body is null || string.IsNullOrWhiteSpace(body.OwnerId) || string.IsNullOrWhiteSpace(body.RawValue))
            {
                return ApiResults.Error(StatusCodes.Status400BadRequest, "invalid_state", "需要原始值和所属品牌、模板或设备");
            }

            var state = MachineState.Canonical(body.State);
            var row = new StateMapRow
            {
                Id = string.IsNullOrWhiteSpace(id) ? Guid.NewGuid().ToString("N") : id.Trim(),
                Scope = body.Scope is "device" or "template" or "brand" ? body.Scope : "brand",
                OwnerId = body.OwnerId.Trim(),
                RawValue = body.RawValue.Trim(),
                State = state
            };
            database.SaveStateMap(row);
            ConfigAudit.Write(http, database, "oee.state", row.Id, row.RawValue + " -> " + row.State);
            return ApiResults.Ok(row);
        });

        api.MapDelete("/oee/states/{id}", (string id, HttpContext http, GatewayPersistence database, LicenseService licensing) =>
        {
            var denied = Deny(licensing);
            if (denied is not null)
            {
                return denied;
            }

            if (!database.DeleteStateMap(id))
            {
                return ApiResults.Error(StatusCodes.Status404NotFound, "not_found", "状态映射不存在");
            }

            ConfigAudit.Write(http, database, "oee.state.delete", id, "删除状态映射");
            return Results.NoContent();
        });
    }

    private static IResult Assign(IReadOnlyList<string>? ids, AssignRequest? body, HttpContext http, GatewayPersistence database, LicenseService licensing, string source)
    {
        if (!licensing.Allows(LicenseFeatures.Oee))
        {
            return ApiResults.Error(StatusCodes.Status403Forbidden, "license_feature", licensing.Denial(LicenseFeatures.Oee));
        }

        var eventIds = ids?.Where(item => !string.IsNullOrWhiteSpace(item)).Select(item => item.Trim()).Distinct(StringComparer.Ordinal).ToList() ?? [];
        if (eventIds.Count == 0)
        {
            return ApiResults.Error(StatusCodes.Status400BadRequest, "invalid_assign", "需要选择停机记录");
        }

        var count = database.AssignDowntime(eventIds, body?.ReasonId, (body?.Note ?? "").Trim(), source, http.Items["studio.user"] as string ?? "", null);
        ConfigAudit.Write(http, database, source == "bulk" ? "downtime.bulk" : "downtime.assign", body?.ReasonId ?? "", count.ToString(System.Globalization.CultureInfo.InvariantCulture));
        return ApiResults.Ok(new { updated = count });
    }

    private static IResult? Deny(LicenseService licensing) =>
        licensing.Allows(LicenseFeatures.Oee)
            ? null
            : ApiResults.Error(StatusCodes.Status403Forbidden, "license_feature", licensing.Denial(LicenseFeatures.Oee));

    private static (long From, long To) Range(string? from, string? to)
    {
        var end = Parse(to, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
        var start = Parse(from, end - 86_400_000);
        return (start, end);
    }

    private static long Parse(string? text, long fallback)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return fallback;
        }

        if (long.TryParse(text, out var unix))
        {
            return unix;
        }

        return DateTimeOffset.TryParse(text, out var parsed) ? parsed.ToUnixTimeMilliseconds() : fallback;
    }

    private static byte[] Bom(string csv) => Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(csv)).ToArray();
}

public sealed class AssignRequest
{
    public List<string>? EventIds { get; set; }

    public string? ReasonId { get; set; }

    public string? Note { get; set; }
}

public sealed class ReasonWrite
{
    public string? ParentId { get; set; }

    public string? Code { get; set; }

    public string? Name { get; set; }

    public int Sort { get; set; }

    public bool Enabled { get; set; } = true;
}

public sealed class ScrapWrite
{
    public string? DeviceId { get; set; }

    public long UnixMs { get; set; }

    public double Quantity { get; set; }

    public string? Note { get; set; }
}

public sealed class CalendarWrite
{
    public ShiftCalendar? Calendar { get; set; }
}

public sealed class PlannedWrite
{
    public string? Scope { get; set; }

    public string? OwnerId { get; set; }

    public string? Name { get; set; }

    public long StartUnixMs { get; set; }

    public long EndUnixMs { get; set; }
}

public sealed class CycleWrite
{
    public string? OwnerId { get; set; }

    public string? Program { get; set; }

    public double IdealSeconds { get; set; }
}

public sealed class StateWrite
{
    public string? Scope { get; set; }

    public string? OwnerId { get; set; }

    public string? RawValue { get; set; }

    public string? State { get; set; }
}
