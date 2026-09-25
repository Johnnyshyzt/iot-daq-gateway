using Studio.Contracts;
using Studio.Host.Config;
using Xunit;

namespace Studio.Tests;

public sealed class FanucPointCatalogTests : IDisposable
{
    private readonly string _directory = Directory.CreateTempSubdirectory("fanuc-catalog").FullName;

    [Theory]
    [InlineData("fanuc.fake")]
    [InlineData("fanuc.focas")]
    public void Three_catalog_points_pass_and_address_typos_are_rewritten(string adapter)
    {
        var bundle = ConfigDefaults.Create();
        bundle.Devices[0].Spec.Adapter = adapter;
        var state = bundle.PointSets[0].Spec.Points[0];
        state.Id = "State";
        state.Address = "D100";
        state.DataType = "int";

        var result = ConfigValidator.Validate(bundle);

        Assert.True(result.Valid, string.Join("; ", result.Issues.Select(issue => issue.Message)));
        Assert.Equal("state", state.Id);
        Assert.Equal("cnc/statinfo", state.Address);
        Assert.Equal("string", state.DataType);
        Assert.Equal(["state", "alarm", "program"], bundle.PointSets[0].Spec.Points.Select(point => point.Id).ToArray());
    }

    [Theory]
    [InlineData("fanuc.fake")]
    [InlineData("fanuc.focas")]
    public void Unknown_point_id_fails_in_chinese(string adapter)
    {
        var bundle = ConfigDefaults.Create();
        bundle.Devices[0].Spec.Adapter = adapter;
        bundle.PointSets[0].Spec.Points.Add(new PointDefinition
        {
            Id = "spindle",
            Address = "cnc/spindle",
            DataType = "string",
            Enabled = true
        });

        var result = ConfigValidator.Validate(bundle);

        Assert.False(result.Valid);
        var issue = Assert.Single(result.Issues, item => item.Severity == "error" && item.Path.EndsWith("/spindle", StringComparison.Ordinal));
        Assert.Contains("spindle", issue.Message, StringComparison.Ordinal);
        Assert.Contains("发那科", issue.Message, StringComparison.Ordinal);
        Assert.Contains("目录", issue.Message, StringComparison.Ordinal);
        Assert.Contains("state", issue.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Disabled_unknown_point_still_fails_validation()
    {
        var bundle = ConfigDefaults.Create();
        bundle.PointSets[0].Spec.Points.Add(new PointDefinition
        {
            Id = "spindle",
            Address = "D200",
            DataType = "string",
            Enabled = false
        });

        var result = ConfigValidator.Validate(bundle);

        Assert.False(result.Valid);
        Assert.Contains(result.Issues, issue => issue.Message.Contains("spindle", StringComparison.Ordinal));
    }

    [Fact]
    public void Upsert_and_publish_fill_catalog_address_without_requiring_the_user_to_type_it()
    {
        var store = new ConfigStore(_directory);
        store.EnsureInitialized();
        var points = store.GetPoints("cnc-01");
        points.Spec.Points[0].Address = "typed-wrong";
        points.Spec.Points[0].Unit = "mode";
        var saved = store.UpsertPoints("cnc-01", points);

        Assert.Equal("cnc/statinfo", saved.Spec.Points[0].Address);
        Assert.Equal("mode", saved.Spec.Points[0].Unit);
        Assert.Contains("address: cnc/statinfo", File.ReadAllText(Path.Combine(_directory, "draft", "points", "cnc-01.yaml")), StringComparison.Ordinal);
        Assert.DoesNotContain("typed-wrong", File.ReadAllText(Path.Combine(_directory, "draft", "points", "cnc-01.yaml")), StringComparison.Ordinal);

        var yamlPath = Path.Combine(_directory, "draft", "points", "cnc-01.yaml");
        File.WriteAllText(yamlPath, File.ReadAllText(yamlPath).Replace("cnc/alarm", "MW100", StringComparison.Ordinal));
        var published = store.Publish("normalize address");

        Assert.True(published.Published, string.Join("; ", published.Issues.Select(issue => issue.Message)));
        var publishedYaml = File.ReadAllText(Path.Combine(_directory, "published", "points", "cnc-01.yaml"));
        Assert.Contains("address: cnc/alarm", publishedYaml, StringComparison.Ordinal);
        Assert.DoesNotContain("MW100", publishedYaml, StringComparison.Ordinal);
        Assert.DoesNotContain("MW100", File.ReadAllText(yamlPath), StringComparison.Ordinal);
    }

    public void Dispose()
    {
        Directory.Delete(_directory, recursive: true);
    }
}
