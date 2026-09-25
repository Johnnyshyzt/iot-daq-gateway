using Studio.Contracts;
using Studio.Host.Config;
using Xunit;

namespace Studio.Tests;

public sealed class PointTemplateTests : IDisposable
{
    private readonly string _directory = Directory.CreateTempSubdirectory("point-templates").FullName;

    [Fact]
    public void Two_devices_share_one_template_and_publish_expands_effective_points()
    {
        var store = new ConfigStore(_directory);
        store.EnsureInitialized();
        store.UpsertDevice("cnc-02", new DeviceDocument
        {
            Metadata = new DeviceMetadata { Id = "cnc-02", DisplayName = "Lathe 02" },
            Spec = new DeviceSpec
            {
                Adapter = "fanuc.focas",
                Enabled = true,
                IntervalMs = 1000,
                PointTemplateId = ConfigDefaults.DefaultFanucTemplateId,
                Connection = new DeviceConnection { Host = "192.168.1.11", Port = 8193, FocasTimeoutMs = 3000 }
            }
        });

        var validation = store.Validate();
        Assert.True(validation.Valid, string.Join("; ", validation.Issues.Select(issue => issue.Message)));

        var template = store.GetPointTemplate(ConfigDefaults.DefaultFanucTemplateId);
        foreach (var deviceId in new[] { "cnc-01", "cnc-02" })
        {
            var device = store.GetDevice(deviceId);
            Assert.Equal(ConfigDefaults.DefaultFanucTemplateId, device.Spec.PointTemplateId);
            var effective = PointExpansion.EffectivePoints(template, store.GetPoints(deviceId));
            Assert.Equal(["state", "alarm", "program"], effective.Where(point => point.Enabled).Select(point => point.Id).ToArray());
        }

        var published = store.Publish("two lathes");
        Assert.True(published.Published, string.Join("; ", published.Issues.Select(issue => issue.Message)));
        var publishedDevices = store.ReadPublished().Devices;
        Assert.Equal(2, publishedDevices.Count);
        Assert.All(publishedDevices, device => Assert.Equal(ConfigDefaults.DefaultFanucTemplateId, device.Spec.PointTemplateId));
        Assert.False(File.Exists(Path.Combine(_directory, "published", "points", "cnc-01.yaml")));
        Assert.False(File.Exists(Path.Combine(_directory, "published", "points", "cnc-02.yaml")));
        Assert.Contains("id: state", File.ReadAllText(Path.Combine(_directory, "published", "point-templates", "fanuc-standard.yaml")), StringComparison.Ordinal);
    }

    [Fact]
    public void Missing_template_fails_in_chinese()
    {
        var bundle = ConfigDefaults.Create();
        bundle.Devices[0].Spec.PointTemplateId = "missing-template";

        var result = ConfigValidator.Validate(bundle);

        Assert.False(result.Valid);
        var issue = Assert.Single(result.Issues, item => item.Path.EndsWith("pointTemplateId", StringComparison.Ordinal) && item.Severity == "error");
        Assert.Contains("不存在", issue.Message, StringComparison.Ordinal);
        Assert.Contains("missing-template", issue.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Blank_template_fails_in_chinese()
    {
        var bundle = ConfigDefaults.Create();
        bundle.Devices[0].Spec.PointTemplateId = " ";

        var result = ConfigValidator.Validate(bundle);

        Assert.False(result.Valid);
        Assert.Contains(result.Issues, issue => issue.Message.Contains("点位模板", StringComparison.Ordinal) && issue.Message.Contains("一类模板", StringComparison.Ordinal));
    }

    [Fact]
    public void Template_family_must_match_the_device_adapter()
    {
        var bundle = ConfigDefaults.Create();
        bundle.PointTemplates[0].Spec.Adapter = "siemens";

        var result = ConfigValidator.Validate(bundle);

        Assert.False(result.Valid);
        Assert.Contains(result.Issues, issue => issue.Message.Contains("不一致", StringComparison.Ordinal) && issue.Message.Contains("fanuc.fake", StringComparison.Ordinal));
        Assert.Contains(result.Issues, issue => issue.Message.Contains("只支持发那科", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("fanuc.fake")]
    [InlineData("fanuc.focas")]
    public void Fanuc_template_matches_both_fanuc_adapters(string adapter)
    {
        var bundle = ConfigDefaults.Create();
        bundle.Devices[0].Spec.Adapter = adapter;

        var result = ConfigValidator.Validate(bundle);

        Assert.True(result.Valid, string.Join("; ", result.Issues.Select(issue => issue.Message)));
    }

    [Fact]
    public void Unknown_template_point_id_fails()
    {
        var bundle = ConfigDefaults.Create();
        bundle.PointTemplates[0].Spec.Points.Add(new PointDefinition
        {
            Id = "spindle",
            Address = "D100",
            DataType = "string",
            Enabled = true
        });

        var result = ConfigValidator.Validate(bundle);

        Assert.False(result.Valid);
        Assert.Contains(result.Issues, issue =>
            issue.Severity == "error"
            && issue.Path.Contains("spindle", StringComparison.Ordinal)
            && issue.Message.Contains("目录", StringComparison.Ordinal));
    }

    [Fact]
    public void Override_cannot_add_a_non_catalog_id()
    {
        var bundle = ConfigDefaults.Create();
        bundle.PointSets.Add(new PointSetDocument
        {
            Metadata = new PointSetMetadata { DeviceId = "cnc-01" },
            Spec = new PointSetSpec
            {
                Points =
                [
                    new PointDefinition
                    {
                        Id = "spindle",
                        Address = "D100",
                        DataType = "string",
                        Enabled = true
                    }
                ]
            }
        });

        var result = ConfigValidator.Validate(bundle);

        Assert.False(result.Valid);
        var issue = Assert.Single(result.Issues, item => item.Severity == "error" && item.Message.Contains("spindle", StringComparison.Ordinal));
        Assert.Contains("本机覆盖", issue.Message, StringComparison.Ordinal);
        Assert.Contains("目录", issue.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Override_replaces_matching_id_and_leaves_the_rest_of_the_template()
    {
        var bundle = ConfigDefaults.Create();
        bundle.PointSets.Add(new PointSetDocument
        {
            Metadata = new PointSetMetadata { DeviceId = "cnc-01" },
            Spec = new PointSetSpec
            {
                Points =
                [
                    new PointDefinition
                    {
                        Id = "alarm",
                        DataType = "string",
                        Unit = "code",
                        Scale = 1,
                        Deadband = 0,
                        Enabled = false
                    }
                ]
            }
        });

        var result = ConfigValidator.Validate(bundle);
        Assert.True(result.Valid, string.Join("; ", result.Issues.Select(issue => issue.Message)));

        var effective = PointExpansion.EffectivePoints(bundle.PointTemplates[0], bundle.PointSets[0]);
        Assert.Equal(["state", "program"], effective.Where(point => point.Enabled).Select(point => point.Id).ToArray());
        Assert.False(effective.Single(point => point.Id == "alarm").Enabled);
        Assert.Equal("cnc/alarm", effective.Single(point => point.Id == "alarm").Address);
    }

    [Fact]
    public void Legacy_full_point_table_attaches_to_the_default_template()
    {
        WriteLegacyBundle(alarmEnabled: true, stateScale: 1);
        var store = new ConfigStore(_directory);
        store.EnsureInitialized();

        Assert.Equal(ConfigDefaults.DefaultFanucTemplateId, store.GetDevice("cnc-01").Spec.PointTemplateId);
        Assert.Equal("Fanuc 标准三态", store.GetPointTemplate(ConfigDefaults.DefaultFanucTemplateId).Metadata.DisplayName);
        Assert.Empty(store.GetPoints("cnc-01").Spec.Points);
        Assert.False(File.Exists(Path.Combine(_directory, "published", "points", "cnc-01.yaml")));
        var validation = store.Validate();
        Assert.True(validation.Valid, string.Join("; ", validation.Issues.Select(issue => issue.Message)));

        var again = new ConfigStore(_directory);
        again.EnsureInitialized();
        Assert.Empty(again.GetPoints("cnc-01").Spec.Points);
        Assert.False(again.GetView().Dirty);
    }

    [Fact]
    public void Legacy_difference_is_kept_as_an_override()
    {
        WriteLegacyBundle(alarmEnabled: false, stateScale: 2);
        var store = new ConfigStore(_directory);
        store.EnsureInitialized();

        var overrides = store.GetPoints("cnc-01");
        Assert.Contains(overrides.Spec.Points, point => point.Id == "alarm" && !point.Enabled);
        Assert.Contains(overrides.Spec.Points, point => point.Id == "state" && point.Scale == 2);
        var effective = PointExpansion.EffectivePoints(
            store.GetPointTemplate(ConfigDefaults.DefaultFanucTemplateId),
            overrides);
        Assert.False(effective.Single(point => point.Id == "alarm").Enabled);
        Assert.Equal(2, effective.Single(point => point.Id == "state").Scale);
        Assert.True(effective.Single(point => point.Id == "program").Enabled);
        var validation = store.Validate();
        Assert.True(validation.Valid, string.Join("; ", validation.Issues.Select(issue => issue.Message)));
    }

    private void WriteLegacyBundle(bool alarmEnabled, double stateScale)
    {
        var published = Path.Combine(_directory, "published");
        Directory.CreateDirectory(Path.Combine(published, "devices"));
        Directory.CreateDirectory(Path.Combine(published, "points"));
        Directory.CreateDirectory(Path.Combine(published, "sinks"));
        File.WriteAllText(
            Path.Combine(published, "gateway.yaml"),
            """
            apiVersion: daq.gateway/v1
            kind: Gateway
            metadata:
              siteId: plant-a
              name: Plant A Gateway
            spec:
              logLevel: Information
              features:
                programWrite: false
              acquisition:
                defaultIntervalMs: 1000
                changeOnly: true
            """);
        File.WriteAllText(
            Path.Combine(published, "devices", "cnc-01.yaml"),
            """
            apiVersion: daq.gateway/v1
            kind: Device
            metadata:
              id: cnc-01
              displayName: Lathe 01
            spec:
              adapter: fanuc.fake
              enabled: true
              intervalMs: 1000
              connection:
                host: 192.168.1.10
                port: 8193
                focasTimeoutMs: 3000
            """);
        var scale = stateScale.ToString(System.Globalization.CultureInfo.InvariantCulture);
        var alarm = alarmEnabled ? "true" : "false";
        File.WriteAllText(
            Path.Combine(published, "points", "cnc-01.yaml"),
            $"""
            apiVersion: daq.gateway/v1
            kind: PointSet
            metadata:
              deviceId: cnc-01
            spec:
              points:
                - id: state
                  address: cnc/statinfo
                  dataType: string
                  unit: ""
                  scale: {scale}
                  deadband: 0
                  enabled: true
                - id: alarm
                  address: cnc/alarm
                  dataType: string
                  unit: ""
                  scale: 1
                  deadband: 0
                  enabled: {alarm}
                - id: program
                  address: cnc/program
                  dataType: string
                  unit: ""
                  scale: 1
                  deadband: 0
                  enabled: true
            """);
        File.WriteAllText(
            Path.Combine(published, "sinks", "mqtt.yaml"),
            """
            apiVersion: daq.gateway/v1
            kind: MqttSink
            metadata:
              id: mqtt-main
            spec:
              broker:
                host: 127.0.0.1
                port: 1883
                clientId: iot-daq-gateway
                usernameFromEnv: MQTT_USER
                passwordFromEnv: MQTT_PASSWORD
                tls: false
              topicTemplate: daq/{site}/{deviceId}/{point}
              qos: 1
              retain: false
              statusTopic: daq/{site}/{deviceId}/$status
            """);
    }

    public void Dispose()
    {
        Directory.Delete(_directory, recursive: true);
    }
}
