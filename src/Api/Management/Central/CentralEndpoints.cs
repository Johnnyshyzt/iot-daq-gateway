using System.Text.Json;
using IotDaq.Licensing;
using IotDaq.Persistence;
using Studio.Host.Config;
using Studio.Host.Endpoints;
using Studio.Host.Licensing;

namespace Studio.Host.Central;

public static class CentralEndpoints
{
    public static void Map(RouteGroupBuilder api)
    {
        api.MapGet("/central/status", (HostMode mode, LicenseService licensing) => ApiResults.Ok(new
        {
            mode = mode.Central ? "central" : "edge",
            licensed = licensing.Allows(LicenseFeatures.Central),
            message = licensing.Allows(LicenseFeatures.Central) ? "" : licensing.Denial(LicenseFeatures.Central)
        }));

        api.MapGet("/central/gateways", (GatewayPersistence database, HostMode mode, LicenseService licensing) =>
        {
            if (!licensing.Allows(LicenseFeatures.Central))
            {
                return ApiResults.Ok(new { licensed = false, message = licensing.Denial(LicenseFeatures.Central), gateways = Array.Empty<object>(), groups = Array.Empty<object>(), mode = mode.Central ? "central" : "edge" });
            }

            return ApiResults.Ok(new
            {
                licensed = true,
                mode = mode.Central ? "central" : "edge",
                gateways = database.ListFleetGateways(),
                groups = database.ListFleetGroups(),
                alerts = database.ListCentralAlerts(true)
            });
        });

        api.MapGet("/central/gateways/{id}", (string id, GatewayPersistence database, LicenseService licensing) =>
        {
            if (!licensing.Allows(LicenseFeatures.Central))
            {
                return ApiResults.Error(StatusCodes.Status403Forbidden, "license_feature", licensing.Denial(LicenseFeatures.Central));
            }

            var gateway = database.FindFleetGateway(id);
            return gateway is null
                ? ApiResults.Error(StatusCodes.Status404NotFound, "gateway_missing", "网关不存在。")
                : ApiResults.Ok(new
                {
                    gateway,
                    summary = Parse(gateway.SummaryJson),
                    note = "这是边缘最近一次主动心跳带来的只读摘要。中心不反向连接边缘，因此没有实时隧道。"
                });
        });

        api.MapPut("/central/groups/{id}", (string id, GroupName? body, GatewayPersistence database, HostMode mode, LicenseService licensing, HttpContext http) =>
            CentralWrite(mode, licensing, () =>
            {
                if (!ConfigValidator.IsSafeId(id))
                {
                    return ApiResults.Error(StatusCodes.Status400BadRequest, "id_invalid", "分组 Id 不合法。");
                }

                var saved = database.SaveFleetGroup(new FleetGroupRow { Id = id, Name = body?.Name ?? id });
                ConfigAudit.Write(http, database, "central.group", id, saved.Name);
                return ApiResults.Ok(saved);
            }));

        api.MapPut("/central/gateways/{id}/group", (string id, GroupAssign? body, GatewayPersistence database, HostMode mode, LicenseService licensing, HttpContext http) =>
            CentralWrite(mode, licensing, () =>
            {
                database.SetFleetGroup(id, body?.GroupId ?? "");
                ConfigAudit.Write(http, database, "central.group", id, body?.GroupId ?? "");
                return ApiResults.Ok(new { id, groupId = body?.GroupId ?? "" });
            }));

        api.MapGet("/central/tokens", (GatewayPersistence database, LicenseService licensing) =>
        {
            if (!licensing.Allows(LicenseFeatures.Central))
            {
                return ApiResults.Error(StatusCodes.Status403Forbidden, "license_feature", licensing.Denial(LicenseFeatures.Central));
            }

            return ApiResults.Ok(new { tokens = database.ListEnrollmentTokens().Select(row => new { row.Id, row.Label, row.ExpiresUnixMs, row.Uses, row.MaxUses, row.Revoked, row.CreatedBy }) });
        });

        api.MapPost("/central/tokens", (TokenWrite? body, CentralCoordinator central, HostMode mode, LicenseService licensing, HttpContext http) =>
            CentralWrite(mode, licensing, () =>
            {
                var (row, token) = central.CreateToken(body?.Label ?? "", body?.MaxUses ?? 1, body?.Days ?? 14, http.Items["studio.user"] as string ?? "");
                ConfigAudit.Write(http, http.RequestServices.GetRequiredService<GatewayPersistence>(), "central.token", row.Id, row.Label);
                return ApiResults.Ok(new { row.Id, row.Label, row.ExpiresUnixMs, row.MaxUses, token });
            }));

        api.MapDelete("/central/tokens/{id}", (string id, GatewayPersistence database, HostMode mode, LicenseService licensing, HttpContext http) =>
            CentralWrite(mode, licensing, () =>
            {
                if (!database.RevokeEnrollmentToken(id))
                {
                    return ApiResults.Error(StatusCodes.Status404NotFound, "token_missing", "令牌不存在。");
                }

                ConfigAudit.Write(http, database, "central.token", id, "吊销注册令牌");
                return Results.NoContent();
            }));

        api.MapGet("/central/templates", (GatewayPersistence database, LicenseService licensing) =>
            licensing.Allows(LicenseFeatures.Central)
                ? ApiResults.Ok(new { templates = database.ListTemplates().Select(row => new { row.Id, row.Key, row.Name, row.Kind, row.Version, row.Comment, row.CreatedBy, row.CreatedUnixMs, row.BodyJson }) })
                : ApiResults.Ok(new { licensed = false, message = licensing.Denial(LicenseFeatures.Central), templates = Array.Empty<object>() }));

        api.MapPut("/central/templates/{key}", (string key, TemplateWrite? body, CentralCoordinator central, HostMode mode, LicenseService licensing, HttpContext http) =>
            CentralWrite(mode, licensing, () =>
            {
                if (!ConfigValidator.IsSafeId(key) || string.IsNullOrWhiteSpace(body?.Body))
                {
                    return ApiResults.Error(StatusCodes.Status400BadRequest, "template_invalid", "请填写模板键和正文。");
                }

                var saved = central.SaveTemplate(key, body.Name ?? key, body.Kind ?? "rules", body.Body, body.Comment ?? "", http.Items["studio.user"] as string ?? "");
                return ApiResults.Ok(saved);
            }));

        api.MapGet("/central/templates/{key}/diff", (string key, int from, int to, GatewayPersistence database, LicenseService licensing) =>
        {
            if (!licensing.Allows(LicenseFeatures.Central))
            {
                return ApiResults.Error(StatusCodes.Status403Forbidden, "license_feature", licensing.Denial(LicenseFeatures.Central));
            }

            var left = database.FindTemplate(key, from)?.BodyJson ?? "";
            var right = database.FindTemplate(key, to)?.BodyJson ?? "";
            return ApiResults.Ok(new
            {
                from,
                to,
                diff = IotDaq.Persistence.Shop.TextDiff.Unified(left, right),
                lines = IotDaq.Persistence.Shop.TextDiff.Lines(left, right)
            });
        });

        api.MapPost("/central/pushes", (PushWrite? body, CentralCoordinator central, HostMode mode, LicenseService licensing, HttpContext http) =>
            CentralWrite(mode, licensing, () =>
            {
                var (push, error) = central.Push(body?.TemplateKey ?? "", body?.Version, body?.GroupId, body?.GatewayIds, body?.ConflictPolicy, http.Items["studio.user"] as string ?? "");
                return push is null
                    ? ApiResults.Error(StatusCodes.Status400BadRequest, "push_invalid", error ?? "无法下发")
                    : ApiResults.Ok(push);
            }));

        api.MapGet("/central/pushes", (GatewayPersistence database, LicenseService licensing) =>
            licensing.Allows(LicenseFeatures.Central)
                ? ApiResults.Ok(new { pushes = database.ListConfigPushes(50), targets = database.ListPushTargets(null, null, null) })
                : ApiResults.Error(StatusCodes.Status403Forbidden, "license_feature", licensing.Denial(LicenseFeatures.Central)));

        api.MapPost("/central/pushes/{id}/rollback", (string id, CentralCoordinator central, HostMode mode, LicenseService licensing, HttpContext http) =>
            CentralWrite(mode, licensing, () =>
            {
                var (push, error) = central.Rollback(id, http.Items["studio.user"] as string ?? "");
                return push is null
                    ? ApiResults.Error(StatusCodes.Status400BadRequest, "rollback_invalid", error ?? "无法回滚")
                    : ApiResults.Ok(push);
            }));

        api.MapPost("/central/rollouts", async (HttpRequest request, CentralCoordinator central, HostMode mode, LicenseService licensing, HttpContext http, CancellationToken cancellationToken) =>
        {
            var blocked = CentralWrite(mode, licensing, () => null);
            if (blocked is not null)
            {
                return blocked;
            }

            var gateways = (request.Query["gateways"].ToString() ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            using var memory = new MemoryStream();
            await request.Body.CopyToAsync(memory, cancellationToken);
            var (rollout, error) = central.StageRollout(memory.ToArray(), gateways, http.Items["studio.user"] as string ?? "");
            return rollout is null
                ? ApiResults.Error(StatusCodes.Status400BadRequest, "rollout_invalid", error ?? "无法暂存升级包")
                : ApiResults.Ok(new { rollout.Id, rollout.Version, rollout.Sha256, rollout.Status });
        });

        api.MapGet("/central/rollouts", (GatewayPersistence database, LicenseService licensing) =>
            licensing.Allows(LicenseFeatures.Central)
                ? ApiResults.Ok(new { rollouts = database.ListRollouts(), targets = database.ListRolloutTargets(null, null) })
                : ApiResults.Error(StatusCodes.Status403Forbidden, "license_feature", licensing.Denial(LicenseFeatures.Central)));

        api.MapPost("/central/scan", (CentralCoordinator central, HostMode mode, LicenseService licensing) =>
            CentralWrite(mode, licensing, () => ApiResults.Ok(new { raised = central.ScanOffline() })));

        api.MapGet("/central/alerts", (GatewayPersistence database, LicenseService licensing) =>
            licensing.Allows(LicenseFeatures.Central)
                ? ApiResults.Ok(new { alerts = database.ListCentralAlerts(false) })
                : ApiResults.Error(StatusCodes.Status403Forbidden, "license_feature", licensing.Denial(LicenseFeatures.Central)));

        api.MapGet("/central/audit", (int? limit, GatewayPersistence database, LicenseService licensing) =>
            licensing.Allows(LicenseFeatures.Central)
                ? ApiResults.Ok(new { events = database.ListCentralAudits(limit ?? 100) })
                : ApiResults.Error(StatusCodes.Status403Forbidden, "license_feature", licensing.Denial(LicenseFeatures.Central)));

        api.MapGet("/agent", (GatewayPersistence database, IConfiguration configuration) => ApiResults.Ok(new
        {
            url = configuration["Central:Url"] ?? "",
            gatewayId = database.GetSetting("central.gatewayId") ?? configuration["Central:GatewayId"] ?? "",
            enrolled = !string.IsNullOrWhiteSpace(database.GetSetting("central.session")),
            lastOkUnixMs = database.GetSetting("central.lastOkUnixMs"),
            documents = new[] { "device-class", "points", "rules", "notifications" }
                .Select(kind => database.FindCentralDocument(kind))
                .Where(row => row is not null)
        }));

        api.MapPost("/agent/pulse", async (CentralCoordinator central, CancellationToken cancellationToken) =>
            ApiResults.Ok(await central.PulseAsync(cancellationToken)));

        api.MapPost("/agent/dirty", (DirtyWrite? body, GatewayPersistence database, HttpContext http) =>
        {
            var kind = string.IsNullOrWhiteSpace(body?.Kind) ? "rules" : body.Kind.Trim();
            database.MarkCentralDocumentDirty(kind, true);
            ConfigAudit.Write(http, database, "central.dirty", kind, "标记本地修改，冲突策略为 local-wins 时中心不会覆盖。");
            return ApiResults.Ok(new { kind, dirty = true });
        });
    }

    public static void MapMachine(WebApplication app)
    {
        app.MapPost("/api/central/v1/enroll", (EnrollWrite? body, CentralCoordinator central) =>
        {
            var outcome = central.Enroll(body?.Token, body?.GatewayId, body?.Name);
            return outcome.Ok ? ApiResults.Ok(outcome) : ApiResults.Error(outcome.Status, outcome.Code, outcome.Message);
        });

        app.MapPost("/api/central/v1/heartbeat", (HeartbeatBody? body, HttpRequest request, CentralCoordinator central) =>
        {
            var outcome = central.Heartbeat(request.Headers.Authorization.ToString(), body);
            return outcome.Ok ? ApiResults.Ok(outcome) : ApiResults.Error(outcome.Status, outcome.Code, outcome.Message);
        });

        app.MapGet("/api/central/v1/rollouts/{id}/package", (string id, HttpRequest request, CentralCoordinator central) =>
        {
            var (bytes, error, status) = central.ReadPackage(id, request.Headers.Authorization.ToString());
            return bytes is null ? ApiResults.Error(status, "package_unavailable", error ?? "无法下载") : Results.File(bytes, "application/zip", id + ".zip");
        });
    }

    private static IResult? CentralWrite(HostMode mode, LicenseService licensing, Func<IResult?> action)
    {
        if (!licensing.Allows(LicenseFeatures.Central))
        {
            return ApiResults.Error(StatusCodes.Status403Forbidden, "license_feature", licensing.Denial(LicenseFeatures.Central));
        }

        if (!mode.Central)
        {
            return ApiResults.Error(StatusCodes.Status409Conflict, "not_central", "这个进程不是中心模式。请用 --central 启动。");
        }

        return action();
    }

    private static object? Parse(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<JsonElement>(json);
        }
        catch (JsonException)
        {
            return json;
        }
    }
}

public sealed class GroupName
{
    public string? Name { get; set; }
}

public sealed class GroupAssign
{
    public string? GroupId { get; set; }
}

public sealed class TokenWrite
{
    public string? Label { get; set; }

    public int? MaxUses { get; set; }

    public int? Days { get; set; }
}

public sealed class TemplateWrite
{
    public string? Name { get; set; }

    public string? Kind { get; set; }

    public string? Body { get; set; }

    public string? Comment { get; set; }
}

public sealed class PushWrite
{
    public string? TemplateKey { get; set; }

    public int? Version { get; set; }

    public string? GroupId { get; set; }

    public List<string>? GatewayIds { get; set; }

    public string? ConflictPolicy { get; set; }
}

public sealed class EnrollWrite
{
    public string? Token { get; set; }

    public string? GatewayId { get; set; }

    public string? Name { get; set; }
}

public sealed class DirtyWrite
{
    public string? Kind { get; set; }
}
