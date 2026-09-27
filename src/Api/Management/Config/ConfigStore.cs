using System.Text.Json;
using System.Text.RegularExpressions;
using Cnc.Catalog;
using IotDaq.Persistence;
using Studio.Contracts;

namespace Studio.Host.Config;

public sealed partial class ConfigStore
{
    private readonly object _gate = new();
    private readonly string _seed;
    private readonly string _draft;
    private readonly string _published;
    private readonly string _runtime;
    private readonly GatewayPersistence _database;

    public ConfigStore(string dataDirectory, GatewayPersistence? database = null)
    {
        DataDirectory = dataDirectory;
        _seed = Path.Combine(dataDirectory, "seed");
        _draft = Path.Combine(dataDirectory, "draft");
        _published = Path.Combine(dataDirectory, "published");
        _runtime = Path.Combine(dataDirectory, "runtime");
        _database = database ?? GatewayPersistence.Open(dataDirectory, null);
    }

    public string DataDirectory { get; }

    public GatewayPersistence Database => _database;

    public void EnsureInitialized(string? importPath = null)
    {
        lock (_gate)
        {
            Directory.CreateDirectory(_runtime);
            _database.EnsureReady();

            if (!_database.HasSlot("published"))
            {
                var published = LoadInitialBundle(importPath);
                PrepareImported(published);
                SaveSlot("published", published);
                var hash = CanonicalRevision.Compute(published);
                _database.AppendRevision(hash, DateTimeOffset.UtcNow, "seed", "初始配置", published, hash);
            }

            if (!_database.HasSlot("draft"))
            {
                var draft = File.Exists(GatewayPath(_draft)) && string.IsNullOrWhiteSpace(importPath)
                    ? ReadBundle(_draft)
                    : CloneBundle(ReadSlot("published"));
                PrepareImported(draft);
                SaveSlot("draft", draft);
            }

            EnsureBrandTemplatesUnlocked();
            AppendLogUnlocked("Config Studio 已启动");
        }
    }

    public ConfigView GetView()
    {
        lock (_gate)
        {
            var draft = ReadSlot("draft");
            var published = ReadSlot("published");
            var draftHash = CanonicalRevision.Compute(draft);
            var active = CanonicalRevision.Compute(published);
            return new ConfigView
            {
                ActiveRevision = active,
                DraftHash = draftHash,
                Dirty = !string.Equals(draftHash, active, StringComparison.Ordinal),
                Draft = draft,
                Published = published
            };
        }
    }

    public DiffView Diff()
    {
        lock (_gate)
        {
            return ConfigDiffer.Compare(ReadSlot("published"), ReadSlot("draft"));
        }
    }

    public IReadOnlyList<DeviceDocument> ListDevices()
    {
        lock (_gate)
        {
            return ReadSlot("draft").Devices
                .OrderBy(device => device.Metadata.Id, StringComparer.Ordinal)
                .ToList();
        }
    }

    public DeviceDocument GetDevice(string id)
    {
        lock (_gate)
        {
            return FindDevice(ReadSlot("draft"), id);
        }
    }

    public DeviceDocument UpsertDevice(string id, DeviceDocument document)
    {
        lock (_gate)
        {
            EnsureSafeId(id);
            document.Metadata ??= new DeviceMetadata();
            if (!string.IsNullOrWhiteSpace(document.Metadata.Id)
                && !string.Equals(document.Metadata.Id, id, StringComparison.Ordinal))
            {
                throw new ConfigStoreException("id_mismatch", "路径中的设备 Id 与正文不一致", StatusCodes.Status400BadRequest);
            }

            document.ApiVersion = StudioApi.Version;
            document.Kind = "Device";
            document.Metadata.Id = id;
            document.Spec ??= new DeviceSpec();
            document.Spec.Connection ??= new DeviceConnection();
            document.Spec.Adapter = (document.Spec.Adapter ?? "").Trim().ToLowerInvariant();
            document.Spec.PointTemplateId = string.IsNullOrWhiteSpace(document.Spec.PointTemplateId)
                ? null
                : document.Spec.PointTemplateId.Trim();

            var bundle = ReadSlot("draft");
            var brandId = CncCatalog.Current.BrandOfAdapter(document.Spec.Adapter);
            if (string.IsNullOrWhiteSpace(brandId))
            {
                brandId = CncCatalog.Current.FindBrand(document.Spec.BrandId)?.Id;
            }

            if (!string.IsNullOrWhiteSpace(brandId) && string.IsNullOrWhiteSpace(document.Spec.PointTemplateId))
            {
                document.Spec.PointTemplateId = string.Equals(brandId, FanucPointCatalog.Family, StringComparison.Ordinal)
                    ? ConfigDefaults.DefaultFanucTemplateId
                    : brandId + "-standard";
            }

            if (string.Equals(document.Spec.PointTemplateId, ConfigDefaults.DefaultFanucTemplateId, StringComparison.Ordinal)
                && bundle.PointTemplates.All(template => !string.Equals(template.Metadata.Id, ConfigDefaults.DefaultFanucTemplateId, StringComparison.Ordinal)))
            {
                bundle.PointTemplates.Add(ConfigDefaults.FanucTemplate());
            }

            if (!string.IsNullOrWhiteSpace(brandId))
            {
                var standardId = string.Equals(brandId, FanucPointCatalog.Family, StringComparison.Ordinal)
                    ? CncCatalog.Current.StandardTemplateId(brandId)
                    : brandId + "-standard";
                if (string.Equals(document.Spec.PointTemplateId, standardId, StringComparison.Ordinal)
                    && bundle.PointTemplates.All(template => !string.Equals(template.Metadata.Id, standardId, StringComparison.Ordinal)))
                {
                    var brand = CncCatalog.Current.FindBrand(brandId);
                    if (brand is not null)
                    {
                        bundle.PointTemplates.Add(BrandTemplateSeeder.Create(CncCatalog.Current, brand, standardId));
                    }
                }
            }

            var index = bundle.Devices.FindIndex(device => string.Equals(device.Metadata.Id, id, StringComparison.OrdinalIgnoreCase));
            if (index >= 0 && !string.Equals(bundle.Devices[index].Metadata.Id, id, StringComparison.Ordinal))
            {
                throw new ConfigStoreException("id_conflict", "已存在仅大小写不同的设备 Id", StatusCodes.Status409Conflict);
            }

            if (index >= 0)
            {
                bundle.Devices[index] = document;
            }
            else
            {
                bundle.Devices.Add(document);
            }

            SaveSlot("draft", bundle);
            AppendLogUnlocked($"草稿已更新设备 {id}");
            return document;
        }
    }

    public void DeleteDevice(string id)
    {
        lock (_gate)
        {
            EnsureSafeId(id);
            var bundle = ReadSlot("draft");
            var removed = bundle.Devices.RemoveAll(device => string.Equals(device.Metadata.Id, id, StringComparison.Ordinal));
            if (removed == 0)
            {
                throw new ConfigStoreException("device_not_found", "设备不存在", StatusCodes.Status404NotFound);
            }

            bundle.PointSets.RemoveAll(set => string.Equals(set.Metadata.DeviceId, id, StringComparison.Ordinal));
            SaveSlot("draft", bundle);
            AppendLogUnlocked($"草稿已删除设备 {id}");
        }
    }

    public IReadOnlyList<PointTemplateDocument> ListPointTemplates()
    {
        lock (_gate)
        {
            return ReadSlot("draft").PointTemplates
                .OrderBy(template => template.Metadata.Id, StringComparer.Ordinal)
                .ToList();
        }
    }

    public PointTemplateDocument GetPointTemplate(string id)
    {
        lock (_gate)
        {
            return FindTemplate(ReadSlot("draft"), id);
        }
    }

    public PointTemplateDocument UpsertPointTemplate(string id, PointTemplateDocument document)
    {
        lock (_gate)
        {
            EnsureSafeId(id);
            document.Metadata ??= new PointTemplateMetadata();
            if (!string.IsNullOrWhiteSpace(document.Metadata.Id)
                && !string.Equals(document.Metadata.Id, id, StringComparison.Ordinal))
            {
                throw new ConfigStoreException("id_mismatch", "路径中的模板 Id 与正文不一致", StatusCodes.Status400BadRequest);
            }

            document.ApiVersion = StudioApi.Version;
            document.Kind = "PointTemplate";
            document.Metadata.Id = id;
            document.Metadata.DisplayName = (document.Metadata.DisplayName ?? "").Trim();
            document.Spec ??= new PointTemplateSpec();
            document.Spec.Points ??= [];
            document.Spec.Adapter = (document.Spec.Adapter ?? "").Trim().ToLowerInvariant();
            if (string.IsNullOrWhiteSpace(document.Spec.Adapter))
            {
                document.Spec.Adapter = FanucPointCatalog.Family;
            }

            foreach (var point in document.Spec.Points)
            {
                point.Id = (point.Id ?? "").Trim();
                point.DataType = (point.DataType ?? "").Trim().ToLowerInvariant();
                point.Address = (point.Address ?? "").Trim();
                point.Unit ??= "";
                PointCatalogNormalizer.TryNormalize(document.Spec.Adapter, point);
            }

            var bundle = ReadSlot("draft");
            var index = bundle.PointTemplates.FindIndex(template =>
                string.Equals(template.Metadata.Id, id, StringComparison.OrdinalIgnoreCase));
            if (index >= 0 && !string.Equals(bundle.PointTemplates[index].Metadata.Id, id, StringComparison.Ordinal))
            {
                throw new ConfigStoreException("id_conflict", "已存在仅大小写不同的模板 Id", StatusCodes.Status409Conflict);
            }

            if (index >= 0)
            {
                bundle.PointTemplates[index] = document;
            }
            else
            {
                bundle.PointTemplates.Add(document);
            }

            SaveSlot("draft", bundle);
            AppendLogUnlocked($"草稿已更新点位模板 {id}");
            return document;
        }
    }

    public void DeletePointTemplate(string id)
    {
        lock (_gate)
        {
            EnsureSafeId(id);
            var bundle = ReadSlot("draft");
            var template = bundle.PointTemplates.FirstOrDefault(item =>
                string.Equals(item.Metadata.Id, id, StringComparison.Ordinal));
            if (template is null)
            {
                throw new ConfigStoreException("template_not_found", "点位模板不存在", StatusCodes.Status404NotFound);
            }

            var users = bundle.Devices
                .Where(device => string.Equals(device.Spec.PointTemplateId, id, StringComparison.Ordinal))
                .Select(device => device.Metadata.Id)
                .ToList();
            if (users.Count > 0)
            {
                throw new ConfigStoreException(
                    "template_in_use",
                    $"仍有设备在使用该模板：{string.Join("、", users)}。请先改这些设备的点位模板。",
                    StatusCodes.Status409Conflict);
            }

            bundle.PointTemplates.Remove(template);
            SaveSlot("draft", bundle);
            AppendLogUnlocked($"草稿已删除点位模板 {id}");
        }
    }

    public PointSetDocument GetPoints(string deviceId)
    {
        lock (_gate)
        {
            var bundle = ReadSlot("draft");
            FindDevice(bundle, deviceId);
            return bundle.PointSets.FirstOrDefault(set => string.Equals(set.Metadata.DeviceId, deviceId, StringComparison.Ordinal))
                ?? ConfigDefaults.EmptyPoints(deviceId);
        }
    }

    public PointSetDocument UpsertPoints(string deviceId, PointSetDocument document)
    {
        lock (_gate)
        {
            EnsureSafeId(deviceId);
            var bundle = ReadSlot("draft");
            var device = FindDevice(bundle, deviceId);
            document.ApiVersion = StudioApi.Version;
            document.Kind = "PointSet";
            document.Metadata ??= new PointSetMetadata();
            document.Metadata.DeviceId = deviceId;
            document.Spec ??= new PointSetSpec();
            document.Spec.Points ??= [];
            var pointBrand = CncCatalog.Current.BrandOfAdapter(device.Spec.Adapter)
                ?? CncCatalog.Current.FindBrand(device.Spec.BrandId)?.Id;
            foreach (var point in document.Spec.Points)
            {
                point.Id = (point.Id ?? "").Trim();
                point.DataType = (point.DataType ?? "").Trim().ToLowerInvariant();
                point.Address = (point.Address ?? "").Trim();
                point.Unit ??= "";
                PointCatalogNormalizer.TryNormalize(pointBrand, point);
            }

            var index = bundle.PointSets.FindIndex(set => string.Equals(set.Metadata.DeviceId, deviceId, StringComparison.Ordinal));
            if (index >= 0)
            {
                bundle.PointSets[index] = document;
            }
            else
            {
                bundle.PointSets.Add(document);
            }

            SaveSlot("draft", bundle);
            AppendLogUnlocked($"草稿已更新点位 {deviceId}");
            return document;
        }
    }

    public MqttSinkDocument GetMqtt()
    {
        lock (_gate)
        {
            return ReadSlot("draft").Mqtt;
        }
    }

    public MqttSinkDocument UpsertMqtt(MqttSinkDocument document)
    {
        lock (_gate)
        {
            document.ApiVersion = StudioApi.Version;
            document.Kind = "MqttSink";
            document.Metadata ??= new MqttSinkMetadata();
            if (string.IsNullOrWhiteSpace(document.Metadata.Id))
            {
                document.Metadata.Id = "mqtt-main";
            }

            document.Spec ??= new MqttSinkSpec();
            document.Spec.Broker ??= new MqttBrokerSpec();
            document.Spec.Broker.UsernameFromEnv = BlankToNull(document.Spec.Broker.UsernameFromEnv);
            document.Spec.Broker.PasswordFromEnv = BlankToNull(document.Spec.Broker.PasswordFromEnv);
            var bundle = ReadSlot("draft");
            bundle.Mqtt = document;
            SaveSlot("draft", bundle);
            AppendLogUnlocked("草稿已更新 MQTT");
            return document;
        }
    }

    public GatewayDocument GetGateway()
    {
        lock (_gate)
        {
            return ReadSlot("draft").Gateway;
        }
    }

    public GatewayDocument UpsertGateway(GatewayDocument document)
    {
        lock (_gate)
        {
            document.ApiVersion = StudioApi.Version;
            document.Kind = "Gateway";
            document.Metadata ??= new GatewayMetadata();
            document.Spec ??= new GatewaySpec();
            document.Spec.Features ??= new GatewayFeatures();
            document.Spec.Acquisition ??= new AcquisitionSpec();
            document.Metadata.SiteId = (document.Metadata.SiteId ?? "").Trim();
            document.Metadata.Name = (document.Metadata.Name ?? "").Trim();
            document.Spec.LogLevel = CanonicalLogLevel(document.Spec.LogLevel);
            var bundle = ReadSlot("draft");
            bundle.Gateway = document;
            SaveSlot("draft", bundle);
            AppendLogUnlocked("草稿已更新网关设置");
            return document;
        }
    }

    public CollectionResult SetCollectionEnabled(string id, bool enabled)
    {
        lock (_gate)
        {
            var draft = ReadSlot("draft");
            var device = FindDevice(draft, id);
            device.Spec.Enabled = enabled;
            SaveSlot("draft", draft);

            var published = ReadSlot("published");
            var index = published.Devices.FindIndex(item => string.Equals(item.Metadata.Id, id, StringComparison.Ordinal));
            var reloaded = false;
            if (index >= 0)
            {
                published.Devices[index].Spec.Enabled = enabled;
                SaveSlot("published", published);
                var hash = CanonicalRevision.Compute(published);
                _database.AppendRevision(
                    hash,
                    DateTimeOffset.UtcNow,
                    "collection",
                    enabled ? "启动采集" : "停止采集",
                    published,
                    hash);
                reloaded = true;
            }

            AppendLogUnlocked(reloaded
                ? $"设备 {id} 采集已{(enabled ? "启动" : "停止")}"
                : $"草稿里设备 {id} 已{(enabled ? "启用" : "禁用")}，尚未发布，采集未重载");
            return new CollectionResult { DeviceId = id, Enabled = enabled, Reloaded = reloaded };
        }
    }

    public ValidationResult Validate()
    {
        lock (_gate)
        {
            return ConfigValidator.Validate(ReadSlot("draft"));
        }
    }

    public PublishOutcome Publish(string? note)
    {
        lock (_gate)
        {
            var draft = ReadSlot("draft");
            var validation = ConfigValidator.Validate(draft);
            if (!validation.Valid)
            {
                return new PublishOutcome { Issues = validation.Issues };
            }

            // Fanuc catalog ids get a fixed internal address. Persist that into the draft
            // so a successful publish does not leave a hand-edited address behind.
            SaveSlot("draft", draft);
            var hash = CanonicalRevision.Compute(draft);
            var current = CanonicalRevision.Compute(ReadSlot("published"));
            var now = DateTimeOffset.UtcNow;
            if (hash == current)
            {
                return new PublishOutcome
                {
                    Published = true,
                    Unchanged = true,
                    Revision = hash,
                    PublishedAt = now,
                    Issues = validation.Issues
                };
            }

            SaveSlot("published", draft);
            _database.AppendRevision(
                hash,
                now,
                "publish",
                string.IsNullOrWhiteSpace(note) ? null : note.Trim(),
                draft,
                hash);
            AppendLogUnlocked($"已发布配置 {hash[..12]}");
            return new PublishOutcome
            {
                Published = true,
                Revision = hash,
                PublishedAt = now,
                Issues = validation.Issues
            };
        }
    }

    public IReadOnlyList<RevisionInfo> ListRevisions(int take)
    {
        lock (_gate)
        {
            return _database.ListRevisions(take)
                .Select(item => new RevisionInfo
                {
                    Revision = item.Revision,
                    CreatedAt = DateTimeOffset.FromUnixTimeMilliseconds(item.CreatedUnixMs),
                    Action = item.Action,
                    Note = item.Note
                })
                .ToList();
        }
    }

    public RollbackOutcome Rollback(string revision)
    {
        lock (_gate)
        {
            var hash = NormalizeRevision(revision);
            var bundle = _database.TryLoadRevision(hash);
            if (bundle is null)
            {
                throw new ConfigStoreException("revision_not_found", "找不到该修订", StatusCodes.Status404NotFound);
            }

            var migrated = PointTemplateMigration.Apply(bundle);
            if (migrated)
            {
                hash = CanonicalRevision.Compute(bundle);
            }

            SaveSlot("published", bundle);
            SaveSlot("draft", bundle);
            var now = DateTimeOffset.UtcNow;
            _database.AppendRevision(
                hash,
                now,
                "rollback",
                migrated ? "回滚到该修订，并迁移为点位模板" : "回滚到该修订",
                bundle,
                hash);
            AppendLogUnlocked($"已回滚到 {hash[..12]}");
            return new RollbackOutcome { Revision = hash, RolledBackAt = now };
        }
    }

    public ConfigBundle ReadPublished()
    {
        lock (_gate)
        {
            return ReadSlot("published");
        }
    }

    public string ActiveRevision()
    {
        lock (_gate)
        {
            return CanonicalRevision.Compute(ReadSlot("published"));
        }
    }

    public string[] ReadLogTail(int lines)
    {
        lock (_gate)
        {
            lines = Math.Clamp(lines, 1, 1000);
            var path = LogPath();
            if (!File.Exists(path))
            {
                return [];
            }

            var all = File.ReadAllLines(path);
            return all.Skip(Math.Max(0, all.Length - lines)).ToArray();
        }
    }

    public void AppendLog(string message)
    {
        lock (_gate)
        {
            AppendLogUnlocked(message);
        }
    }

    private static DeviceDocument FindDevice(ConfigBundle bundle, string id)
    {
        EnsureSafeId(id);
        var device = bundle.Devices.FirstOrDefault(item => string.Equals(item.Metadata.Id, id, StringComparison.Ordinal));
        if (device is null)
        {
            throw new ConfigStoreException("device_not_found", "设备不存在", StatusCodes.Status404NotFound);
        }

        return device;
    }

    private static PointTemplateDocument FindTemplate(ConfigBundle bundle, string id)
    {
        EnsureSafeId(id);
        var template = bundle.PointTemplates.FirstOrDefault(item => string.Equals(item.Metadata.Id, id, StringComparison.Ordinal));
        if (template is null)
        {
            throw new ConfigStoreException("template_not_found", "点位模板不存在", StatusCodes.Status404NotFound);
        }

        return template;
    }

    public string ExportJson(bool published)
    {
        lock (_gate)
        {
            var bundle = published ? ReadSlot("published") : ReadSlot("draft");
            return JsonSerializer.Serialize(bundle, StudioJson.Options);
        }
    }

    public string ExportYaml(bool published)
    {
        lock (_gate)
        {
            var bundle = published ? ReadSlot("published") : ReadSlot("draft");
            return YamlFiles.Serialize(bundle);
        }
    }

    public void ImportJson(string json)
    {
        ConfigBundle bundle;
        try
        {
            bundle = JsonSerializer.Deserialize<ConfigBundle>(json, StudioJson.Options)
                ?? throw new InvalidOperationException("empty");
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException)
        {
            throw new ConfigStoreException("import_invalid", "无法解析 JSON 配置：" + ex.Message, StatusCodes.Status400BadRequest);
        }

        ImportBundle(bundle);
    }

    public void ImportYaml(string yaml)
    {
        ConfigBundle bundle;
        try
        {
            bundle = YamlFiles.Deserialize<ConfigBundle>(yaml);
        }
        catch (Exception ex) when (ex is not ConfigStoreException)
        {
            throw new ConfigStoreException("import_invalid", "无法解析 YAML 配置：" + ex.Message, StatusCodes.Status400BadRequest);
        }

        ImportBundle(bundle);
    }

    private void ImportBundle(ConfigBundle bundle)
    {
        lock (_gate)
        {
            YamlFiles.Normalize(bundle);
            SaveSlot("draft", bundle);
            AppendLogUnlocked("已导入草稿");
        }
    }

    private void EnsureBrandTemplatesUnlocked()
    {
        var published = ReadSlot("published");
        var draft = ReadSlot("draft");
        if (BrandTemplateSeeder.Ensure(published))
        {
            SaveSlot("published", published);
        }

        if (BrandTemplateSeeder.Ensure(draft))
        {
            SaveSlot("draft", draft);
        }
    }

    private ConfigBundle LoadInitialBundle(string? importPath)
    {
        var imported = TryReadImport(importPath);
        if (imported is not null)
        {
            return imported;
        }

        var legacy = Path.Combine(DataDirectory, "config");
        if (File.Exists(GatewayPath(_published)))
        {
            return ReadBundle(_published);
        }

        if (File.Exists(GatewayPath(legacy)))
        {
            return ReadBundle(legacy);
        }

        if (File.Exists(GatewayPath(_seed)))
        {
            return ReadBundle(_seed);
        }

        return ConfigDefaults.Create();
    }

    private static ConfigBundle? TryReadImport(string? importPath)
    {
        if (string.IsNullOrWhiteSpace(importPath))
        {
            return null;
        }

        var full = Path.GetFullPath(importPath);
        if (Directory.Exists(full) && File.Exists(GatewayPath(full)))
        {
            return ReadBundle(full);
        }

        if (!File.Exists(full))
        {
            return null;
        }

        var head = File.ReadLines(full).Take(40);
        var text = string.Join('\n', head);
        if (text.Contains("apiVersion:", StringComparison.Ordinal) && text.Contains("kind: Gateway", StringComparison.Ordinal))
        {
            var directory = Path.GetDirectoryName(full);
            return directory is null ? null : ReadBundle(directory);
        }

        return FromLegacyYaml(full);
    }

    private static ConfigBundle FromLegacyYaml(string path)
    {
        var deserializer = new YamlDotNet.Serialization.DeserializerBuilder()
            .WithNamingConvention(YamlDotNet.Serialization.NamingConventions.CamelCaseNamingConvention.Instance)
            .IgnoreUnmatchedProperties()
            .Build();
        var config = deserializer.Deserialize<Gateway.Abstractions.Configuration.GatewayConfiguration>(File.ReadAllText(path))
            ?? throw new ConfigStoreException("config_unreadable", $"无法解析 {path}", StatusCodes.Status500InternalServerError);
        var interval = (int)Math.Clamp(config.Pipeline.SweepInterval.TotalMilliseconds, 100, 86_400_000);
        var site = config.Gateway.Site;
        var bundle = new ConfigBundle
        {
            Gateway = new GatewayDocument
            {
                Metadata = new GatewayMetadata { SiteId = site, Name = string.IsNullOrWhiteSpace(config.Gateway.Id) ? site : config.Gateway.Id },
                Spec = new GatewaySpec
                {
                    LogLevel = "Information",
                    Features = new GatewayFeatures { ProgramWrite = config.ProgramTransfer.Enabled },
                    Acquisition = new AcquisitionSpec { DefaultIntervalMs = interval, ChangeOnly = config.Pipeline.ChangeOnly }
                }
            },
            Mqtt = new MqttSinkDocument
            {
                Spec = new MqttSinkSpec
                {
                    Broker = new MqttBrokerSpec
                    {
                        Host = config.Mqtt.Host,
                        Port = config.Mqtt.Port,
                        ClientId = config.Mqtt.ClientId,
                        Tls = config.Mqtt.Tls
                    },
                    Qos = config.Mqtt.Qos,
                    Retain = config.Mqtt.Retain,
                    TopicTemplate = config.Mqtt.TopicTemplate,
                    StatusTopic = config.Mqtt.StatusTopic,
                    ContractVersion = string.IsNullOrWhiteSpace(config.Mqtt.ContractVersion) ? "legacy" : config.Mqtt.ContractVersion
                }
            },
            Devices = config.Devices.Select(device => new DeviceDocument
            {
                Metadata = new DeviceMetadata
                {
                    Id = device.Id,
                    DisplayName = Option(device, "displayName") ?? device.Id
                },
                Spec = new DeviceSpec
                {
                    Adapter = device.Adapter,
                    Enabled = device.Enabled,
                    IntervalMs = interval,
                    PointTemplateId = device.Adapter.StartsWith("fanuc.", StringComparison.OrdinalIgnoreCase)
                        ? ConfigDefaults.DefaultFanucTemplateId
                        : null,
                    Connection = new DeviceConnection
                    {
                        Host = Option(device, "host") ?? "127.0.0.1",
                        Port = OptionInt(device, "port") ?? 8193,
                        TimeoutMs = OptionInt(device, "timeoutMs"),
                        FocasTimeoutMs = OptionInt(device, "timeoutMs")
                    }
                }
            }).ToList()
        };
        if (bundle.PointTemplates.Count == 0
            && bundle.Devices.Any(device => device.Spec.PointTemplateId == ConfigDefaults.DefaultFanucTemplateId))
        {
            bundle.PointTemplates.Add(ConfigDefaults.FanucTemplate());
        }

        return bundle;
    }

    private static string? Option(Gateway.Abstractions.Configuration.DeviceBinding device, string key)
    {
        if (!device.Options.TryGetValue(key, out var value) || value is null)
        {
            return null;
        }

        return Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture);
    }

    private static int? OptionInt(Gateway.Abstractions.Configuration.DeviceBinding device, string key)
    {
        var text = Option(device, key);
        return int.TryParse(text, out var number) ? number : null;
    }

    private static void PrepareImported(ConfigBundle bundle)
    {
        PointTemplateMigration.Apply(bundle);
        BrandTemplateSeeder.Ensure(bundle);
    }

    private static ConfigBundle CloneBundle(ConfigBundle bundle)
    {
        var json = JsonSerializer.Serialize(bundle, StudioJson.Options);
        return JsonSerializer.Deserialize<ConfigBundle>(json, StudioJson.Options) ?? new ConfigBundle();
    }

    private ConfigBundle ReadSlot(string slot)
    {
        if (!_database.TryLoadBundle(slot, out var bundle, out _))
        {
            throw new ConfigStoreException("config_unreadable", "数据库里没有这份配置", StatusCodes.Status500InternalServerError);
        }

        if (PointTemplateMigration.Apply(bundle))
        {
            SaveSlot(slot, bundle);
        }

        return bundle;
    }

    private void SaveSlot(string slot, ConfigBundle bundle)
    {
        YamlFiles.Normalize(bundle);
        _database.SaveBundle(slot, bundle, CanonicalRevision.Compute(bundle));
    }

    private static void EnsureSafeId(string id)
    {
        if (!ConfigValidator.IsSafeId(id))
        {
            throw new ConfigStoreException("id_invalid", "标识只能包含字母、数字、下划线和连字符", StatusCodes.Status400BadRequest);
        }
    }

    private static string NormalizeRevision(string? revision)
    {
        if (revision is null || !RevisionPattern().IsMatch(revision))
        {
            throw new ConfigStoreException("revision_invalid", "revision 必须是 64 位十六进制摘要", StatusCodes.Status400BadRequest);
        }

        return revision.ToLowerInvariant();
    }

    private static ConfigBundle ReadBundle(string root)
    {
        var gateway = YamlFiles.Normalize(new ConfigBundle
        {
            Gateway = YamlFiles.Read<GatewayDocument>(GatewayPath(root)),
            Devices = ReadMany<DeviceDocument>(Path.Combine(root, "devices")),
            PointTemplates = ReadMany<PointTemplateDocument>(Path.Combine(root, "point-templates")),
            PointSets = ReadMany<PointSetDocument>(Path.Combine(root, "points")),
            Mqtt = File.Exists(MqttPath(root))
                ? YamlFiles.Read<MqttSinkDocument>(MqttPath(root))
                : ConfigDefaults.Mqtt()
        });
        return gateway;
    }

    private static List<T> ReadMany<T>(string directory)
    {
        if (!Directory.Exists(directory))
        {
            return [];
        }

        return Directory.GetFiles(directory, "*.yaml")
            .Order(StringComparer.Ordinal)
            .Select(YamlFiles.Read<T>)
            .ToList();
    }

    private void AppendLogUnlocked(string message)
    {
        var clean = message.Replace('\r', ' ').Replace('\n', ' ');
        var line = $"{DateTimeOffset.UtcNow:yyyy-MM-dd HH:mm:ss}Z {clean}";
        File.AppendAllText(LogPath(), line + "\n");
    }

    private string LogPath() => Path.Combine(_runtime, "studio.log");

    private static string GatewayPath(string root) => Path.Combine(root, "gateway.yaml");

    private static string MqttPath(string root) => Path.Combine(root, "sinks", "mqtt.yaml");

    private static string? BlankToNull(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string CanonicalLogLevel(string? level)
    {
        var match = new[] { "Trace", "Debug", "Information", "Warning", "Error", "Critical" }
            .FirstOrDefault(item => string.Equals(item, level?.Trim(), StringComparison.OrdinalIgnoreCase));
        return match ?? (level ?? "").Trim();
    }

    [GeneratedRegex("^[a-fA-F0-9]{64}$")]
    private static partial Regex RevisionPattern();

}

public sealed class PublishOutcome
{
    public bool Published { get; init; }

    public bool Unchanged { get; init; }

    public string Revision { get; init; } = "";

    public DateTimeOffset PublishedAt { get; init; }

    public List<ValidationIssue> Issues { get; init; } = [];
}

public sealed class RollbackOutcome
{
    public string Revision { get; init; } = "";

    public DateTimeOffset RolledBackAt { get; init; }
}

public sealed class ConfigStoreException : Exception
{
    public ConfigStoreException(string code, string message, int statusCode)
        : base(message)
    {
        Code = code;
        StatusCode = statusCode;
    }

    public string Code { get; }

    public int StatusCode { get; }
}
