using Studio.Contracts;
using Studio.Host.Config;
using Xunit;

namespace Studio.Tests;

public sealed class ConfigStoreTests : IDisposable
{
    private readonly string _directory = Directory.CreateTempSubdirectory("studio-store").FullName;

    [Fact]
    public void Publish_and_rollback_round_trip_files()
    {
        var store = new ConfigStore(_directory);
        store.EnsureInitialized();

        var initial = store.ActiveRevision();
        Assert.Equal(initial, CanonicalRevision.Compute(store.ReadPublished()));
        Assert.False(store.GetView().Dirty);

        var created = store.UpsertDevice("cnc-02", new DeviceDocument
        {
            Metadata = new DeviceMetadata { Id = "cnc-02", DisplayName = "铣床 02" },
            Spec = new DeviceSpec
            {
                Adapter = "fanuc.fake",
                Enabled = true,
                IntervalMs = 1000,
                Connection = new DeviceConnection { Host = "10.0.0.8", Port = 8193 }
            }
        });
        Assert.Equal("cnc-02", created.Metadata.Id);
        Assert.Equal(ConfigDefaults.DefaultFanucTemplateId, created.Spec.PointTemplateId);
        Assert.Empty(store.GetPoints("cnc-02").Spec.Points);
        Assert.True(File.Exists(Path.Combine(_directory, "draft", "devices", "cnc-02.yaml")));
        Assert.False(File.Exists(Path.Combine(_directory, "draft", "points", "cnc-02.yaml")));
        Assert.Contains(
            "pointTemplateId: fanuc-standard",
            File.ReadAllText(Path.Combine(_directory, "draft", "devices", "cnc-02.yaml")),
            StringComparison.Ordinal);

        var mqtt = store.GetMqtt();
        mqtt.Spec.Broker.UsernameFromEnv = "MQTT_USER";
        mqtt.Spec.Broker.PasswordFromEnv = "MQTT_PASSWORD";
        mqtt.Spec.Broker.ClientId = "studio-line";
        store.UpsertMqtt(mqtt);
        var mqttYaml = File.ReadAllText(Path.Combine(_directory, "draft", "sinks", "mqtt.yaml"));
        Assert.Contains("passwordFromEnv: MQTT_PASSWORD", mqttYaml, StringComparison.Ordinal);
        Assert.DoesNotContain("\npassword:", mqttYaml, StringComparison.Ordinal);

        var device = store.GetDevice("cnc-01");
        device.Spec.Adapter = "modbus";
        store.UpsertDevice("cnc-01", device);
        var invalid = store.Validate();
        Assert.False(invalid.Valid);
        Assert.Contains(invalid.Issues, issue => issue.Severity == "error" && issue.Path.Contains("adapter", StringComparison.Ordinal));

        device.Spec.Adapter = "fanuc.fake";
        store.UpsertDevice("cnc-01", device);
        var valid = store.Validate();
        Assert.True(valid.Valid, string.Join("; ", valid.Issues.Select(issue => issue.Message)));

        var published = store.Publish("add cnc-02");
        Assert.True(published.Published);
        Assert.False(published.Unchanged);
        Assert.NotEqual(initial, published.Revision);
        Assert.Equal(published.Revision, store.ActiveRevision());
        Assert.Contains("cnc-02", File.ReadAllText(Path.Combine(_directory, "published", "devices", "cnc-02.yaml")), StringComparison.Ordinal);

        var again = store.Publish("noop");
        Assert.True(again.Unchanged);
        Assert.Equal(published.Revision, again.Revision);

        store.Rollback(initial);
        Assert.Equal(initial, store.ActiveRevision());
        Assert.Equal("Lathe 01", store.GetDevice("cnc-01").Metadata.DisplayName);
        Assert.Throws<ConfigStoreException>(() => store.GetDevice("cnc-02"));
        Assert.False(File.Exists(Path.Combine(_directory, "published", "devices", "cnc-02.yaml")));
        Assert.False(File.Exists(Path.Combine(_directory, "draft", "devices", "cnc-02.yaml")));

        var revisions = store.ListRevisions(10);
        Assert.Contains(revisions, item => item.Revision == published.Revision && item.Action == "publish");
        Assert.Contains(revisions, item => item.Revision == initial && item.Action == "rollback");
    }

    [Fact]
    public void Enabled_device_without_points_fails_validation()
    {
        var store = new ConfigStore(_directory);
        store.EnsureInitialized();
        var template = store.GetPointTemplate(ConfigDefaults.DefaultFanucTemplateId);
        foreach (var point in template.Spec.Points)
        {
            point.Enabled = false;
        }

        store.UpsertPointTemplate(ConfigDefaults.DefaultFanucTemplateId, template);

        var result = store.Validate();

        Assert.False(result.Valid);
        Assert.Contains(result.Issues, issue => issue.Severity == "error" && issue.Message.Contains("启用的点位", StringComparison.Ordinal));
    }

    public void Dispose()
    {
        Directory.Delete(_directory, recursive: true);
    }
}
