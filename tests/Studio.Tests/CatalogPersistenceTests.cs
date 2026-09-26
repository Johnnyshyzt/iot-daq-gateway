using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Adapters.Cnc;
using Cnc.Catalog;
using IotDaq.Persistence;
using Microsoft.AspNetCore.Mvc.Testing;
using Studio.Contracts;
using Studio.Host.Config;
using Xunit;

namespace Studio.Tests;

public sealed class CatalogPersistenceTests
{
    [Fact]
    public void Catalog_has_nineteen_brands_and_keeps_synonyms_that_coexist()
    {
        var catalog = CncCatalog.Current;
        Assert.Equal(19, catalog.Brands.Count);
        Assert.Contains(catalog.Items, item => item.Id == "state" && item.NameZh == "运行状态");
        Assert.Contains(catalog.Items, item => item.Id == "workMode");
        Assert.Contains(catalog.Items, item => item.Id == "spindleSpeed");
        Assert.Contains(catalog.Items, item => item.Id == "machinePosition");
        Assert.Contains(catalog.Items, item => item.Id == "partCount");
        Assert.Contains(catalog.Items, item => item.Id == "alarm");

        var fanuc = catalog.FindBrand("fanuc");
        Assert.NotNull(fanuc);
        Assert.Contains("state", fanuc.ItemIds);
        Assert.Contains("spindleSpeed", fanuc.ItemIds);
        Assert.Contains("fanuc_fanSpeed", fanuc.ItemIds);
        Assert.Equal(2, fanuc.Models.Count);

        var siemens = catalog.FindBrand("siemens");
        Assert.NotNull(siemens);
        Assert.Contains(siemens.Mappings, row => row.ItemId == "spindleSpeed" && row.Source.Contains("实际主轴", StringComparison.Ordinal));
        Assert.Contains("spindleSpeed", siemens.ItemIds);
        Assert.DoesNotContain("fanuc_fanSpeed", siemens.ItemIds);
        Assert.Contains(siemens.Models, model => model.Id == "siemens-828d");
        Assert.Contains(siemens.ItemIds, id => id == "state");
        Assert.Contains(siemens.ItemIds, id => id == "workMode");

        Assert.Equal("fanuc-catalog", catalog.StandardTemplateId("fanuc"));
        Assert.Equal("siemens-standard", catalog.StandardTemplateId("siemens"));
        Assert.Null(catalog.BrandOfAdapter("generic.ftp"));
        Assert.Equal(6, catalog.Brands.Count(brand => brand.Models.Count == 0));
    }

    [Fact]
    public void Sqlite_seeds_catalog_and_imports_existing_yaml_when_the_database_is_empty()
    {
        var source = Directory.CreateTempSubdirectory("catalog-yaml").FullName;
        var target = Directory.CreateTempSubdirectory("catalog-import").FullName;
        try
        {
            var store = new ConfigStore(source);
            store.EnsureInitialized();
            Assert.Equal(19, store.Database.CountBrands());
            Assert.Equal("Sqlite", store.Database.Provider);

            store.UpsertDevice("cnc-yaml", new DeviceDocument
            {
                Metadata = new DeviceMetadata { Id = "cnc-yaml", DisplayName = "迁移机床" },
                Spec = new DeviceSpec
                {
                    Adapter = "fanuc.fake",
                    BrandId = "fanuc",
                    Enabled = true,
                    IntervalMs = 1000,
                    PointTemplateId = ConfigDefaults.DefaultFanucTemplateId,
                    Connection = new DeviceConnection { Host = "127.0.0.1", Port = 8193, FocasTimeoutMs = 3000 }
                }
            });
            var published = store.Publish("migrate");
            Assert.False(string.IsNullOrWhiteSpace(published.Revision));

            CopyTree(source, target);
            foreach (var db in Directory.GetFiles(target, "gateway.db*"))
            {
                File.Delete(db);
            }

            var again = new ConfigStore(target);
            again.EnsureInitialized();
            using var dbContext = again.Database.CreateContext();
            Assert.Contains(dbContext.ConfigDevices, row => row.Slot == "published" && row.Id == "cnc-yaml");
            Assert.Equal(19, again.Database.CountBrands());
            Assert.Contains(again.ListPointTemplates(), template => template.Metadata.Id == "siemens-standard");
            Assert.Contains(again.ListPointTemplates(), template =>
                template.Metadata.Id == ConfigDefaults.DefaultFanucTemplateId
                && template.Metadata.DisplayName == "Fanuc 标准三态");
        }
        finally
        {
            TryDelete(source);
            TryDelete(target);
        }
    }

    [Fact]
    public void Postgres_without_a_connection_string_is_rejected()
    {
        var dir = Directory.CreateTempSubdirectory("catalog-pg").FullName;
        try
        {
            var error = Assert.Throws<InvalidOperationException>(() => GatewayPersistence.Open(dir, "PostgreSQL", null));
            Assert.Contains("PostgreSQL", error.Message, StringComparison.Ordinal);
        }
        finally
        {
            TryDelete(dir);
        }
    }

    [Fact]
    public void Publish_rejects_a_template_from_another_brand()
    {
        var dir = Directory.CreateTempSubdirectory("catalog-validate").FullName;
        try
        {
            var store = new ConfigStore(dir);
            store.EnsureInitialized();
            store.UpsertDevice("mix-01", new DeviceDocument
            {
                Metadata = new DeviceMetadata { Id = "mix-01", DisplayName = "品牌不一致" },
                Spec = new DeviceSpec
                {
                    Adapter = "siemens.sim",
                    BrandId = "siemens",
                    Enabled = true,
                    IntervalMs = 1000,
                    PointTemplateId = ConfigDefaults.DefaultFanucTemplateId,
                    ControllerModelId = "siemens-828d",
                    Connection = new DeviceConnection { Host = "127.0.0.1", Port = 4840, TimeoutMs = 3000 }
                }
            });
            var result = store.Validate();
            Assert.Contains(result.Issues, issue => issue.Message.Contains("不一致", StringComparison.Ordinal) && issue.Message.Contains("siemens.sim", StringComparison.Ordinal));

            store.UpsertDevice("ok-01", new DeviceDocument
            {
                Metadata = new DeviceMetadata { Id = "ok-01", DisplayName = "西门子模拟" },
                Spec = new DeviceSpec
                {
                    Adapter = "siemens.sim",
                    BrandId = "siemens",
                    Enabled = true,
                    IntervalMs = 1000,
                    PointTemplateId = "siemens-standard",
                    ControllerModelId = "siemens-828d",
                    Connection = new DeviceConnection { Host = "127.0.0.1", Port = 4840, TimeoutMs = 3000 }
                }
            });
            store.DeleteDevice("mix-01");
            var ok = store.Validate();
            Assert.True(ok.Valid, string.Join("; ", ok.Issues.Select(issue => issue.Message)));
        }
        finally
        {
            TryDelete(dir);
        }
    }

    [Fact]
    public void Simulator_values_stay_inside_the_brand_and_follow_the_cycle()
    {
        var idleSpindle = 0d;
        var sawIdle = false;
        var sawRun = false;
        for (var second = 0; second < MachineSimulation.CycleSeconds; second++)
        {
            var snapshot = MachineSimulation.At(DateTimeOffset.FromUnixTimeSeconds(second), "cycle-probe", "siemens");
            if (snapshot.State == "IDLE")
            {
                sawIdle = true;
                idleSpindle = snapshot.SpindleSpeed;
                Assert.Equal(0, snapshot.SpindleSpeed);
                Assert.Equal(0, snapshot.FeedRate);
            }

            if (snapshot.State == "RUNNING")
            {
                sawRun = true;
                Assert.True(snapshot.SpindleSpeed > 0);
            }
        }

        Assert.True(sawIdle);
        Assert.True(sawRun);
        Assert.Equal(0, idleSpindle);

        var early = MachineSimulation.At(DateTimeOffset.FromUnixTimeSeconds(0), "cycle-probe", "haas");
        var later = MachineSimulation.At(DateTimeOffset.FromUnixTimeSeconds(MachineSimulation.CycleSeconds), "cycle-probe", "haas");
        Assert.True(later.PartCount > early.PartCount);
        Assert.Contains(".nc", MachineSimulation.ProgramName("haas", 0), StringComparison.Ordinal);

        var rows = BrandSimulatorAdapter.Sample("cycle-probe", "siemens", DateTimeOffset.UnixEpoch, null);
        var ids = rows.Select(row => row.Point).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var siemens = CncCatalog.Current.FindBrand("siemens")!;
        Assert.Equal(siemens.ItemIds.Count, ids.Count);
        Assert.Contains("spindleSpeed", ids);
        Assert.DoesNotContain("fanuc_fanSpeed", ids);
        Assert.All(ids, id => Assert.Contains(id, siemens.ItemIds));
    }

    private static void CopyTree(string source, string destination)
    {
        foreach (var file in Directory.GetFiles(source, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(source, file);
            var target = Path.Combine(destination, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target, overwrite: true);
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
        catch (IOException)
        {
        }
    }
}

[Collection("studio-host")]
public sealed class BrandSimulatorHostTests
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    [Fact]
    public async Task Each_brand_simulator_writes_samples_into_sqlite()
    {
        var data = Directory.CreateTempSubdirectory("brand-host").FullName;
        Environment.SetEnvironmentVariable("STUDIO_DATA", data);
        await using var host = new BrandHostFactory(data);
        try
        {
            using var client = host.CreateClient();
            await Authorize(client);

            var catalog = await Read<CatalogBody>(await client.GetAsync("/api/v1/catalog/brands"));
            Assert.Equal(19, catalog.Brands.Count);
            Assert.Contains(catalog.Brands, brand => brand.Id == "fanuc");
            Assert.Contains(catalog.Items, item => item.Id == "spindleSpeed");

            foreach (var brand in catalog.Brands)
            {
                var simulator = brand.Adapters.First(adapter => adapter.Id.EndsWith(".sim", StringComparison.Ordinal));
                var templateId = brand.Id == "fanuc" ? "fanuc-catalog" : brand.Id + "-standard";
                var device = new DeviceDocument
                {
                    Metadata = new DeviceMetadata { Id = "sim-" + brand.Id, DisplayName = brand.NameZh + "模拟" },
                    Spec = new DeviceSpec
                    {
                        Adapter = simulator.Id,
                        BrandId = brand.Id,
                        Enabled = true,
                        IntervalMs = 400,
                        PointTemplateId = templateId,
                        ControllerModelId = brand.Models.FirstOrDefault()?.Id,
                        Connection = new DeviceConnection { Host = "127.0.0.1", Port = simulator.Port, TimeoutMs = 3000 }
                    }
                };
                await Read<DeviceDocument>(await client.PutAsJsonAsync(
                    "/api/v1/config/devices/" + device.Metadata.Id,
                    device,
                    Json));
            }

            var validation = await Read<ValidationResult>(await client.PostAsync("/api/v1/config/validate", content: null));
            Assert.True(validation.Valid, string.Join("; ", validation.Issues.Select(issue => issue.Message)));
            await Read<PublishResult>(await client.PostAsJsonAsync("/api/v1/config/publish", new PublishRequest { Note = "sims" }, Json));

            var pending = catalog.Brands.Select(brand => "sim-" + brand.Id).ToHashSet(StringComparer.Ordinal);
            var deadline = DateTime.UtcNow.AddSeconds(40);
            while (pending.Count > 0 && DateTime.UtcNow < deadline)
            {
                foreach (var id in pending.ToArray())
                {
                    var latest = await Read<LatestBody>(await client.GetAsync("/api/v1/devices/" + id + "/latest"));
                    if (latest.Samples.Count > 0)
                    {
                        pending.Remove(id);
                    }
                }

                if (pending.Count > 0)
                {
                    await Task.Delay(500);
                }
            }

            Assert.True(pending.Count == 0, "missing samples: " + string.Join(", ", pending));

            var history = await Read<HistoryBody>(await client.GetAsync(
                "/api/v1/samples/history?deviceId=sim-siemens&items=state,spindleSpeed&from=0"));
            Assert.Contains(history.Samples, sample => sample.PointId == "state");
            var alarms = await Read<AlarmBody>(await client.GetAsync("/api/v1/alarms?deviceId=sim-siemens&limit=20"));
            Assert.NotNull(alarms.Alarms);
        }
        finally
        {
            Environment.SetEnvironmentVariable("STUDIO_DATA", null);
            TryDelete(data);
        }
    }

    private static async Task Authorize(HttpClient client)
    {
        var response = await client.PostAsJsonAsync("/api/v1/auth/login", new LoginRequest { Username = "admin", Password = "admin" }, Json);
        var login = await Read<LoginResponse>(response);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", login.Token);
    }

    private static async Task<T> Read<T>(HttpResponseMessage response)
    {
        var json = await response.Content.ReadAsStringAsync();
        Assert.True(response.IsSuccessStatusCode, $"{(int)response.StatusCode} {json}");
        var body = JsonSerializer.Deserialize<T>(json, Json);
        Assert.NotNull(body);
        return body;
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
        catch (IOException)
        {
        }
    }

    private sealed class BrandHostFactory : WebApplicationFactory<Program>
    {
        private readonly string _dataDirectory;

        public BrandHostFactory(string dataDirectory)
        {
            _dataDirectory = dataDirectory;
        }

        protected override void ConfigureWebHost(Microsoft.AspNetCore.Hosting.IWebHostBuilder builder)
        {
            builder.UseSetting("Studio:DataDirectory", _dataDirectory);
            builder.UseSetting("Host:Acquisition", "on");
            builder.UseSetting("Studio:GatewayLoopback", "off");
        }
    }

    private sealed class CatalogBody
    {
        public List<BrandBody> Brands { get; set; } = [];

        public List<ItemBody> Items { get; set; } = [];
    }

    private sealed class BrandBody
    {
        public string Id { get; set; } = "";

        public string NameZh { get; set; } = "";

        public List<AdapterBody> Adapters { get; set; } = [];

        public List<ModelBody> Models { get; set; } = [];
    }

    private sealed class AdapterBody
    {
        public string Id { get; set; } = "";

        public List<ParamBody> Parameters { get; set; } = [];

        public int Port
        {
            get
            {
                var port = Parameters.FirstOrDefault(item => item.Name == "port");
                return port?.Default.ValueKind == JsonValueKind.Number ? port.Default.GetInt32() : 8193;
            }
        }
    }

    private sealed class ParamBody
    {
        public string Name { get; set; } = "";

        public JsonElement Default { get; set; }
    }

    private sealed class ModelBody
    {
        public string Id { get; set; } = "";
    }

    private sealed class ItemBody
    {
        public string Id { get; set; } = "";
    }

    private sealed class LatestBody
    {
        public List<SampleBody> Samples { get; set; } = [];
    }

    private sealed class HistoryBody
    {
        public List<SampleBody> Samples { get; set; } = [];
    }

    private sealed class SampleBody
    {
        public string PointId { get; set; } = "";
    }

    private sealed class AlarmBody
    {
        public List<AlarmItem> Alarms { get; set; } = [];
    }

    private sealed class AlarmItem
    {
        public string Message { get; set; } = "";
    }
}
