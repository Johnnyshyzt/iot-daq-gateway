using System.Text.Json;
using System.Text.RegularExpressions;
using Studio.Contracts;

namespace Studio.Host.Config;

public sealed partial class ConfigStore
{
    private const int MaxRevisions = 30;
    private readonly object _gate = new();
    private readonly string _seed;
    private readonly string _draft;
    private readonly string _published;
    private readonly string _revisions;
    private readonly string _runtime;

    private static readonly JsonSerializerOptions IndexOptions = new(StudioJson.Options)
    {
        WriteIndented = true
    };

    public ConfigStore(string dataDirectory)
    {
        DataDirectory = dataDirectory;
        _seed = Path.Combine(dataDirectory, "seed");
        _draft = Path.Combine(dataDirectory, "draft");
        _published = Path.Combine(dataDirectory, "config");
        _revisions = Path.Combine(dataDirectory, "revisions");
        _runtime = Path.Combine(dataDirectory, "runtime");
    }

    public string DataDirectory { get; }

    public void EnsureInitialized()
    {
        lock (_gate)
        {
            Directory.CreateDirectory(_draft);
            Directory.CreateDirectory(_published);
            Directory.CreateDirectory(_revisions);
            Directory.CreateDirectory(_runtime);

            if (!File.Exists(GatewayPath(_published)))
            {
                if (File.Exists(GatewayPath(_seed)))
                {
                    CopyBundleFiles(_seed, _published);
                    CopyBundleFiles(_seed, _draft);
                }
                else
                {
                    var defaults = ConfigDefaults.Create();
                    WriteBundle(_published, defaults);
                    WriteBundle(_draft, defaults);
                }
            }
            else if (!File.Exists(GatewayPath(_draft)))
            {
                CopyBundleFiles(_published, _draft);
            }

            var published = ReadBundle(_published);
            var hash = CanonicalRevision.Compute(published);
            var revisionFile = RevisionPath(_published);
            var recorded = File.Exists(revisionFile) ? File.ReadAllText(revisionFile).Trim() : "";
            if (!string.Equals(recorded, hash, StringComparison.Ordinal))
            {
                File.WriteAllText(revisionFile, hash + "\n");
            }

            var index = ReadIndex();
            if (index.Items.All(item => item.Revision != hash))
            {
                WriteBundle(SnapshotDir(hash), published);
                index.Items.Insert(0, new RevisionInfo
                {
                    Revision = hash,
                    CreatedAt = DateTimeOffset.UtcNow,
                    Action = recorded.Length == 0 ? "seed" : "sync",
                    Note = recorded.Length == 0 ? "初始配置" : "与已发布文件对齐"
                });
                Trim(index, hash);
                WriteIndex(index);
            }

            AppendLogUnlocked("Config Studio 已启动");
        }
    }

    public ConfigView GetView()
    {
        lock (_gate)
        {
            var draft = ReadBundle(_draft);
            var published = ReadBundle(_published);
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
            return ConfigDiffer.Compare(ReadBundle(_published), ReadBundle(_draft));
        }
    }

    public IReadOnlyList<DeviceDocument> ListDevices()
    {
        lock (_gate)
        {
            return ReadBundle(_draft).Devices
                .OrderBy(device => device.Metadata.Id, StringComparer.Ordinal)
                .ToList();
        }
    }

    public DeviceDocument GetDevice(string id)
    {
        lock (_gate)
        {
            return FindDevice(ReadBundle(_draft), id);
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

            var bundle = ReadBundle(_draft);
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

            if (bundle.PointSets.All(set => !string.Equals(set.Metadata.DeviceId, id, StringComparison.Ordinal)))
            {
                bundle.PointSets.Add(ConfigDefaults.DefaultPoints(id));
            }

            WriteBundle(_draft, bundle);
            AppendLogUnlocked($"草稿已更新设备 {id}");
            return document;
        }
    }

    public void DeleteDevice(string id)
    {
        lock (_gate)
        {
            EnsureSafeId(id);
            var bundle = ReadBundle(_draft);
            var removed = bundle.Devices.RemoveAll(device => string.Equals(device.Metadata.Id, id, StringComparison.Ordinal));
            if (removed == 0)
            {
                throw new ConfigStoreException("device_not_found", "设备不存在", StatusCodes.Status404NotFound);
            }

            bundle.PointSets.RemoveAll(set => string.Equals(set.Metadata.DeviceId, id, StringComparison.Ordinal));
            WriteBundle(_draft, bundle);
            AppendLogUnlocked($"草稿已删除设备 {id}");
        }
    }

    public PointSetDocument GetPoints(string deviceId)
    {
        lock (_gate)
        {
            var bundle = ReadBundle(_draft);
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
            var bundle = ReadBundle(_draft);
            FindDevice(bundle, deviceId);
            document.ApiVersion = StudioApi.Version;
            document.Kind = "PointSet";
            document.Metadata ??= new PointSetMetadata();
            document.Metadata.DeviceId = deviceId;
            document.Spec ??= new PointSetSpec();
            document.Spec.Points ??= [];
            foreach (var point in document.Spec.Points)
            {
                point.Id = (point.Id ?? "").Trim();
                point.DataType = (point.DataType ?? "").Trim().ToLowerInvariant();
                point.Address = (point.Address ?? "").Trim();
                point.Unit ??= "";
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

            WriteBundle(_draft, bundle);
            AppendLogUnlocked($"草稿已更新点位 {deviceId}");
            return document;
        }
    }

    public MqttSinkDocument GetMqtt()
    {
        lock (_gate)
        {
            return ReadBundle(_draft).Mqtt;
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
            var bundle = ReadBundle(_draft);
            bundle.Mqtt = document;
            WriteBundle(_draft, bundle);
            AppendLogUnlocked("草稿已更新 MQTT");
            return document;
        }
    }

    public GatewayDocument GetGateway()
    {
        lock (_gate)
        {
            return ReadBundle(_draft).Gateway;
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
            var bundle = ReadBundle(_draft);
            bundle.Gateway = document;
            WriteBundle(_draft, bundle);
            AppendLogUnlocked("草稿已更新网关设置");
            return document;
        }
    }

    public ValidationResult Validate()
    {
        lock (_gate)
        {
            return ConfigValidator.Validate(ReadBundle(_draft));
        }
    }

    public PublishOutcome Publish(string? note)
    {
        lock (_gate)
        {
            var draft = ReadBundle(_draft);
            var validation = ConfigValidator.Validate(draft);
            if (!validation.Valid)
            {
                return new PublishOutcome { Issues = validation.Issues };
            }

            var hash = CanonicalRevision.Compute(draft);
            var current = CanonicalRevision.Compute(ReadBundle(_published));
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

            WriteBundle(SnapshotDir(hash), draft);
            WriteBundle(_published, draft);
            File.WriteAllText(RevisionPath(_published), hash + "\n");
            var index = ReadIndex();
            index.Items.Insert(0, new RevisionInfo
            {
                Revision = hash,
                CreatedAt = now,
                Action = "publish",
                Note = string.IsNullOrWhiteSpace(note) ? null : note.Trim()
            });
            Trim(index, hash);
            WriteIndex(index);
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
            take = Math.Clamp(take, 1, 100);
            return ReadIndex().Items.Take(take).ToList();
        }
    }

    public RollbackOutcome Rollback(string revision)
    {
        lock (_gate)
        {
            var hash = NormalizeRevision(revision);
            var directory = SnapshotDir(hash);
            if (!File.Exists(GatewayPath(directory)))
            {
                throw new ConfigStoreException("revision_not_found", "找不到该修订", StatusCodes.Status404NotFound);
            }

            var bundle = ReadBundle(directory);
            WriteBundle(_published, bundle);
            WriteBundle(_draft, bundle);
            File.WriteAllText(RevisionPath(_published), hash + "\n");
            var now = DateTimeOffset.UtcNow;
            var index = ReadIndex();
            index.Items.Insert(0, new RevisionInfo
            {
                Revision = hash,
                CreatedAt = now,
                Action = "rollback",
                Note = "回滚到该修订"
            });
            Trim(index, hash);
            WriteIndex(index);
            AppendLogUnlocked($"已回滚到 {hash[..12]}");
            return new RollbackOutcome { Revision = hash, RolledBackAt = now };
        }
    }

    public ConfigBundle ReadPublished()
    {
        lock (_gate)
        {
            return ReadBundle(_published);
        }
    }

    public string ActiveRevision()
    {
        lock (_gate)
        {
            return CanonicalRevision.Compute(ReadBundle(_published));
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

    private ConfigBundle ReadBundle(string root)
    {
        var gateway = YamlFiles.Normalize(new ConfigBundle
        {
            Gateway = YamlFiles.Read<GatewayDocument>(GatewayPath(root)),
            Devices = ReadMany<DeviceDocument>(Path.Combine(root, "devices")),
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

    private static void WriteBundle(string root, ConfigBundle bundle)
    {
        YamlFiles.Normalize(bundle);
        Directory.CreateDirectory(root);
        YamlFiles.Write(GatewayPath(root), bundle.Gateway);
        ReplaceYaml(Path.Combine(root, "devices"), bundle.Devices.Select(device => (device.Metadata.Id, device)));
        ReplaceYaml(Path.Combine(root, "points"), bundle.PointSets.Select(set => (set.Metadata.DeviceId, set)));
        YamlFiles.Write(MqttPath(root), bundle.Mqtt);
    }

    private static void ReplaceYaml<T>(string directory, IEnumerable<(string Id, T Document)> documents)
    {
        Directory.CreateDirectory(directory);
        var keep = new HashSet<string>(StringComparer.Ordinal);
        foreach (var (id, document) in documents)
        {
            if (!ConfigValidator.IsSafeId(id))
            {
                throw new ConfigStoreException("id_invalid", "配置里有不能作为文件名的标识", StatusCodes.Status400BadRequest);
            }

            var name = id + ".yaml";
            keep.Add(name);
            YamlFiles.Write(Path.Combine(directory, name), document);
        }

        foreach (var file in Directory.GetFiles(directory, "*.yaml"))
        {
            if (!keep.Contains(Path.GetFileName(file)))
            {
                File.Delete(file);
            }
        }
    }

    private static void CopyBundleFiles(string from, string to)
    {
        Directory.CreateDirectory(to);
        File.Copy(GatewayPath(from), GatewayPath(to), overwrite: true);
        CopyYamlDirectory(Path.Combine(from, "devices"), Path.Combine(to, "devices"));
        CopyYamlDirectory(Path.Combine(from, "points"), Path.Combine(to, "points"));
        Directory.CreateDirectory(Path.Combine(to, "sinks"));
        if (File.Exists(MqttPath(from)))
        {
            File.Copy(MqttPath(from), MqttPath(to), overwrite: true);
        }
    }

    private static void CopyYamlDirectory(string from, string to)
    {
        Directory.CreateDirectory(to);
        foreach (var file in Directory.Exists(to) ? Directory.GetFiles(to, "*.yaml") : [])
        {
            File.Delete(file);
        }

        if (!Directory.Exists(from))
        {
            return;
        }

        foreach (var file in Directory.GetFiles(from, "*.yaml"))
        {
            File.Copy(file, Path.Combine(to, Path.GetFileName(file)), overwrite: true);
        }
    }

    private RevisionIndex ReadIndex()
    {
        var path = Path.Combine(_revisions, "index.json");
        if (!File.Exists(path))
        {
            return new RevisionIndex();
        }

        try
        {
            var json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<RevisionIndex>(json, IndexOptions) ?? new RevisionIndex();
        }
        catch (Exception ex)
        {
            throw new ConfigStoreException("config_unreadable", $"无法读取修订索引: {ex.Message}", StatusCodes.Status500InternalServerError);
        }
    }

    private void WriteIndex(RevisionIndex index)
    {
        var path = Path.Combine(_revisions, "index.json");
        File.WriteAllText(path, JsonSerializer.Serialize(index, IndexOptions) + "\n");
    }

    private void Trim(RevisionIndex index, string active)
    {
        index.Items = index.Items.Take(MaxRevisions).ToList();
        var referenced = index.Items.Select(item => item.Revision).Append(active).ToHashSet(StringComparer.Ordinal);
        if (!Directory.Exists(_revisions))
        {
            return;
        }

        foreach (var directory in Directory.GetDirectories(_revisions))
        {
            var name = Path.GetFileName(directory);
            if (!referenced.Contains(name))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    private void AppendLogUnlocked(string message)
    {
        var clean = message.Replace('\r', ' ').Replace('\n', ' ');
        var line = $"{DateTimeOffset.UtcNow:yyyy-MM-dd HH:mm:ss}Z {clean}";
        File.AppendAllText(LogPath(), line + "\n");
    }

    private string SnapshotDir(string hash) => Path.Combine(_revisions, hash);

    private string LogPath() => Path.Combine(_runtime, "studio.log");

    private static string GatewayPath(string root) => Path.Combine(root, "gateway.yaml");

    private static string MqttPath(string root) => Path.Combine(root, "sinks", "mqtt.yaml");

    private static string RevisionPath(string root) => Path.Combine(root, ".revision");

    private static string? BlankToNull(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string CanonicalLogLevel(string? level)
    {
        var match = new[] { "Trace", "Debug", "Information", "Warning", "Error", "Critical" }
            .FirstOrDefault(item => string.Equals(item, level?.Trim(), StringComparison.OrdinalIgnoreCase));
        return match ?? (level ?? "").Trim();
    }

    [GeneratedRegex("^[a-fA-F0-9]{64}$")]
    private static partial Regex RevisionPattern();

    private sealed class RevisionIndex
    {
        public List<RevisionInfo> Items { get; set; } = [];
    }
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
