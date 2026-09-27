using System.Globalization;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using IotDaq.Licensing;
using IotDaq.Persistence;
using IotDaq.Persistence.Rules;
using IotDaq.Persistence.Shop;
using Studio.Host.Config;
using Studio.Host.Licensing;

namespace Studio.Host.Central;

public sealed class CentralCoordinator(
    GatewayPersistence database,
    ConfigStore store,
    LicenseService licensing,
    HostMode mode,
    IConfiguration configuration,
    ICentralTransport transport,
    ILogger<CentralCoordinator> logger)
{
    public bool IsCentral => mode.Central;

    public void EnsureBootstrap()
    {
        var token = configuration["Central:BootstrapToken"];
        if (string.IsNullOrWhiteSpace(token))
        {
            return;
        }

        var hash = CentralSession.Hash(token.Trim());
        if (database.ListEnrollmentTokens().Any(row => row.TokenHash == hash))
        {
            return;
        }

        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        database.AddEnrollmentToken(new EnrollmentTokenRow
        {
            Id = "bootstrap",
            TokenHash = hash,
            Label = "引导令牌",
            ExpiresUnixMs = now + (long)TimeSpan.FromDays(365).TotalMilliseconds,
            MaxUses = 20,
            CreatedBy = "system",
            CreatedUnixMs = now
        });
    }

    public (EnrollmentTokenRow Row, string Token) CreateToken(string label, int maxUses, int days, string actor)
    {
        var token = Convert.ToHexString(RandomNumberGenerator.GetBytes(24)).ToLowerInvariant();
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var row = database.AddEnrollmentToken(new EnrollmentTokenRow
        {
            Id = Guid.NewGuid().ToString("N"),
            TokenHash = CentralSession.Hash(token),
            Label = string.IsNullOrWhiteSpace(label) ? "注册令牌" : label.Trim(),
            ExpiresUnixMs = now + (long)TimeSpan.FromDays(Math.Clamp(days, 1, 365)).TotalMilliseconds,
            MaxUses = Math.Clamp(maxUses, 1, 100),
            CreatedBy = actor,
            CreatedUnixMs = now
        });
        return (row, token);
    }

    public EnrollOutcome Enroll(string? token, string? gatewayId, string? name)
    {
        if (!licensing.Allows(LicenseFeatures.Central))
        {
            return EnrollOutcome.Fail(StatusCodes.Status403Forbidden, "license_feature", licensing.Denial(LicenseFeatures.Central));
        }

        if (!mode.Central)
        {
            return EnrollOutcome.Fail(StatusCodes.Status409Conflict, "not_central", "这个进程不是中心模式。");
        }

        if (string.IsNullOrWhiteSpace(token) || string.IsNullOrWhiteSpace(gatewayId) || !ConfigValidator.IsSafeId(gatewayId))
        {
            return EnrollOutcome.Fail(StatusCodes.Status400BadRequest, "enroll_invalid", "需要注册令牌和合法的网关 Id。");
        }

        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var row = database.TakeEnrollmentToken(CentralSession.Hash(token.Trim()), now);
        if (row is null)
        {
            return EnrollOutcome.Fail(StatusCodes.Status401Unauthorized, "enroll_rejected", "注册令牌无效、过期或已用完。");
        }

        var exp = now + (long)TimeSpan.FromDays(30).TotalMilliseconds;
        var session = CentralSession.Issue(HmacKey(), gatewayId.Trim(), exp);
        database.UpsertFleetGateway(new FleetGatewayRow
        {
            Id = gatewayId.Trim(),
            Name = string.IsNullOrWhiteSpace(name) ? gatewayId.Trim() : name.Trim(),
            SessionHash = CentralSession.Hash(session),
            Status = "online",
            LastSeenUnixMs = now,
            EnrolledUnixMs = now
        });
        database.AppendAudit("central", "system", "central.enroll", gatewayId, row.Label);
        return new EnrollOutcome { Ok = true, GatewayId = gatewayId.Trim(), SessionToken = session, ExpiresUnixMs = exp };
    }

    public HeartbeatOutcome Heartbeat(string? authorization, HeartbeatBody? body)
    {
        if (!licensing.Allows(LicenseFeatures.Central))
        {
            return HeartbeatOutcome.Fail(StatusCodes.Status403Forbidden, "license_feature", licensing.Denial(LicenseFeatures.Central));
        }

        if (!mode.Central)
        {
            return HeartbeatOutcome.Fail(StatusCodes.Status409Conflict, "not_central", "这个进程不是中心模式。");
        }

        var token = Bearer(authorization);
        if (!CentralSession.TryParse(HmacKey(), token, out var gatewayId, out var exp) || exp < DateTimeOffset.UtcNow.ToUnixTimeMilliseconds())
        {
            return HeartbeatOutcome.Fail(StatusCodes.Status401Unauthorized, "session_invalid", "心跳令牌无效或已过期。请重新注册。");
        }

        var gateway = database.FindFleetGatewayBySession(CentralSession.Hash(token!));
        if (gateway is null || !string.Equals(gateway.Id, gatewayId, StringComparison.Ordinal))
        {
            return HeartbeatOutcome.Fail(StatusCodes.Status401Unauthorized, "session_invalid", "心跳令牌与已注册网关不一致。");
        }

        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        database.TouchFleetSeen(gatewayId, new FleetGatewayRow
        {
            Name = body?.Name ?? "",
            Version = body?.Version ?? "",
            LicenseSummary = body?.LicenseSummary ?? "",
            DeviceCount = body?.DeviceCount ?? 0,
            OnlineLinks = body?.OnlineLinks ?? 0,
            OfflineLinks = body?.OfflineLinks ?? 0,
            WorkingSetMb = body?.WorkingSetMb,
            SummaryJson = body?.SummaryJson ?? ""
        }, now);
        if (body?.Audit is { Count: > 0 })
        {
            database.AddCentralAudits(body.Audit.Take(50).Select(item => new CentralAuditRow
            {
                GatewayId = gatewayId,
                Username = item.Username ?? "",
                Role = item.Role ?? "",
                Action = item.Action ?? "",
                Target = item.Target ?? "",
                Detail = item.Detail ?? "",
                UnixMs = item.UnixMs
            }).ToList());
        }

        ApplyReports(gatewayId, body, now);
        return new HeartbeatOutcome
        {
            Ok = true,
            GatewayId = gatewayId,
            Pushes = PendingPushes(gatewayId),
            Rollouts = PendingRollouts(gatewayId)
        };
    }

    public IReadOnlyList<CentralAlertRow> ScanOffline()
    {
        var seconds = 45;
        if (int.TryParse(configuration["Central:OfflineAfterSeconds"], NumberStyles.Integer, CultureInfo.InvariantCulture, out var configured))
        {
            seconds = Math.Clamp(configured, 1, 86_400);
        }

        var raised = database.ScanOfflineGateways(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), seconds * 1000L);
        foreach (var alert in raised)
        {
            database.AppendAudit("central", "system", "central.offline", alert.GatewayId, alert.Message);
            logger.LogWarning("Gateway offline {GatewayId}", alert.GatewayId);
        }

        return raised;
    }

    public CentralTemplateRow SaveTemplate(string key, string name, string kind, string body, string comment, string actor)
    {
        var saved = database.AddTemplateVersion(new CentralTemplateRow
        {
            Key = key,
            Name = string.IsNullOrWhiteSpace(name) ? key : name.Trim(),
            Kind = string.IsNullOrWhiteSpace(kind) ? "rules" : kind.Trim(),
            BodyJson = body,
            Comment = comment,
            CreatedBy = actor,
            CreatedUnixMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
        });
        database.AppendAudit(actor, "", "central.template", key, "v" + saved.Version.ToString(CultureInfo.InvariantCulture));
        return saved;
    }

    public (ConfigPushRow? Push, string? Error) Push(string templateKey, int? version, string? groupId, IReadOnlyList<string>? gatewayIds, string? conflictPolicy, string actor)
    {
        var templates = database.ListTemplates().Where(row => row.Key == templateKey).OrderBy(row => row.Version).ToList();
        var chosen = version is int number
            ? templates.FirstOrDefault(row => row.Version == number)
            : templates.LastOrDefault();
        if (chosen is null)
        {
            return (null, "模板不存在。");
        }

        var targets = ResolveTargets(groupId, gatewayIds);
        if (targets.Count == 0)
        {
            return (null, "没有要下发的网关。");
        }

        var previous = templates.LastOrDefault(row => row.Version < chosen.Version);
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var push = database.AddConfigPush(new ConfigPushRow
        {
            Id = Guid.NewGuid().ToString("N"),
            TemplateKey = chosen.Key,
            Version = chosen.Version,
            PreviousVersion = previous?.Version,
            GroupId = groupId ?? "",
            DiffText = TextDiff.Unified(previous?.BodyJson ?? "", chosen.BodyJson),
            ConflictPolicy = conflictPolicy is "local-wins" ? "local-wins" : "central-wins",
            Status = "pending",
            CreatedBy = actor,
            CreatedUnixMs = now
        }, targets);
        database.AppendAudit(actor, "", "central.push", chosen.Key, "v" + chosen.Version.ToString(CultureInfo.InvariantCulture));
        return (push, null);
    }

    public (ConfigPushRow? Push, string? Error) Rollback(string pushId, string actor)
    {
        var push = database.FindConfigPush(pushId);
        if (push is null)
        {
            return (null, "下发记录不存在。");
        }

        if (push.PreviousVersion is not int previousVersion)
        {
            return (null, "没有上一版可以回滚。");
        }

        var current = database.FindTemplate(push.TemplateKey, push.Version);
        var previous = database.FindTemplate(push.TemplateKey, previousVersion);
        if (previous is null)
        {
            return (null, "上一版模板不存在。");
        }

        List<string> ids;
        try
        {
            ids = JsonSerializer.Deserialize<List<string>>(push.GatewayIdsJson) ?? [];
        }
        catch (JsonException)
        {
            ids = [];
        }

        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var created = database.AddConfigPush(new ConfigPushRow
        {
            Id = Guid.NewGuid().ToString("N"),
            TemplateKey = push.TemplateKey,
            Version = previous.Version,
            PreviousVersion = push.Version,
            GroupId = push.GroupId,
            DiffText = TextDiff.Unified(current?.BodyJson ?? "", previous.BodyJson),
            ConflictPolicy = push.ConflictPolicy,
            Status = "pending",
            Rollback = true,
            CreatedBy = actor,
            CreatedUnixMs = now
        }, ids);
        database.AppendAudit(actor, "", "central.rollback", push.TemplateKey, "回到 v" + previous.Version.ToString(CultureInfo.InvariantCulture));
        return (created, null);
    }

    public (RolloutRow? Rollout, string? Error) StageRollout(byte[] zip, IReadOnlyList<string> gatewayIds, string actor)
    {
        if (gatewayIds.Count == 0)
        {
            return (null, "请选择网关。");
        }

        using var stream = new MemoryStream(zip);
        using var key = LicenseCrypto.CreatePublic(licensing.Options.PublicKeySpki);
        var inspection = UpgradePackage.Inspect(stream, key);
        if (!inspection.Ok)
        {
            return (null, inspection.Message);
        }

        var id = Guid.NewGuid().ToString("N");
        var directory = Path.Combine(store.DataDirectory, "central", "rollouts");
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, id + ".zip");
        File.WriteAllBytes(path, zip);
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var row = database.AddRollout(new RolloutRow
        {
            Id = id,
            Version = inspection.Version,
            Sha256 = Convert.ToHexString(SHA256.HashData(zip)).ToLowerInvariant(),
            PackagePath = path,
            Status = "rolling",
            CreatedBy = actor,
            CreatedUnixMs = now
        }, gatewayIds);
        database.AppendAudit(actor, "", "central.rollout", id, inspection.Version);
        return (row, null);
    }

    public (byte[]? Bytes, string? Error, int Status) ReadPackage(string rolloutId, string? authorization)
    {
        var token = Bearer(authorization);
        if (!CentralSession.TryParse(HmacKey(), token, out var gatewayId, out var exp) || exp < DateTimeOffset.UtcNow.ToUnixTimeMilliseconds())
        {
            return (null, "心跳令牌无效。", StatusCodes.Status401Unauthorized);
        }

        var gateway = database.FindFleetGatewayBySession(CentralSession.Hash(token!));
        if (gateway is null || gateway.Id != gatewayId)
        {
            return (null, "心跳令牌无效。", StatusCodes.Status401Unauthorized);
        }

        var target = database.ListRolloutTargets(rolloutId, gatewayId).FirstOrDefault();
        var rollout = database.FindRollout(rolloutId);
        if (target is null || rollout is null || !File.Exists(rollout.PackagePath))
        {
            return (null, "没有分配给这台网关的升级包。", StatusCodes.Status404NotFound);
        }

        return (File.ReadAllBytes(rollout.PackagePath), null, StatusCodes.Status200OK);
    }

    public async Task<PulseOutcome> PulseAsync(CancellationToken cancellationToken)
    {
        if (mode.Central || string.IsNullOrWhiteSpace(configuration["Central:Url"]))
        {
            return new PulseOutcome { Skipped = true, Message = "未连接中心，边缘继续独立运行。" };
        }

        try
        {
            var session = database.GetSetting("central.session");
            if (string.IsNullOrWhiteSpace(session))
            {
                var enrolled = await EnrollFromEdgeAsync(cancellationToken).ConfigureAwait(false);
                if (!enrolled.Ok)
                {
                    return new PulseOutcome { Ok = false, Message = enrolled.Message };
                }

                session = enrolled.SessionToken;
            }

            var first = await PostHeartbeatAsync(session!, BuildHeartbeat(null, null), cancellationToken).ConfigureAwait(false);
            if (!first.Ok && first.Code == "session_invalid")
            {
                database.SetSetting("central.session", "");
                var enrolled = await EnrollFromEdgeAsync(cancellationToken).ConfigureAwait(false);
                if (!enrolled.Ok)
                {
                    return new PulseOutcome { Ok = false, Message = enrolled.Message };
                }

                session = enrolled.SessionToken;
                first = await PostHeartbeatAsync(session!, BuildHeartbeat(null, null), cancellationToken).ConfigureAwait(false);
            }

            if (!first.Ok)
            {
                return new PulseOutcome { Ok = false, Message = first.Message };
            }

            var pushResults = ApplyPushes(first.Pushes);
            var rolloutResults = await ApplyRolloutsAsync(session!, first.Rollouts, cancellationToken).ConfigureAwait(false);
            if (pushResults.Count > 0 || rolloutResults.Count > 0)
            {
                await PostHeartbeatAsync(session!, BuildHeartbeat(pushResults, rolloutResults), cancellationToken).ConfigureAwait(false);
            }

            database.SetSetting("central.lastOkUnixMs", DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString(CultureInfo.InvariantCulture));
            return new PulseOutcome { Ok = true, Message = "已向中心心跳。", Applied = pushResults.Count, Rollouts = rolloutResults.Count };
        }
        catch (Exception ex) when (ex is HttpRequestException or InvalidOperationException or TaskCanceledException)
        {
            logger.LogInformation("Central is unreachable, edge keeps running: {Message}", ex.Message);
            return new PulseOutcome { Ok = false, Message = "中心不可达，边缘继续独立运行。" };
        }
    }

    public string Diff(string key, int from, int to)
    {
        var left = database.FindTemplate(key, from)?.BodyJson ?? "";
        var right = database.FindTemplate(key, to)?.BodyJson ?? "";
        return TextDiff.Unified(left, right);
    }

    private List<PushCommand> ApplyPushes(IReadOnlyList<PushCommand> pushes)
    {
        var results = new List<PushCommand>();
        foreach (var push in pushes)
        {
            var dirty = database.FindCentralDocument(push.Kind)?.Dirty == true
                || (push.Kind == "rules" && database.CentralRulesDirty());
            if (dirty && push.ConflictPolicy == "local-wins")
            {
                results.Add(new PushCommand { PushId = push.PushId, Status = "conflict", Message = "本地改过中心管理字段，按 local-wins 保留。" });
                continue;
            }

            if (push.Kind == "rules" && !TryMaterializeRules(push.Body, out var error, write: true))
            {
                results.Add(new PushCommand { PushId = push.PushId, Status = "failed", Message = error });
                continue;
            }

            database.SaveCentralDocument(new CentralDocumentRow
            {
                Kind = push.Kind,
                TemplateKey = push.TemplateKey,
                Version = push.Version,
                BodyJson = push.Body,
                Dirty = false,
                Conflict = "",
                UpdatedUnixMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
            });
            database.AppendAudit("central", "system", "central.apply", push.TemplateKey, "v" + push.Version.ToString(CultureInfo.InvariantCulture));
            results.Add(new PushCommand { PushId = push.PushId, Status = "applied", Message = "已应用 v" + push.Version.ToString(CultureInfo.InvariantCulture) });
        }

        return results;
    }

    private async Task<List<PushCommand>> ApplyRolloutsAsync(string session, IReadOnlyList<PushCommand> rollouts, CancellationToken cancellationToken)
    {
        var results = new List<PushCommand>();
        foreach (var rollout in rollouts)
        {
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, "api/central/v1/rollouts/" + rollout.RolloutId + "/package");
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", session);
                using var response = await transport.SendAsync(request, cancellationToken).ConfigureAwait(false);
                var bytes = await response.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
                if (!response.IsSuccessStatusCode)
                {
                    results.Add(new PushCommand { RolloutId = rollout.RolloutId, Status = "failed", Message = "下载升级包失败。" });
                    continue;
                }

                using var stream = new MemoryStream(bytes);
                var inspection = UpgradeCoordinator.Stage(stream, store.DataDirectory, database, licensing.Options.PublicKeySpki);
                results.Add(inspection.Ok
                    ? new PushCommand { RolloutId = rollout.RolloutId, Status = "staged", Message = "已校验并暂存 " + inspection.Version + "。重启后由升级脚本替换程序。" }
                    : new PushCommand { RolloutId = rollout.RolloutId, Status = "failed", Message = inspection.Message });
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                results.Add(new PushCommand { RolloutId = rollout.RolloutId, Status = "failed", Message = ex.Message });
            }
        }

        return results;
    }

    private static bool TryMaterializeRules(string body, out string error)
    {
        error = "";
        RulesDocument? document;
        try
        {
            document = JsonSerializer.Deserialize<RulesDocument>(body, StudioJson.Options);
        }
        catch (JsonException ex)
        {
            error = ex.Message;
            return false;
        }

        if (document?.Rules is null)
        {
            return true;
        }

        foreach (var rule in document.Rules)
        {
            var compiled = ExpressionEngine.Compile(rule.Expression ?? "");
            if (!compiled.Ok)
            {
                error = compiled.Error ?? "规则表达式无效";
                return false;
            }
        }

        return true;
    }

    private bool TryMaterializeRules(string body, out string error, bool write)
    {
        if (!TryMaterializeRules(body, out error))
        {
            return false;
        }

        if (!write)
        {
            return true;
        }

        var document = JsonSerializer.Deserialize<RulesDocument>(body, StudioJson.Options);
        var rules = (document?.Rules ?? []).Select(rule => new EdgeRuleRow
        {
            Id = string.IsNullOrWhiteSpace(rule.Id) ? "central-" + Guid.NewGuid().ToString("N") : rule.Id!,
            Name = rule.Name ?? "中心规则",
            Enabled = rule.Enabled,
            Scope = string.IsNullOrWhiteSpace(rule.Scope) ? "all" : rule.Scope!,
            Expression = rule.Expression ?? "",
            DurationMs = rule.DurationMs,
            DebounceMs = rule.DebounceMs,
            ActionsJson = string.IsNullOrWhiteSpace(rule.ActionsJson) ? "[]" : rule.ActionsJson!
        }).ToList();
        database.ReplaceCentralRules(rules, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
        return true;
    }

    private async Task<EnrollOutcome> EnrollFromEdgeAsync(CancellationToken cancellationToken)
    {
        var token = configuration["Central:EnrollmentToken"];
        var gatewayId = configuration["Central:GatewayId"];
        if (string.IsNullOrWhiteSpace(token) || string.IsNullOrWhiteSpace(gatewayId))
        {
            return EnrollOutcome.Fail(StatusCodes.Status400BadRequest, "enroll_missing", "边缘未配置注册令牌。采集不受影响。");
        }

        var payload = JsonSerializer.Serialize(new
        {
            token,
            gatewayId,
            name = configuration["Central:Name"] ?? gatewayId
        }, StudioJson.Options);
        using var request = new HttpRequestMessage(HttpMethod.Post, "api/central/v1/enroll")
        {
            Content = new StringContent(payload, Encoding.UTF8, "application/json")
        };
        using var response = await transport.SendAsync(request, cancellationToken).ConfigureAwait(false);
        var json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            return EnrollOutcome.Fail((int)response.StatusCode, "enroll_failed", json);
        }

        var body = JsonSerializer.Deserialize<EnrollOutcome>(json, StudioJson.Options);
        if (string.IsNullOrWhiteSpace(body?.SessionToken))
        {
            return EnrollOutcome.Fail(StatusCodes.Status502BadGateway, "enroll_failed", "中心没有返回会话令牌。");
        }

        database.SetSetting("central.session", body.SessionToken);
        database.SetSetting("central.gatewayId", gatewayId);
        return body;
    }

    private async Task<HeartbeatOutcome> PostHeartbeatAsync(string session, HeartbeatBody body, CancellationToken cancellationToken)
    {
        var payload = JsonSerializer.Serialize(body, StudioJson.Options);
        using var request = new HttpRequestMessage(HttpMethod.Post, "api/central/v1/heartbeat")
        {
            Content = new StringContent(payload, Encoding.UTF8, "application/json")
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", session);
        using var response = await transport.SendAsync(request, cancellationToken).ConfigureAwait(false);
        var json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            var code = json.Contains("session_invalid", StringComparison.Ordinal) ? "session_invalid" : "heartbeat_failed";
            return HeartbeatOutcome.Fail((int)response.StatusCode, code, json);
        }

        return JsonSerializer.Deserialize<HeartbeatOutcome>(json, StudioJson.Options) ?? new HeartbeatOutcome { Ok = true };
    }

    private HeartbeatBody BuildHeartbeat(List<PushCommand>? pushResults, List<PushCommand>? rolloutResults)
    {
        var published = store.ReadPublished();
        var links = database.ListLinkStatus();
        var devices = published.Devices.Take(12).Select(device =>
        {
            var latest = database.Latest(device.Metadata.Id);
            string? Text(string point) => latest.FirstOrDefault(sample => sample.PointId.Equals(point, StringComparison.OrdinalIgnoreCase))?.Value;
            return new
            {
                id = device.Metadata.Id,
                name = device.Metadata.DisplayName,
                state = Text("state"),
                program = Text("program"),
                alarm = Text("alarm")
            };
        }).ToList();
        var evaluation = licensing.Evaluate();
        var cursorText = database.GetSetting("central.auditCursor");
        var cursor = long.TryParse(cursorText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) ? parsed : 0L;
        var audits = database.ListAudit(40).Where(row => row.UnixMs > cursor).OrderBy(row => row.UnixMs).Take(40).ToList();
        if (audits.Count > 0)
        {
            database.SetSetting("central.auditCursor", audits[^1].UnixMs.ToString(CultureInfo.InvariantCulture));
        }

        return new HeartbeatBody
        {
            Name = configuration["Central:Name"] ?? Environment.MachineName,
            Version = typeof(CentralCoordinator).Assembly.GetName().Version?.ToString(3) ?? "",
            LicenseSummary = evaluation.Edition + "/" + evaluation.State,
            DeviceCount = published.Devices.Count,
            OnlineLinks = links.Count(row => row.Status.Equals("online", StringComparison.OrdinalIgnoreCase)),
            OfflineLinks = links.Count(row => !row.Status.Equals("online", StringComparison.OrdinalIgnoreCase)),
            WorkingSetMb = Math.Round(Environment.WorkingSet / 1024d / 1024d, 1),
            SummaryJson = JsonSerializer.Serialize(new { devices }, StudioJson.Options),
            Audit = audits.Select(row => new AuditItem
            {
                Username = row.Username,
                Role = row.Role,
                Action = row.Action,
                Target = row.Target,
                Detail = row.Detail,
                UnixMs = row.UnixMs
            }).ToList(),
            PushResults = pushResults,
            RolloutResults = rolloutResults
        };
    }

    private List<string> ResolveTargets(string? groupId, IReadOnlyList<string>? gatewayIds)
    {
        if (gatewayIds is { Count: > 0 })
        {
            return gatewayIds.Where(id => !string.IsNullOrWhiteSpace(id)).Distinct(StringComparer.Ordinal).ToList();
        }

        if (string.IsNullOrWhiteSpace(groupId))
        {
            return [];
        }

        return database.ListFleetGateways().Where(row => row.GroupId == groupId).Select(row => row.Id).ToList();
    }

    private List<PushCommand> PendingPushes(string gatewayId)
    {
        var pending = database.ListPushTargets(null, gatewayId, "pending");
        var commands = new List<PushCommand>();
        foreach (var target in pending)
        {
            var push = database.FindConfigPush(target.PushId);
            var template = push is null ? null : database.FindTemplate(push.TemplateKey, push.Version);
            if (push is null || template is null)
            {
                continue;
            }

            commands.Add(new PushCommand
            {
                PushId = push.Id,
                TemplateKey = push.TemplateKey,
                Kind = template.Kind,
                Version = push.Version,
                Body = template.BodyJson,
                ConflictPolicy = push.ConflictPolicy,
                Diff = push.DiffText
            });
        }

        return commands;
    }

    private List<PushCommand> PendingRollouts(string gatewayId) =>
        database.ListRolloutTargets(null, gatewayId)
            .Where(row => row.Status == "pending")
            .Select(row => new PushCommand
            {
                RolloutId = row.RolloutId,
                PackageVersion = database.FindRollout(row.RolloutId)?.Version ?? ""
            })
            .ToList();

    private void ApplyReports(string gatewayId, HeartbeatBody? body, long now)
    {
        foreach (var result in body?.PushResults ?? [])
        {
            if (!string.IsNullOrWhiteSpace(result.PushId))
            {
                database.SetPushTarget(result.PushId, gatewayId, result.Status ?? "failed", result.Message ?? "", now);
            }
        }

        foreach (var result in body?.RolloutResults ?? [])
        {
            if (!string.IsNullOrWhiteSpace(result.RolloutId))
            {
                database.SetRolloutTarget(result.RolloutId, gatewayId, result.Status ?? "failed", result.Message ?? "", now);
            }
        }
    }

    private string HmacKey()
    {
        var key = database.GetSetting("central.hmac");
        if (!string.IsNullOrWhiteSpace(key))
        {
            return key;
        }

        key = Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();
        database.SetSetting("central.hmac", key);
        return key;
    }

    private static string? Bearer(string? authorization)
    {
        const string prefix = "Bearer ";
        if (string.IsNullOrWhiteSpace(authorization) || !authorization.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return authorization[prefix.Length..].Trim();
    }
}

public sealed class EnrollOutcome
{
    public bool Ok { get; set; }

    public int Status { get; set; }

    public string Code { get; set; } = "";

    public string Message { get; set; } = "";

    public string GatewayId { get; set; } = "";

    public string? SessionToken { get; set; }

    public long ExpiresUnixMs { get; set; }

    public static EnrollOutcome Fail(int status, string code, string message) =>
        new() { Status = status, Code = code, Message = message };
}

public sealed class HeartbeatOutcome
{
    public bool Ok { get; set; }

    public int Status { get; set; }

    public string Code { get; set; } = "";

    public string Message { get; set; } = "";

    public string GatewayId { get; set; } = "";

    public List<PushCommand> Pushes { get; set; } = [];

    public List<PushCommand> Rollouts { get; set; } = [];

    public static HeartbeatOutcome Fail(int status, string code, string message) =>
        new() { Status = status, Code = code, Message = message };
}

public sealed class HeartbeatBody
{
    public string? Name { get; set; }

    public string? Version { get; set; }

    public string? LicenseSummary { get; set; }

    public int DeviceCount { get; set; }

    public int OnlineLinks { get; set; }

    public int OfflineLinks { get; set; }

    public double? WorkingSetMb { get; set; }

    public string? SummaryJson { get; set; }

    public List<AuditItem>? Audit { get; set; }

    public List<PushCommand>? PushResults { get; set; }

    public List<PushCommand>? RolloutResults { get; set; }
}

public sealed class AuditItem
{
    public string? Username { get; set; }

    public string? Role { get; set; }

    public string? Action { get; set; }

    public string? Target { get; set; }

    public string? Detail { get; set; }

    public long UnixMs { get; set; }
}

public sealed class PushCommand
{
    public string PushId { get; set; } = "";

    public string RolloutId { get; set; } = "";

    public string TemplateKey { get; set; } = "";

    public string Kind { get; set; } = "";

    public int Version { get; set; }

    public string Body { get; set; } = "";

    public string ConflictPolicy { get; set; } = "central-wins";

    public string Diff { get; set; } = "";

    public string? Status { get; set; }

    public string? Message { get; set; }

    public string PackageVersion { get; set; } = "";
}

public sealed class PulseOutcome
{
    public bool Ok { get; set; }

    public bool Skipped { get; set; }

    public string Message { get; set; } = "";

    public int Applied { get; set; }

    public int Rollouts { get; set; }
}

public sealed class RulesDocument
{
    public List<RuleDocument>? Rules { get; set; }
}

public sealed class RuleDocument
{
    public string? Id { get; set; }

    public string? Name { get; set; }

    public string? Expression { get; set; }

    public bool Enabled { get; set; } = true;

    public long DurationMs { get; set; }

    public long DebounceMs { get; set; }

    public string? Scope { get; set; }

    public string? ActionsJson { get; set; }
}
