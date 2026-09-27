using System.Globalization;
using System.Text;
using IotDaq.Licensing;
using IotDaq.Persistence;
using IotDaq.Persistence.Shop;
using Studio.Host.Config;
using Studio.Host.Endpoints;
using Studio.Host.Licensing;

namespace Studio.Host.Shop;

public static class ToolEndpoints
{
    public static void Map(RouteGroupBuilder api)
    {
        api.MapGet("/tools", (string? deviceId, GatewayPersistence database, LicenseService licensing) =>
        {
            if (!licensing.Allows(LicenseFeatures.ToolLife))
            {
                return ApiResults.Ok(new
                {
                    licensed = false,
                    message = licensing.Denial(LicenseFeatures.ToolLife),
                    tools = Array.Empty<object>(),
                    pockets = Array.Empty<object>(),
                    life = Array.Empty<object>(),
                    changes = Array.Empty<object>(),
                    brands = TransferCatalog.DescribeAll()
                });
            }

            return ApiResults.Ok(new
            {
                licensed = true,
                tools = database.ListTools(),
                pockets = database.ListPockets(deviceId),
                life = database.ListToolLife(deviceId),
                changes = database.ListToolChanges(deviceId, 100),
                brands = TransferCatalog.DescribeAll()
            });
        });

        api.MapGet("/tools/report", (string? deviceId, GatewayPersistence database, LicenseService licensing) =>
        {
            if (!licensing.Allows(LicenseFeatures.ToolLife))
            {
                return ApiResults.Error(StatusCodes.Status403Forbidden, "license_feature", licensing.Denial(LicenseFeatures.ToolLife));
            }

            var builder = new StringBuilder();
            builder.AppendLine("deviceId,toolNumber,usedCount,usedCuttingMinutes,level,source");
            foreach (var row in database.ListToolLife(deviceId))
            {
                builder.Append(row.DeviceId).Append(',')
                    .Append(row.ToolNumber).Append(',')
                    .Append(row.UsedCount.ToString("0.###", CultureInfo.InvariantCulture)).Append(',')
                    .Append((row.UsedCuttingMs / 60000d).ToString("0.###", CultureInfo.InvariantCulture)).Append(',')
                    .Append(row.Level).Append(',')
                    .Append(row.Source)
                    .AppendLine();
            }

            return Results.Text(builder.ToString(), "text/csv; charset=utf-8");
        });

        api.MapPut("/tools/{id}", (string id, ToolWrite body, GatewayPersistence database, LicenseService licensing, HttpContext http) =>
        {
            if (!licensing.Allows(LicenseFeatures.ToolLife))
            {
                return ApiResults.Error(StatusCodes.Status403Forbidden, "license_feature", licensing.Denial(LicenseFeatures.ToolLife));
            }

            if (!ConfigValidator.IsSafeId(id) || string.IsNullOrWhiteSpace(body?.ToolNumber))
            {
                return ApiResults.Error(StatusCodes.Status400BadRequest, "tool_invalid", "刀号 Id 不合法。");
            }

            try
            {
                var saved = database.SaveTool(new ToolRow
                {
                    Id = id,
                    ToolNumber = body.ToolNumber.Trim(),
                    Description = body.Description?.Trim() ?? "",
                    LifeLimitCount = body.LifeLimitCount,
                    LifeLimitMinutes = body.LifeLimitMinutes,
                    WarningPercent = body.WarningPercent is > 0 and <= 100 ? body.WarningPercent.Value : 80,
                    Enabled = body.Enabled ?? true,
                    UpdatedBy = http.Items["studio.user"] as string ?? "",
                    UpdatedUnixMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
                });
                ConfigAudit.Write(http, database, "tool.upsert", id, saved.ToolNumber + " " + saved.Description);
                return ApiResults.Ok(saved);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                return ApiResults.Error(StatusCodes.Status409Conflict, "tool_conflict", "刀号已存在或无法保存。");
            }
        });

        api.MapDelete("/tools/{id}", (string id, GatewayPersistence database, LicenseService licensing, HttpContext http) =>
        {
            if (!licensing.Allows(LicenseFeatures.ToolLife))
            {
                return ApiResults.Error(StatusCodes.Status403Forbidden, "license_feature", licensing.Denial(LicenseFeatures.ToolLife));
            }

            if (!database.DeleteTool(id))
            {
                return ApiResults.Error(StatusCodes.Status404NotFound, "tool_missing", "刀具不存在。");
            }

            ConfigAudit.Write(http, database, "tool.delete", id, "删除刀具主数据");
            return Results.NoContent();
        });

        api.MapPut("/tools/pockets/{id}", (string id, PocketWrite body, GatewayPersistence database, LicenseService licensing, HttpContext http) =>
        {
            if (!licensing.Allows(LicenseFeatures.ToolLife))
            {
                return ApiResults.Error(StatusCodes.Status403Forbidden, "license_feature", licensing.Denial(LicenseFeatures.ToolLife));
            }

            if (string.IsNullOrWhiteSpace(body?.DeviceId) || string.IsNullOrWhiteSpace(body.ToolNumber) || body.Pocket < 1)
            {
                return ApiResults.Error(StatusCodes.Status400BadRequest, "pocket_invalid", "请填写设备、刀位和刀号。");
            }

            var saved = database.SavePocket(new ToolPocketRow
            {
                Id = string.IsNullOrWhiteSpace(id) ? Guid.NewGuid().ToString("N") : id,
                DeviceId = body.DeviceId.Trim(),
                Pocket = body.Pocket,
                ToolNumber = body.ToolNumber.Trim()
            });
            ConfigAudit.Write(http, database, "tool.pocket", saved.Id, saved.DeviceId + " #" + saved.Pocket.ToString(CultureInfo.InvariantCulture));
            return ApiResults.Ok(saved);
        });

        api.MapPost("/tools/change", (ToolChangeWrite? body, GatewayPersistence database, LicenseService licensing, HttpContext http) =>
        {
            if (!licensing.Allows(LicenseFeatures.ToolLife))
            {
                return ApiResults.Error(StatusCodes.Status403Forbidden, "license_feature", licensing.Denial(LicenseFeatures.ToolLife));
            }

            if (string.IsNullOrWhiteSpace(body?.DeviceId))
            {
                return ApiResults.Error(StatusCodes.Status400BadRequest, "tool_change_invalid", "请填写设备。");
            }

            var actor = http.Items["studio.user"] as string ?? "";
            var row = database.RecordToolChange(new ToolChangeRow
            {
                Id = Guid.NewGuid().ToString("N"),
                DeviceId = body.DeviceId.Trim(),
                Pocket = body.Pocket,
                OldToolNumber = body.OldToolNumber?.Trim() ?? "",
                NewToolNumber = body.NewToolNumber?.Trim() ?? "",
                Note = body.Note?.Trim() ?? "",
                Actor = actor,
                UnixMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
            }, body.ResetLife ?? true);
            ConfigAudit.Write(http, database, "tool.change", row.DeviceId, row.OldToolNumber + " -> " + row.NewToolNumber);
            return ApiResults.Ok(row);
        });

        api.MapPost("/tools/count", (ToolCountWrite? body, GatewayPersistence database, LicenseService licensing, HttpContext http) =>
        {
            if (!licensing.Allows(LicenseFeatures.ToolLife))
            {
                return ApiResults.Error(StatusCodes.Status403Forbidden, "license_feature", licensing.Denial(LicenseFeatures.ToolLife));
            }

            if (string.IsNullOrWhiteSpace(body?.DeviceId) || string.IsNullOrWhiteSpace(body.ToolNumber))
            {
                return ApiResults.Error(StatusCodes.Status400BadRequest, "tool_count_invalid", "请填写设备和刀号。");
            }

            try
            {
                var life = database.AddManualCount(body.DeviceId.Trim(), body.ToolNumber.Trim(), body.Count ?? 1, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
                ConfigAudit.Write(http, database, "tool.count", body.DeviceId, body.ToolNumber + " +" + (body.Count ?? 1).ToString(CultureInfo.InvariantCulture));
                return ApiResults.Ok(life);
            }
            catch (InvalidOperationException ex)
            {
                return ApiResults.Error(StatusCodes.Status400BadRequest, "tool_count_invalid", ex.Message);
            }
        });

        api.MapPost("/tools/tick", (string? deviceId, ToolLifeService tools, LicenseService licensing) =>
        {
            if (!licensing.Allows(LicenseFeatures.ToolLife))
            {
                return ApiResults.Error(StatusCodes.Status403Forbidden, "license_feature", licensing.Denial(LicenseFeatures.ToolLife));
            }

            return ApiResults.Ok(new { devices = tools.Tick(deviceId) });
        });
    }
}

public sealed class ToolWrite
{
    public string? ToolNumber { get; set; }

    public string? Description { get; set; }

    public double? LifeLimitCount { get; set; }

    public double? LifeLimitMinutes { get; set; }

    public double? WarningPercent { get; set; }

    public bool? Enabled { get; set; }
}

public sealed class PocketWrite
{
    public string? DeviceId { get; set; }

    public int Pocket { get; set; }

    public string? ToolNumber { get; set; }
}

public sealed class ToolChangeWrite
{
    public string? DeviceId { get; set; }

    public int? Pocket { get; set; }

    public string? OldToolNumber { get; set; }

    public string? NewToolNumber { get; set; }

    public string? Note { get; set; }

    public bool? ResetLife { get; set; }
}

public sealed class ToolCountWrite
{
    public string? DeviceId { get; set; }

    public string? ToolNumber { get; set; }

    public double? Count { get; set; }
}
