using System.Text.Json;
using Gateway.Abstractions.Contracts;
using Gateway.Abstractions.Models;
using IotDaq.Licensing;
using IotDaq.Persistence;
using IotDaq.Persistence.Rules;
using Studio.Host.Config;
using Studio.Host.Licensing;

namespace Studio.Host.Rules;

public sealed class EdgePipeline(GatewayPersistence database, LicenseService license, ConfigStore store) : IObservationExpander
{
    private readonly object _gate = new();
    private readonly Dictionary<string, PointMemory> _memory = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, RuleEpisode> _episodes = new(StringComparer.Ordinal);
    private readonly Dictionary<string, ExpressionProgram> _programs = new(StringComparer.Ordinal);
    private long _cacheTick;
    private List<ComputedPointRow> _points = [];
    private List<EdgeRuleRow> _rules = [];
    private Dictionary<string, string> _templates = new(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyList<Observation> Expand(IReadOnlyList<Observation> observations)
    {
        if (observations.Count == 0)
        {
            return observations;
        }

        Refresh();
        if (_points.Count == 0 && (!_rules.Any(rule => rule.Enabled) || !license.Allows(LicenseFeatures.Rules)))
        {
            return observations;
        }

        var result = new List<Observation>(observations);
        foreach (var group in observations.GroupBy(item => item.DeviceId, StringComparer.OrdinalIgnoreCase))
        {
            var deviceId = group.Key;
            var batch = group.ToList();
            var now = batch.Max(item => item.Timestamp.ToUnixTimeMilliseconds());
            var latest = database.Latest(deviceId);
            var numbers = new Dictionary<string, double?>(StringComparer.OrdinalIgnoreCase);
            var texts = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
            foreach (var sample in latest)
            {
                numbers[sample.PointId] = sample.NumericValue;
                texts[sample.PointId] = sample.Value;
            }

            var memory = Memory(deviceId);
            var computed = EdgeEvaluator.Compute(deviceId, Specs(deviceId), batch, numbers, texts, memory, now);
            result.AddRange(computed.Observations);
            if (license.Allows(LicenseFeatures.Rules))
            {
                ApplyRules(deviceId, computed, memory, now, result);
            }
        }

        return result;
    }

    public void Invalidate()
    {
        lock (_gate)
        {
            _cacheTick = 0;
        }
    }

    private void ApplyRules(string deviceId, ComputeBatch batch, PointMemory memory, long now, List<Observation> result)
    {
        _templates.TryGetValue(deviceId, out var template);
        foreach (var rule in _rules.Where(item => item.Enabled && Applies(item, deviceId, template)))
        {
            var program = Compile(rule.Expression);
            if (!program.Ok)
            {
                continue;
            }

            var value = ExpressionEngine.Evaluate(program, new EvalContext
            {
                Memory = memory,
                Numbers = batch.Numbers,
                Texts = batch.Texts,
                NowMs = now,
                DurationScope = deviceId + ":rule:" + rule.Id
            });
            if (!value.Ok)
            {
                continue;
            }

            var episode = Episode(deviceId, rule.Id);
            var wasHolding = episode.Holding;
            var fired = episode.Step(value.Truthy, now, rule.DurationMs, rule.DebounceMs);
            if (!value.Truthy)
            {
                if (wasHolding)
                {
                    database.UpsertRuleAlarm(deviceId, rule.Id, "", "", "", false, now);
                }

                continue;
            }

            if (!fired)
            {
                continue;
            }

            var actions = ParseActions(rule.ActionsJson);
            var notes = new List<string>();
            foreach (var action in actions)
            {
                notes.Add(Run(action, rule, deviceId, now, result));
            }

            database.AppendRuleLog(rule.Id, deviceId, true, "规则触发", string.Join("；", notes.Where(item => item.Length > 0)));
        }
    }

    private string Run(RuleAction action, EdgeRuleRow rule, string deviceId, long now, List<Observation> result)
    {
        var type = (action.Type ?? "").Trim().ToLowerInvariant();
        switch (type)
        {
            case "alarm":
                database.UpsertRuleAlarm(
                    deviceId,
                    rule.Id,
                    string.IsNullOrWhiteSpace(action.Code) ? rule.Name : action.Code.Trim(),
                    string.IsNullOrWhiteSpace(action.Message) ? rule.Name : action.Message.Trim(),
                    string.IsNullOrWhiteSpace(action.Severity) ? "warning" : action.Severity.Trim(),
                    true,
                    now);
                return "报警";
            case "notify":
                if (!license.Allows(LicenseFeatures.AlarmNotifications) || string.IsNullOrWhiteSpace(action.ChannelId))
                {
                    return "通知未发送";
                }

                database.AddDelivery(new NotificationDeliveryRow
                {
                    ChannelId = action.ChannelId.Trim(),
                    Kind = "rule",
                    Summary = "【采集网关】" + (string.IsNullOrWhiteSpace(action.Message) ? rule.Name : action.Message.Trim()) + "（" + deviceId + "）",
                    Status = "pending",
                    CreatedUnixMs = now,
                    NextAttemptUnixMs = now
                });
                return "通知";
            case "write":
                if (string.IsNullOrWhiteSpace(action.PointId))
                {
                    return "";
                }

                var number = action.Value ?? 1;
                result.Add(new Observation
                {
                    DeviceId = deviceId,
                    Point = action.PointId.Trim(),
                    Value = number,
                    Timestamp = DateTimeOffset.FromUnixTimeMilliseconds(now),
                    Quality = "good",
                    Computed = true
                });
                return "写入 " + action.PointId.Trim();
            case "event":
                database.AppendRuleEvent(rule.Id, deviceId, string.IsNullOrWhiteSpace(action.Name) ? rule.Name : action.Name.Trim(), action.Message ?? rule.Name);
                return "北向事件";
            case "reason":
                var reason = database.ListReasons().FirstOrDefault(item =>
                    string.Equals(item.Code, action.ReasonCode, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(item.Id, action.ReasonCode, StringComparison.OrdinalIgnoreCase));
                if (reason is null)
                {
                    return "原因码不存在";
                }

                var assigned = database.AssignOpenDowntime(deviceId, reason.Id, rule.Id);
                return assigned is null ? "没有待填写的停机" : "已填原因";
            default:
                return "";
        }
    }

    private IReadOnlyList<ComputedSpec> Specs(string deviceId)
    {
        _templates.TryGetValue(deviceId, out var template);
        var chosen = new Dictionary<string, ComputedPointRow>(StringComparer.OrdinalIgnoreCase);
        foreach (var row in _points.Where(item => item.Enabled))
        {
            var match = string.Equals(row.Scope, "device", StringComparison.OrdinalIgnoreCase) && string.Equals(row.OwnerId, deviceId, StringComparison.OrdinalIgnoreCase)
                || string.Equals(row.Scope, "template", StringComparison.OrdinalIgnoreCase) && string.Equals(row.OwnerId, template, StringComparison.OrdinalIgnoreCase);
            if (!match)
            {
                continue;
            }

            if (!chosen.TryGetValue(row.PointId, out var current) || string.Equals(row.Scope, "device", StringComparison.OrdinalIgnoreCase))
            {
                chosen[row.PointId] = row;
            }
        }

        var specs = new List<ComputedSpec>();
        foreach (var row in chosen.Values)
        {
            var program = Compile(row.Expression);
            if (!program.Ok)
            {
                continue;
            }

            specs.Add(new ComputedSpec
            {
                PointId = row.PointId,
                Name = row.Name,
                Unit = row.Unit,
                Program = program
            });
        }

        return specs;
    }

    private ExpressionProgram Compile(string expression)
    {
        lock (_gate)
        {
            if (_programs.TryGetValue(expression, out var cached))
            {
                return cached;
            }

            var program = ExpressionEngine.Compile(expression);
            if (_programs.Count > 400)
            {
                _programs.Clear();
            }

            _programs[expression] = program;
            return program;
        }
    }

    private PointMemory Memory(string deviceId)
    {
        lock (_gate)
        {
            if (!_memory.TryGetValue(deviceId, out var memory))
            {
                memory = new PointMemory();
                _memory[deviceId] = memory;
            }

            return memory;
        }
    }

    private RuleEpisode Episode(string deviceId, string ruleId)
    {
        var key = deviceId + "\n" + ruleId;
        lock (_gate)
        {
            if (!_episodes.TryGetValue(key, out var episode))
            {
                episode = new RuleEpisode();
                _episodes[key] = episode;
            }

            return episode;
        }
    }

    private static bool Applies(EdgeRuleRow rule, string deviceId, string? template)
    {
        if (string.Equals(rule.Scope, "device", StringComparison.OrdinalIgnoreCase))
        {
            return string.Equals(rule.OwnerId, deviceId, StringComparison.OrdinalIgnoreCase);
        }

        if (string.Equals(rule.Scope, "template", StringComparison.OrdinalIgnoreCase))
        {
            return string.Equals(rule.OwnerId, template, StringComparison.OrdinalIgnoreCase);
        }

        return true;
    }

    private void Refresh()
    {
        var now = Environment.TickCount64;
        if (_cacheTick != 0 && now - _cacheTick < 2000)
        {
            return;
        }

        lock (_gate)
        {
            _points = database.ListComputedPoints().ToList();
            _rules = database.ListEdgeRules().ToList();
            _templates = store.ReadPublished().Devices.ToDictionary(
                device => device.Metadata.Id,
                device => device.Spec.PointTemplateId ?? "",
                StringComparer.OrdinalIgnoreCase);
            _cacheTick = now;
        }
    }

    internal static List<RuleAction> ParseActions(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return [];
        }

        try
        {
            return JsonSerializer.Deserialize<List<RuleAction>>(json, new JsonSerializerOptions(JsonSerializerDefaults.Web)) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }
}

public sealed class RuleAction
{
    public string Type { get; set; } = "";

    public string Severity { get; set; } = "warning";

    public string Message { get; set; } = "";

    public string Code { get; set; } = "";

    public string ChannelId { get; set; } = "";

    public string PointId { get; set; } = "";

    public double? Value { get; set; }

    public string Name { get; set; } = "";

    public string ReasonCode { get; set; } = "";
}
