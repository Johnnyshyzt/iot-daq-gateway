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
        var state = bundle.PointTemplates[0].Spec.Points[0];
        state.Id = "State";
        state.Address = "D100";
        state.DataType = "int";

        var result = ConfigValidator.Validate(bundle);

        Assert.True(result.Valid, string.Join("; ", result.Issues.Select(issue => issue.Message)));
        Assert.Equal("state", state.Id);
        Assert.Equal("cnc/statinfo", state.Address);
        Assert.Equal("string", state.DataType);
        Assert.Equal(["state", "alarm", "program"], bundle.PointTemplates[0].Spec.Points.Select(point => point.Id).ToArray());
    }

    [Theory]
    [InlineData("fanuc.fake")]
    [InlineData("fanuc.focas")]
    public void Unknown_point_id_fails_in_chinese(string adapter)
    {
        var bundle = ConfigDefaults.Create();
        bundle.Devices[0].Spec.Adapter = adapter;
        bundle.PointTemplates[0].Spec.Points.Add(new PointDefinition
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
        bundle.PointTemplates[0].Spec.Points.Add(new PointDefinition
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
        var template = store.GetPointTemplate(ConfigDefaults.DefaultFanucTemplateId);
        template.Spec.Points[0].Address = "typed-wrong";
        template.Spec.Points[0].Unit = "mode";
        var saved = store.UpsertPointTemplate(ConfigDefaults.DefaultFanucTemplateId, template);

        Assert.Equal("cnc/statinfo", saved.Spec.Points[0].Address);
        Assert.Equal("mode", saved.Spec.Points[0].Unit);
        Assert.Equal("cnc/statinfo", store.GetPointTemplate(ConfigDefaults.DefaultFanucTemplateId).Spec.Points[0].Address);
        Assert.False(File.Exists(Path.Combine(_directory, "draft", "point-templates", "fanuc-standard.yaml")));

        var alarm = store.GetPointTemplate(ConfigDefaults.DefaultFanucTemplateId);
        alarm.Spec.Points.Single(point => point.Id == "alarm").Address = "MW100";
        store.UpsertPointTemplate(ConfigDefaults.DefaultFanucTemplateId, alarm);
        var decoy = Path.Combine(_directory, "draft", "point-templates");
        Directory.CreateDirectory(decoy);
        File.WriteAllText(Path.Combine(decoy, "fanuc-standard.yaml"), "address: MW100\n");
        var published = store.Publish("normalize address");

        Assert.True(published.Published, string.Join("; ", published.Issues.Select(issue => issue.Message)));
        var publishedTemplate = store.ReadPublished().PointTemplates.Single(template => template.Metadata.Id == ConfigDefaults.DefaultFanucTemplateId);
        Assert.Contains(publishedTemplate.Spec.Points, point => point.Id == "alarm" && point.Address == "cnc/alarm");
        Assert.DoesNotContain(publishedTemplate.Spec.Points, point => point.Address == "MW100");
        Assert.Contains("address: MW100", File.ReadAllText(Path.Combine(decoy, "fanuc-standard.yaml")), StringComparison.Ordinal);
    }

    public void Dispose()
    {
        Directory.Delete(_directory, recursive: true);
    }
}
