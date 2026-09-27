using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Gateway.Abstractions.Models;
using IotDaq.Persistence;
using IotDaq.Persistence.Rules;
using IotDaq.Persistence.Visualization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Studio.Host.Auth;
using Studio.Host.Config;
using Xunit;

namespace Studio.Tests;

public sealed class Phase9ExpressionTests
{
    [Fact]
    public void Sandbox_rejects_code_and_unknown_calls()
    {
        Assert.False(ExpressionEngine.Compile("Process.Start(\"cmd\")").Ok);
        Assert.False(ExpressionEngine.Compile("1; spindleLoad").Ok);
        Assert.False(ExpressionEngine.Compile("new string()").Ok);
        Assert.False(ExpressionEngine.Compile("{ return 1 }").Ok);
        Assert.Contains("不允许", ExpressionEngine.Compile("evil(1)").Error);
        Assert.False(ExpressionEngine.Compile(new string('1', 2001) + "+1").Ok);
    }

    [Fact]
    public void Math_logic_and_strings_evaluate()
    {
        var program = ExpressionEngine.Compile("if(spindleLoad > 80 and not estop, 1, 0)");
        Assert.True(program.Ok);
        var value = ExpressionEngine.Evaluate(program, Context(new() { ["spindleLoad"] = 91, ["estop"] = 0 }, new()));
        Assert.Equal(1, value.Number);
        var idle = ExpressionEngine.Compile("state == \"IDLE\" or state == \"待机\"");
        var text = ExpressionEngine.Evaluate(idle, Context(new(), new() { ["state"] = "待机" }));
        Assert.True(text.Truthy);
    }

    [Fact]
    public void Time_functions_cover_delta_rate_average_duration_and_rollover()
    {
        var memory = new PointMemory();
        memory.Observe("partCount", 1_000, 10);
        memory.Observe("partCount", 2_000, 14);
        memory.Observe("spindleLoad", 1_000, 10);
        memory.Observe("spindleLoad", 2_000, 30);
        var numbers = new Dictionary<string, double?> { ["partCount"] = 14, ["spindleLoad"] = 30 };
        Assert.Equal(4, Eval("delta(partCount)", memory, numbers, 2_000).Number);
        Assert.Equal(20, Eval("rate(spindleLoad)", memory, numbers, 2_000).Number);
        Assert.Equal(20, Eval("avg(spindleLoad, 5)", memory, numbers, 2_000).Number);
        Assert.Equal(4, Eval("counterInc(partCount)", memory, numbers, 2_000).Number);
        memory.Observe("partCount", 3_000, 2);
        numbers["partCount"] = 2;
        Assert.Equal(2, Eval("counterInc(partCount)", memory, numbers, 3_000).Number);
        Assert.Equal(988, Eval("counterInc(partCount, 1000)", memory, numbers, 3_000).Number);
        var held = Eval("durationTrue(spindleLoad > 20)", memory, numbers, 2_000);
        var later = Eval("durationTrue(spindleLoad > 20)", memory, numbers, 5_000);
        Assert.Equal(0, held.Number);
        Assert.Equal(3, later.Number);
        Assert.Equal(1, Eval("since(partCount)", memory, numbers, 4_000).Number);
    }

    [Fact]
    public void Rule_duration_debounce_and_backtest()
    {
        var episode = new RuleEpisode();
        Assert.False(episode.Step(true, 0, 5_000, 10_000));
        Assert.False(episode.Step(true, 4_000, 5_000, 10_000));
        Assert.True(episode.Step(true, 5_000, 5_000, 10_000));
        Assert.False(episode.Step(false, 6_000, 5_000, 10_000));
        Assert.False(episode.Step(true, 7_000, 5_000, 10_000));
        Assert.True(episode.Step(true, 16_000, 5_000, 10_000));

        var program = ExpressionEngine.Compile("spindleLoad > 90");
        var frames = new List<HistoryFrame>
        {
            Frame(0, 10),
            Frame(1_000, 95),
            Frame(6_000, 96),
            Frame(7_000, 10),
            Frame(8_000, 99),
            Frame(20_000, 99)
        };
        var hits = RuleRuntime.Backtest(program, 5_000, 10_000, frames);
        Assert.Equal(2, hits.Count(hit => hit.Fired));
        Assert.Equal(6_000, hits.First(hit => hit.Fired).UnixMs);
    }

    [Fact]
    public void State_map_overrides_the_default_word()
    {
        var maps = new List<StateMapRule>
        {
            new() { Scope = "brand", OwnerId = "fanuc", RawValue = "HOLD", State = "setup" }
        };
        Assert.Equal(MachineState.Setup, StateClassifier.Classify("HOLD", "cnc", "fanuc", null, maps));
        Assert.Equal(MachineState.Idle, StateClassifier.Classify("HOLD", "cnc", "siemens", null, maps));
        Assert.Equal(MachineState.Waiting, StateClassifier.Classify("待料", "cnc", "fanuc", null, []));
        Assert.Equal(MachineState.Running, MachineState.Normalize("RUNNING"));
    }

    [Fact]
    public void Oee_handles_zero_plan_and_missing_cycle()
    {
        var empty = OeeMath.Compute(new OeeInput { PlannedMs = 0, RunMs = 0, TotalParts = 0, ScrapParts = 0, IdealCycleSeconds = 48 });
        Assert.Equal(0, empty.Oee);
        Assert.Equal("noPlannedTime", empty.Flag);

        var missing = OeeMath.Compute(new OeeInput { PlannedMs = 600_000, RunMs = 300_000, TotalParts = 10, ScrapParts = 1, IdealCycleSeconds = null });
        Assert.Equal(0.5, missing.Availability);
        Assert.Null(missing.Performance);
        Assert.Null(missing.Oee);
        Assert.Equal("missingIdealCycle", missing.Flag);
        Assert.Equal(0.9, missing.Quality);

        var full = OeeMath.Compute(new OeeInput { PlannedMs = 600_000, RunMs = 300_000, TotalParts = 10, ScrapParts = 2, IdealCycleSeconds = 30 });
        Assert.Equal(0.5, full.Availability);
        Assert.Equal(1, full.Performance);
        Assert.Equal(0.8, full.Quality);
        Assert.Equal(0.4, full.Oee);
        Assert.Equal(8, full.GoodParts);

        var none = OeeMath.Compute(new OeeInput { PlannedMs = 600_000, RunMs = 0, TotalParts = 0, ScrapParts = 0, IdealCycleSeconds = 30 });
        Assert.Equal(1, none.Quality);
    }

    [Fact]
    public void Two_hundred_devices_times_twenty_points_stay_inside_the_budget()
    {
        var programs = Enumerable.Range(0, 20)
            .Select(index => ExpressionEngine.Compile(index % 2 == 0
                ? "if(spindleLoad > 80, spindleLoad * 0.01, delta(partCount))"
                : "avg(spindleLoad, 30) + counterInc(partCount)"))
            .ToList();
        Assert.All(programs, program => Assert.True(program.Ok));
        var watch = Stopwatch.StartNew();
        for (var device = 0; device < 200; device++)
        {
            var memory = new PointMemory();
            memory.Observe("spindleLoad", 1_000, 40);
            memory.Observe("partCount", 1_000, device);
            memory.Observe("spindleLoad", 2_000, 50 + device % 40);
            memory.Observe("partCount", 2_000, device + 1);
            var numbers = new Dictionary<string, double?>
            {
                ["spindleLoad"] = 50 + device % 40,
                ["partCount"] = device + 1
            };
            foreach (var program in programs)
            {
                var value = ExpressionEngine.Evaluate(program, Context(numbers, new(), memory, 2_000));
                Assert.True(value.Ok);
            }
        }

        watch.Stop();
        Assert.True(watch.ElapsedMilliseconds < 3000, "elapsed " + watch.ElapsedMilliseconds);
    }

    private static EvalValue Eval(string source, PointMemory memory, Dictionary<string, double?> numbers, long now) =>
        ExpressionEngine.Evaluate(ExpressionEngine.Compile(source), Context(numbers, new(), memory, now));

    private static EvalContext Context(Dictionary<string, double?> numbers, Dictionary<string, string?> texts, PointMemory? memory = null, long now = 0) => new()
    {
        Memory = memory ?? new PointMemory(),
        Numbers = numbers,
        Texts = texts,
        NowMs = now,
        DurationScope = "test"
    };

    private static HistoryFrame Frame(long unix, double load) => new()
    {
        UnixMs = unix,
        Numbers = new Dictionary<string, double?> { ["spindleLoad"] = load }
    };
}

public sealed class Phase9SchemaTests
{
    [Fact]
    public void Startup_upgrades_v7_to_v8_and_seeds_reasons()
    {
        var directory = Directory.CreateTempSubdirectory("phase9-schema").FullName;
        try
        {
            var store = new ConfigStore(directory);
            store.EnsureInitialized();
            using (var db = store.Database.CreateContext())
            {
                db.Database.ExecuteSqlRaw("DROP TABLE computed_points");
                db.Database.ExecuteSqlRaw("UPDATE schema_info SET Version = 7 WHERE Id = 1");
            }

            store.Database.EnsureReady();
            using var check = store.Database.CreateContext();
            Assert.Equal(GatewayPersistence.SchemaVersion, check.SchemaInfo.AsNoTracking().Single().Version);
            Assert.True(check.DowntimeReasons.Any(row => row.Code == "TOOL"));
            Assert.Equal(0, check.ComputedPoints.Count());
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}

public sealed class Phase9ApiTests : IClassFixture<Phase8Factory>
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly Phase8Factory _factory;

    public Phase9ApiTests(Phase8Factory factory) => _factory = factory;

    [Fact]
    public async Task Operator_can_enter_a_reason_but_cannot_change_rules()
    {
        using var admin = _factory.CreateClient();
        using var floor = _factory.CreateClient();
        using var viewer = _factory.CreateClient();
        await Login(admin, "admin", "admin");
        await Login(floor, "operator", "operator");
        await Login(viewer, "viewer", "viewer");

        var database = _factory.Services.GetRequiredService<GatewayPersistence>();
        database.Write(
        [
            new Observation { DeviceId = "rbac-cnc", Point = "state", Value = "IDLE", Timestamp = DateTimeOffset.UtcNow }
        ]);
        var open = database.ListDowntime("rbac-cnc", null, null, true, 10);
        var id = Assert.Single(open).Id;

        var denied = await viewer.PostAsJsonAsync("/api/v1/downtime/assign", new { eventIds = new[] { id }, reasonId = "fault.tool", note = "刀具" }, Json);
        Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);

        var rule = await floor.PutAsJsonAsync("/api/v1/rules/rule-1", new { name = "不允许", expression = "1 > 0", scope = "all", enabled = true, actionsJson = "[]" }, Json);
        Assert.Equal(HttpStatusCode.Forbidden, rule.StatusCode);

        var assigned = await floor.PostAsJsonAsync("/api/v1/downtime/assign", new { eventIds = new[] { id }, reasonId = "fault.tool", note = "刀具" }, Json);
        Assert.Equal(HttpStatusCode.OK, assigned.StatusCode);
        var updated = database.ListDowntime("rbac-cnc", null, null, false, 10).Single();
        Assert.Equal("fault.tool", updated.ReasonId);
        Assert.Equal(ApiPolicy.Operate, ApiPolicy.Required("POST", "/api/v1/downtime/assign"));
        Assert.Equal(ApiPolicy.Write, ApiPolicy.Required("PUT", "/api/v1/rules/rule-1"));
    }

    private static async Task Login(HttpClient client, string username, string password)
    {
        var response = await client.PostAsJsonAsync("/api/v1/auth/login", new { username, password }, Json);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>(Json);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", json.GetProperty("token").GetString());
    }
}
