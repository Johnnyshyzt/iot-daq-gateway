using System.Text.Json.Nodes;
using Gateway.Abstractions.Contract;
using Gateway.Abstractions.Reliability;
using Json.Schema;
using Xunit;

namespace Gateway.Tests;

public sealed class NorthboundContractTests
{
    [Fact]
    public void Examples_match_the_published_schemas()
    {
        var root = RepoRoot();
        var contract = Path.Combine(root, "docs", "contract");
        AssertValid(contract, "point-value.schema.json", "examples/point-value.legacy.json");
        AssertValid(contract, "point-value.schema.json", "examples/point-value.v1.json");
        AssertValid(contract, "point-value.schema.json", "examples/point-value.computed.json");
        AssertValid(contract, "device-status.schema.json", "examples/device-status.legacy.json");
        AssertValid(contract, "alarm.schema.json", "examples/alarm.json");
        AssertValid(contract, "part-count.schema.json", "examples/part-count.json");
        AssertValid(contract, "utilization.schema.json", "examples/utilization.json");
        AssertValid(contract, "rule-event.schema.json", "examples/rule-event.json");
        AssertValid(contract, "batch.schema.json", "examples/batch.json");
    }

    [Fact]
    public void Legacy_point_json_omits_schema_and_v1_adds_it()
    {
        var when = new DateTimeOffset(2026, 9, 27, 8, 0, 0, TimeSpan.Zero);
        var legacy = NorthboundPayload.Point("gw-plant-a", "plant-a", "cnc-01", "state", "RUNNING", "good", null, when, versioned: false);
        var v1 = NorthboundPayload.Point("gw-plant-a", "plant-a", "cnc-01", "state", "RUNNING", "good", null, when, versioned: true);
        Assert.DoesNotContain("\"schema\"", legacy, StringComparison.Ordinal);
        Assert.Contains("\"kind\":\"pointValue\"", v1.Replace(" ", ""), StringComparison.Ordinal);
        Assert.Contains("\"gatewayId\":\"gw-plant-a\"", legacy.Replace(" ", ""), StringComparison.Ordinal);
        Assert.Contains("\"unit\":null", legacy.Replace(" ", ""), StringComparison.Ordinal);

        var alarm = NorthboundTopics.Alarm("daq/{site}/{deviceId}/{point}", "plant-a", "cnc-01");
        var parts = NorthboundTopics.Parts("daq/{site}/{deviceId}/{point}", "plant-a", "cnc-01");
        var utilization = NorthboundTopics.Utilization("daq/{site}/{deviceId}/{point}", "plant-a");
        Assert.Equal("daq/plant-a/cnc-01/$alarm", alarm);
        Assert.Equal("daq/plant-a/cnc-01/$parts", parts);
        Assert.Equal("daq/plant-a/$utilization", utilization);
    }

    [Fact]
    public void Disk_spool_replays_oldest_first_and_drops_when_full()
    {
        var directory = Directory.CreateTempSubdirectory("disk-spool").FullName;
        try
        {
            var spool = new DiskForwardSpool(directory, maxMessages: 2, maxBytes: 1024 * 1024, maxAge: TimeSpan.FromHours(1));
            var now = DateTimeOffset.UtcNow;
            spool.Enqueue("one", now);
            spool.Enqueue("two", now.AddSeconds(1));
            spool.Enqueue("three", now.AddSeconds(2));
            Assert.Equal(2, spool.Depth);
            Assert.True(spool.Dropped >= 1);
            var first = spool.PeekOldest(now.AddMinutes(1));
            Assert.NotNull(first);
            Assert.Equal("two", first!.Payload);
            spool.Acknowledge(first.Seq);
            var second = spool.PeekOldest(now.AddMinutes(1));
            Assert.Equal("three", second!.Payload);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static void AssertValid(string contract, string schemaName, string exampleName)
    {
        var schema = JsonSchema.FromText(File.ReadAllText(Path.Combine(contract, schemaName)));
        var node = JsonNode.Parse(File.ReadAllText(Path.Combine(contract, exampleName)));
        var result = schema.Evaluate(node, new EvaluationOptions { OutputFormat = OutputFormat.List });
        Assert.True(result.IsValid, schemaName + " rejected " + exampleName);
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "IotDaqGateway.sln")))
            {
                return dir.FullName;
            }

            dir = dir.Parent;
        }

        throw new InvalidOperationException("找不到仓库根目录");
    }
}
