using System.Globalization;
using System.Text.Json;
using IotDaq.Licensing;
using IotDaq.Persistence;
using IotDaq.Persistence.Rules;
using Studio.Host.Endpoints;
using Studio.Host.Licensing;

namespace Studio.Host.Rules;

public static class RuleEndpoints
{
    public static void Map(RouteGroupBuilder api)
    {
        api.MapGet("/computed", (GatewayPersistence database) =>
            ApiResults.Ok(new { points = database.ListComputedPoints(), functions = FunctionHelp() }));

        api.MapPut("/computed/{id}", (string id, ComputedWrite? body, HttpContext http, GatewayPersistence database, EdgePipeline pipeline) =>
        {
            var error = ValidateComputed(body);
            if (error is not null)
            {
                return ApiResults.Error(StatusCodes.Status400BadRequest, "invalid_computed", error);
            }

            var row = new ComputedPointRow
            {
                Id = string.IsNullOrWhiteSpace(id) ? Guid.NewGuid().ToString("N") : id.Trim(),
                Scope = body!.Scope!.Trim().ToLowerInvariant(),
                OwnerId = body.OwnerId!.Trim(),
                PointId = body.PointId!.Trim(),
                Name = body.Name!.Trim(),
                Unit = (body.Unit ?? "").Trim(),
                Expression = body.Expression!.Trim(),
                Enabled = body.Enabled,
                UpdatedUnixMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                UpdatedBy = http.Items["studio.user"] as string ?? ""
            };
            database.SaveComputedPoint(row);
            pipeline.Invalidate();
            ConfigAudit.Write(http, database, "computed.upsert", row.Id, row.PointId + " " + row.Expression);
            return ApiResults.Ok(row);
        });

        api.MapDelete("/computed/{id}", (string id, HttpContext http, GatewayPersistence database, EdgePipeline pipeline) =>
        {
            if (!database.DeleteComputedPoint(id))
            {
                return ApiResults.Error(StatusCodes.Status404NotFound, "not_found", "计算点不存在");
            }

            pipeline.Invalidate();
            ConfigAudit.Write(http, database, "computed.delete", id, "删除计算点");
            return Results.NoContent();
        });

        api.MapPost("/computed/preview", (PreviewRequest? body, GatewayPersistence database) =>
        {
            var program = ExpressionEngine.Compile(body?.Expression);
            if (!program.Ok)
            {
                return ApiResults.Ok(new { ok = false, error = program.Error, references = program.References });
            }

            var deviceId = (body?.DeviceId ?? "").Trim();
            var memory = new PointMemory();
            var numbers = new Dictionary<string, double?>(StringComparer.OrdinalIgnoreCase);
            var texts = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
            long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            if (deviceId.Length > 0)
            {
                var from = now - 60 * 60 * 1000;
                foreach (var frame in database.HistoryFrames(deviceId, program.References, from, now, 400))
                {
                    foreach (var pair in frame.Numbers)
                    {
                        if (pair.Value is double number)
                        {
                            memory.Observe(pair.Key, frame.UnixMs, number);
                            numbers[pair.Key] = number;
                        }

                        if (frame.Texts.TryGetValue(pair.Key, out var text))
                        {
                            texts[pair.Key] = text;
                        }
                    }

                    now = frame.UnixMs;
                }

                foreach (var sample in database.Latest(deviceId))
                {
                    if (!program.References.Contains(sample.PointId, StringComparer.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    numbers[sample.PointId] = sample.NumericValue;
                    texts[sample.PointId] = sample.Value;
                    if (sample.NumericValue is double number)
                    {
                        memory.Observe(sample.PointId, sample.TimestampUnixMs, number);
                        now = Math.Max(now, sample.TimestampUnixMs);
                    }
                }
            }

            var value = ExpressionEngine.Evaluate(program, new EvalContext
            {
                Memory = memory,
                Numbers = numbers,
                Texts = texts,
                NowMs = now,
                DurationScope = "preview"
            });
            return ApiResults.Ok(new
            {
                ok = value.Ok,
                value = value.Number,
                text = value.Text,
                error = value.Error,
                references = program.References
            });
        });

        api.MapGet("/rules", (GatewayPersistence database, LicenseService licensing) =>
            ApiResults.Ok(new
            {
                licensed = licensing.Allows(LicenseFeatures.Rules),
                rules = database.ListEdgeRules(),
                templates = RuleTemplates.All
            }));

        api.MapPut("/rules/{id}", (string id, RuleWrite? body, HttpContext http, GatewayPersistence database, LicenseService licensing, EdgePipeline pipeline) =>
        {
            if (!licensing.Allows(LicenseFeatures.Rules))
            {
                return ApiResults.Error(StatusCodes.Status403Forbidden, "license_feature", licensing.Denial(LicenseFeatures.Rules));
            }

            var error = ValidateRule(body);
            if (error is not null)
            {
                return ApiResults.Error(StatusCodes.Status400BadRequest, "invalid_rule", error);
            }

            var row = new EdgeRuleRow
            {
                Id = string.IsNullOrWhiteSpace(id) ? Guid.NewGuid().ToString("N") : id.Trim(),
                Name = body!.Name!.Trim(),
                Enabled = body.Enabled,
                Scope = string.IsNullOrWhiteSpace(body.Scope) ? "all" : body.Scope.Trim().ToLowerInvariant(),
                OwnerId = (body.OwnerId ?? "").Trim(),
                Expression = body.Expression!.Trim(),
                DurationMs = Math.Max(0, body.DurationMs),
                DebounceMs = Math.Max(0, body.DebounceMs),
                ActionsJson = body.ActionsJson ?? "[]",
                TemplateKey = body.TemplateKey ?? "",
                UpdatedUnixMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                UpdatedBy = http.Items["studio.user"] as string ?? ""
            };
            database.SaveEdgeRule(row);
            pipeline.Invalidate();
            ConfigAudit.Write(http, database, "rule.upsert", row.Id, row.Name);
            return ApiResults.Ok(row);
        });

        api.MapDelete("/rules/{id}", (string id, HttpContext http, GatewayPersistence database, LicenseService licensing, EdgePipeline pipeline) =>
        {
            if (!licensing.Allows(LicenseFeatures.Rules))
            {
                return ApiResults.Error(StatusCodes.Status403Forbidden, "license_feature", licensing.Denial(LicenseFeatures.Rules));
            }

            if (!database.DeleteEdgeRule(id))
            {
                return ApiResults.Error(StatusCodes.Status404NotFound, "not_found", "规则不存在");
            }

            pipeline.Invalidate();
            ConfigAudit.Write(http, database, "rule.delete", id, "删除规则");
            return Results.NoContent();
        });

        api.MapPost("/rules/backtest", (BacktestRequest? body, GatewayPersistence database, LicenseService licensing) =>
        {
            if (!licensing.Allows(LicenseFeatures.Rules))
            {
                return ApiResults.Error(StatusCodes.Status403Forbidden, "license_feature", licensing.Denial(LicenseFeatures.Rules));
            }

            var program = ExpressionEngine.Compile(body?.Expression);
            if (!program.Ok)
            {
                return ApiResults.Error(StatusCodes.Status400BadRequest, "invalid_rule", program.Error ?? "表达式无效");
            }

            var deviceId = (body?.DeviceId ?? "").Trim();
            if (deviceId.Length == 0)
            {
                return ApiResults.Error(StatusCodes.Status400BadRequest, "invalid_rule", "回测需要选择设备");
            }

            var to = body?.ToUnixMs is > 0 ? body.ToUnixMs : DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            var from = body?.FromUnixMs is > 0 ? body.FromUnixMs : to - 6 * 60 * 60 * 1000;
            var frames = database.HistoryFrames(deviceId, program.References, from, to, 3000);
            var hits = RuleRuntime.Backtest(program, Math.Max(0, body?.DurationMs ?? 0), Math.Max(0, body?.DebounceMs ?? 0), frames);
            return ApiResults.Ok(new
            {
                frames = frames.Count,
                fires = hits.Count(item => item.Fired),
                hits
            });
        });

        api.MapGet("/rules/log", (string? ruleId, int? limit, GatewayPersistence database) =>
            ApiResults.Ok(new { events = database.ListRuleLogs(ruleId, limit ?? 50) }));
    }

    private static string? ValidateComputed(ComputedWrite? body)
    {
        if (body is null)
        {
            return "请求无效";
        }

        if (body.Scope is not ("device" or "template"))
        {
            return "范围只能是设备或点位模板";
        }

        if (string.IsNullOrWhiteSpace(body.OwnerId) || string.IsNullOrWhiteSpace(body.Name))
        {
            return "需要名称和所属设备或模板";
        }

        if (string.IsNullOrWhiteSpace(body.PointId) || !System.Text.RegularExpressions.Regex.IsMatch(body.PointId.Trim(), "^[A-Za-z_][A-Za-z0-9_]{0,63}$"))
        {
            return "点位 Id 需以字母或下划线开头，只能包含字母、数字和下划线";
        }

        var program = ExpressionEngine.Compile(body.Expression);
        return program.Ok ? null : program.Error;
    }

    private static string? ValidateRule(RuleWrite? body)
    {
        if (body is null || string.IsNullOrWhiteSpace(body.Name))
        {
            return "规则需要名称";
        }

        var scope = string.IsNullOrWhiteSpace(body.Scope) ? "all" : body.Scope.Trim().ToLowerInvariant();
        if (scope is not ("all" or "device" or "template"))
        {
            return "范围只能是全部、设备或模板";
        }

        if (scope != "all" && string.IsNullOrWhiteSpace(body.OwnerId))
        {
            return "需要选择设备或模板";
        }

        var program = ExpressionEngine.Compile(body.Expression);
        if (!program.Ok)
        {
            return program.Error;
        }

        var actions = EdgePipeline.ParseActions(body.ActionsJson);
        if ((body.ActionsJson ?? "").Trim().Length > 2 && actions.Count == 0)
        {
            return "动作需要是 JSON 数组";
        }

        foreach (var action in actions)
        {
            if (action.Type is not ("alarm" or "notify" or "write" or "event" or "reason"))
            {
                return "动作类型只能是报警、通知、写入、北向事件或原因";
            }
        }

        return null;
    }

    private static object[] FunctionHelp() =>
    [
        new { name = "if(条件, 真, 假)", text = "条件成立时取第二个值" },
        new { name = "delta(点位)", text = "相对上一笔采样的差值" },
        new { name = "rate(点位)", text = "差值除以间隔秒" },
        new { name = "avg(点位, 秒)", text = "窗口内移动平均，最长 1 小时" },
        new { name = "durationTrue(条件)", text = "条件持续为真的秒数" },
        new { name = "counterInc(点位) / counterInc(点位, 模)", text = "计数增量，下降视为回绕" },
        new { name = "since(点位)", text = "距上次数值变化的秒数" }
    ];
}

public sealed class ComputedWrite
{
    public string? Scope { get; set; }

    public string? OwnerId { get; set; }

    public string? PointId { get; set; }

    public string? Name { get; set; }

    public string? Unit { get; set; }

    public string? Expression { get; set; }

    public bool Enabled { get; set; } = true;
}

public sealed class PreviewRequest
{
    public string? Expression { get; set; }

    public string? DeviceId { get; set; }
}

public sealed class RuleWrite
{
    public string? Name { get; set; }

    public bool Enabled { get; set; } = true;

    public string? Scope { get; set; }

    public string? OwnerId { get; set; }

    public string? Expression { get; set; }

    public long DurationMs { get; set; }

    public long DebounceMs { get; set; }

    public string? ActionsJson { get; set; }

    public string? TemplateKey { get; set; }
}

public sealed class BacktestRequest
{
    public string? Expression { get; set; }

    public string? DeviceId { get; set; }

    public long DurationMs { get; set; }

    public long DebounceMs { get; set; }

    public long FromUnixMs { get; set; }

    public long ToUnixMs { get; set; }
}
