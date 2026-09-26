using System.Globalization;
using System.Text.Json;
using Cnc.Catalog;
using Gateway.Abstractions.Contracts;
using Gateway.Abstractions.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Studio.Contracts;

namespace IotDaq.Persistence;

/// <summary>
/// Opens the edge database, seeds the CNC catalog, mirrors draft/published config,
/// and stores samples. SQLite is the default file under the data directory.
/// PostgreSQL is selected with Database:Provider=Postgres.
/// </summary>
public sealed class GatewayPersistence : ISampleWriter
{
    public const int SchemaVersion = 1;

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly object _gate = new();
    private readonly DbContextOptions<GatewayDbContext> _options;
    private readonly bool _sqlite;

    private GatewayPersistence(DbContextOptions<GatewayDbContext> options, bool sqlite, string provider, int historyRetentionDays)
    {
        _options = options;
        _sqlite = sqlite;
        Provider = provider;
        HistoryRetentionDays = historyRetentionDays;
    }

    public string Provider { get; }

    public int HistoryRetentionDays { get; }

    public static GatewayPersistence Open(string dataDirectory, IConfiguration? configuration)
    {
        var provider = configuration?["Database:Provider"]
            ?? Environment.GetEnvironmentVariable("DATABASE_PROVIDER")
            ?? "Sqlite";
        var connection = configuration?["Database:ConnectionString"]
            ?? Environment.GetEnvironmentVariable("DATABASE_CONNECTION");
        var retentionText = configuration?["Database:HistoryRetentionDays"]
            ?? Environment.GetEnvironmentVariable("DATABASE_HISTORY_DAYS");
        var retention = int.TryParse(retentionText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var days)
            ? Math.Clamp(days, 1, 3650)
            : 14;
        return Open(dataDirectory, provider, connection, retention);
    }

    public static GatewayPersistence Open(string dataDirectory, string provider, string? connectionString, int historyRetentionDays = 14)
    {
        var connection = connectionString;
        var retention = Math.Clamp(historyRetentionDays, 1, 3650);
        var builder = new DbContextOptionsBuilder<GatewayDbContext>();
        if (IsPostgres(provider))
        {
            if (string.IsNullOrWhiteSpace(connection))
            {
                throw new InvalidOperationException("PostgreSQL 需要 Database:ConnectionString 或环境变量 DATABASE_CONNECTION。");
            }

            builder.UseNpgsql(connection);
            return new GatewayPersistence(builder.Options, sqlite: false, "Postgres", retention);
        }

        Directory.CreateDirectory(dataDirectory);
        var path = Path.Combine(dataDirectory, "gateway.db");
        builder.UseSqlite($"Data Source={path};Cache=Shared;Default Timeout=5");
        return new GatewayPersistence(builder.Options, sqlite: true, "Sqlite", retention);
    }

    public GatewayDbContext CreateContext() => new(_options);

    public void EnsureReady()
    {
        lock (_gate)
        {
            using var db = CreateContext();
            db.Database.EnsureCreated();
            if (_sqlite)
            {
                db.Database.ExecuteSqlRaw("PRAGMA journal_mode=WAL;");
            }

            var info = db.SchemaInfo.FirstOrDefault(row => row.Id == 1);
            if (info is null)
            {
                db.SchemaInfo.Add(new SchemaInfoRow { Id = 1, Version = SchemaVersion, Provider = Provider });
                db.SaveChanges();
            }

            SeedCatalog(db);
        }
    }

    public bool HasSlot(string slot)
    {
        lock (_gate)
        {
            using var db = CreateContext();
            return db.ConfigBundles.Any(row => row.Slot == slot);
        }
    }

    public void SaveBundle(string slot, ConfigBundle bundle, string hash)
    {
        EnsureReady();
        lock (_gate)
        {
            using var db = CreateContext();
            using var tx = db.Database.BeginTransaction();
            ReplaceSlot(db, slot, bundle, hash);
            db.SaveChanges();
            tx.Commit();
        }
    }

    public void SaveRevision(string revision, DateTimeOffset createdAt, string action, string? note, ConfigBundle bundle)
    {
        if (string.IsNullOrWhiteSpace(revision))
        {
            return;
        }

        EnsureReady();
        lock (_gate)
        {
            using var db = CreateContext();
            var existing = db.ConfigRevisions.FirstOrDefault(row => row.Revision == revision);
            if (existing is null)
            {
                db.ConfigRevisions.Add(new ConfigRevisionRow
                {
                    Revision = revision,
                    CreatedUnixMs = createdAt.ToUnixTimeMilliseconds(),
                    Action = action,
                    Note = note,
                    BundleJson = JsonSerializer.Serialize(bundle, Json)
                });
            }
            else if (!string.Equals(existing.Action, action, StringComparison.Ordinal))
            {
                db.ConfigRevisions.Add(new ConfigRevisionRow
                {
                    Revision = revision + ":" + action + ":" + createdAt.ToUnixTimeMilliseconds().ToString(CultureInfo.InvariantCulture),
                    CreatedUnixMs = createdAt.ToUnixTimeMilliseconds(),
                    Action = action,
                    Note = note,
                    BundleJson = existing.BundleJson
                });
            }

            db.SaveChanges();
        }
    }

    public int CountBrands()
    {
        EnsureReady();
        lock (_gate)
        {
            using var db = CreateContext();
            return db.Brands.Count();
        }
    }

    public CatalogOverview ReadCatalog()
    {
        EnsureReady();
        lock (_gate)
        {
            using var db = CreateContext();
            var meta = db.CatalogMeta.First(row => row.Id == 1);
            var items = db.CatalogItems.AsNoTracking().OrderBy(row => row.Id).ToList();
            var itemById = items.ToDictionary(row => row.Id, StringComparer.OrdinalIgnoreCase);
            var mappings = db.BrandItems.AsNoTracking().ToList();
            var models = db.ControllerModels.AsNoTracking().ToList();
            var adapters = db.Adapters.AsNoTracking().OrderBy(row => row.Id).ToList();
            var brands = db.Brands.AsNoTracking().OrderBy(row => row.SortOrder).Select(row => new CatalogBrandView
            {
                Id = row.Id,
                NameZh = row.NameZh,
                NameEn = row.NameEn
            }).ToList();

            foreach (var brand in brands)
            {
                brand.Models = models.Where(row => row.BrandId == brand.Id)
                    .Select(row => new CatalogModelView { Id = row.Id, Name = row.Name })
                    .ToList();
                brand.Adapters = adapters.Where(row => string.Equals(row.BrandId, brand.Id, StringComparison.Ordinal))
                    .Select(ToAdapterView)
                    .ToList();
                brand.Items = mappings.Where(row => row.BrandId == brand.Id)
                    .GroupBy(row => row.ItemId, StringComparer.OrdinalIgnoreCase)
                    .Select(group =>
                    {
                        itemById.TryGetValue(group.Key, out var item);
                        return new CatalogItemView
                        {
                            Id = group.Key,
                            NameZh = item?.NameZh ?? group.Key,
                            DataType = item?.DataType ?? "string",
                            Unit = item?.Unit ?? "",
                            Category = item?.Category ?? "",
                            BrandSpecific = group.Any(row => row.BrandSpecific),
                            Sources = group.Select(row => row.SourceName).Distinct().ToList()
                        };
                    })
                    .OrderBy(row => row.NameZh, StringComparer.Ordinal)
                    .ToList();
            }

            return new CatalogOverview
            {
                Version = meta.Version,
                Source = meta.Source,
                Brands = brands,
                Items = items.Where(row => !row.BrandSpecific).Select(row => new CatalogItemView
                {
                    Id = row.Id,
                    NameZh = row.NameZh,
                    DataType = row.DataType,
                    Unit = row.Unit,
                    Category = row.Category,
                    BrandSpecific = false
                }).ToList(),
                GenericAdapters = adapters.Where(row => row.BrandId == null).Select(ToAdapterView).ToList()
            };
        }
    }

    public void Write(IReadOnlyList<Observation> observations)
    {
        if (observations.Count == 0)
        {
            return;
        }

        EnsureReady();
        lock (_gate)
        {
            using var db = CreateContext();
            foreach (var observation in observations)
            {
                var text = FormatValue(observation.Value);
                var numeric = ToNumber(observation.Value);
                var when = observation.Timestamp.ToUnixTimeMilliseconds();
                var latest = db.SampleLatest.FirstOrDefault(row =>
                    row.DeviceId == observation.DeviceId && row.PointId == observation.Point);
                if (latest is null)
                {
                    latest = new SampleLatestRow
                    {
                        DeviceId = observation.DeviceId,
                        PointId = observation.Point
                    };
                    db.SampleLatest.Add(latest);
                }

                latest.ValueText = text;
                latest.NumericValue = numeric;
                latest.Quality = observation.Quality;
                latest.Unit = observation.Unit;
                latest.TimestampUnixMs = when;
                db.SampleHistory.Add(new SampleHistoryRow
                {
                    Id = Guid.NewGuid().ToString("N"),
                    DeviceId = observation.DeviceId,
                    PointId = observation.Point,
                    ValueText = text,
                    NumericValue = numeric,
                    Quality = observation.Quality,
                    Unit = observation.Unit,
                    TimestampUnixMs = when
                });
                RecordAlarm(db, observation, text, when);
            }

            db.SaveChanges();
        }
    }

    public IReadOnlyList<SampleView> Latest(string deviceId)
    {
        EnsureReady();
        lock (_gate)
        {
            using var db = CreateContext();
            return db.SampleLatest.AsNoTracking()
                .Where(row => row.DeviceId == deviceId)
                .OrderBy(row => row.PointId)
                .Select(row => new SampleView
                {
                    DeviceId = row.DeviceId,
                    PointId = row.PointId,
                    Value = row.ValueText,
                    NumericValue = row.NumericValue,
                    Quality = row.Quality,
                    Unit = row.Unit,
                    TimestampUnixMs = row.TimestampUnixMs
                })
                .ToList();
        }
    }

    public IReadOnlyList<SampleView> History(string deviceId, IReadOnlyCollection<string> items, long fromUnixMs, long toUnixMs, long bucketMs)
    {
        EnsureReady();
        lock (_gate)
        {
            using var db = CreateContext();
            var query = db.SampleHistory.AsNoTracking()
                .Where(row => row.DeviceId == deviceId && row.TimestampUnixMs >= fromUnixMs && row.TimestampUnixMs <= toUnixMs);
            if (items.Count > 0)
            {
                query = query.Where(row => items.Contains(row.PointId));
            }

            var rows = query
                .OrderBy(row => row.TimestampUnixMs)
                .Take(20000)
                .Select(row => new SampleView
                {
                    DeviceId = row.DeviceId,
                    PointId = row.PointId,
                    Value = row.ValueText,
                    NumericValue = row.NumericValue,
                    Quality = row.Quality,
                    Unit = row.Unit,
                    TimestampUnixMs = row.TimestampUnixMs
                })
                .ToList();
            if (bucketMs <= 0)
            {
                return rows;
            }

            return rows
                .GroupBy(row => (row.PointId, Bucket: row.TimestampUnixMs / bucketMs))
                .Select(group => group.Last())
                .OrderBy(row => row.TimestampUnixMs)
                .ToList();
        }
    }

    public IReadOnlyList<AlarmView> ListAlarms(string? deviceId, int limit)
    {
        EnsureReady();
        limit = Math.Clamp(limit, 1, 500);
        lock (_gate)
        {
            using var db = CreateContext();
            var query = db.Alarms.AsNoTracking().AsQueryable();
            if (!string.IsNullOrWhiteSpace(deviceId))
            {
                query = query.Where(row => row.DeviceId == deviceId);
            }

            return query.OrderByDescending(row => row.RaisedUnixMs).Take(limit).Select(row => new AlarmView
            {
                Id = row.Id,
                DeviceId = row.DeviceId,
                PointId = row.PointId,
                Message = row.Message,
                Severity = row.Severity,
                Active = row.Active,
                RaisedUnixMs = row.RaisedUnixMs
            }).ToList();
        }
    }

    public int PurgeOlderThan(DateTimeOffset cutoff)
    {
        EnsureReady();
        var unix = cutoff.ToUnixTimeMilliseconds();
        lock (_gate)
        {
            using var db = CreateContext();
            var samples = db.SampleHistory.Where(row => row.TimestampUnixMs < unix).ExecuteDelete();
            var alarms = db.Alarms.Where(row => row.RaisedUnixMs < unix && !row.Active).ExecuteDelete();
            return samples + alarms;
        }
    }

    public void SyncUsers(string accountsPath)
    {
        if (!File.Exists(accountsPath))
        {
            return;
        }

        EnsureReady();
        AccountFileDto? file;
        try
        {
            file = JsonSerializer.Deserialize<AccountFileDto>(File.ReadAllText(accountsPath), Json);
        }
        catch (JsonException)
        {
            return;
        }

        if (file?.Users is null)
        {
            return;
        }

        lock (_gate)
        {
            using var db = CreateContext();
            foreach (var user in file.Users)
            {
                if (string.IsNullOrWhiteSpace(user.Username))
                {
                    continue;
                }

                var row = db.Users.FirstOrDefault(item => item.Username == user.Username);
                if (row is null)
                {
                    row = new UserRow { Username = user.Username };
                    db.Users.Add(row);
                }

                row.Role = user.Role ?? "";
                row.PasswordHash = user.PasswordHash ?? "";
                row.MustChangePassword = user.MustChangePassword;
                row.Mode = file.Mode ?? "";
            }

            db.SaveChanges();
        }
    }

    public IReadOnlyList<DeviceGroupRow> ListGroups()
    {
        EnsureReady();
        lock (_gate)
        {
            using var db = CreateContext();
            return db.DeviceGroups.AsNoTracking().OrderBy(row => row.Id).ToList();
        }
    }

    public DeviceGroupRow UpsertGroup(string id, string name, string workshop, string line)
    {
        EnsureReady();
        lock (_gate)
        {
            using var db = CreateContext();
            var row = db.DeviceGroups.FirstOrDefault(item => item.Id == id);
            if (row is null)
            {
                row = new DeviceGroupRow { Id = id };
                db.DeviceGroups.Add(row);
            }

            row.Name = name;
            row.Workshop = workshop;
            row.Line = line;
            db.SaveChanges();
            return row;
        }
    }

    public void DeleteGroup(string id)
    {
        EnsureReady();
        lock (_gate)
        {
            using var db = CreateContext();
            var row = db.DeviceGroups.FirstOrDefault(item => item.Id == id);
            if (row is not null)
            {
                db.DeviceGroups.Remove(row);
                db.SaveChanges();
            }
        }
    }

    private void SeedCatalog(GatewayDbContext db)
    {
        var catalog = CncCatalog.Current;
        var meta = db.CatalogMeta.FirstOrDefault(row => row.Id == 1);
        if (meta is not null && string.Equals(meta.Version, catalog.Version, StringComparison.Ordinal))
        {
            return;
        }

        db.BrandItems.ExecuteDelete();
        db.ControllerModels.ExecuteDelete();
        db.Adapters.ExecuteDelete();
        db.CatalogItems.ExecuteDelete();
        db.Brands.ExecuteDelete();

        var order = 0;
        foreach (var brand in catalog.Brands)
        {
            db.Brands.Add(new BrandRow
            {
                Id = brand.Id,
                NameZh = brand.NameZh,
                NameEn = brand.NameEn,
                SortOrder = order++
            });
            foreach (var model in brand.Models)
            {
                db.ControllerModels.Add(new ControllerModelRow
                {
                    Id = model.Id,
                    BrandId = brand.Id,
                    Name = model.Name
                });
            }

            var seenSources = new HashSet<string>(StringComparer.Ordinal);
            foreach (var mapping in brand.Mappings)
            {
                if (!seenSources.Add(mapping.Source))
                {
                    continue;
                }

                db.BrandItems.Add(new BrandItemRow
                {
                    BrandId = brand.Id,
                    SourceName = mapping.Source,
                    ItemId = mapping.ItemId,
                    BrandSpecific = mapping.BrandSpecific
                });
            }

            foreach (var adapter in brand.Adapters)
            {
                db.Adapters.Add(ToAdapterRow(adapter));
            }
        }

        foreach (var adapter in catalog.GenericAdapters)
        {
            db.Adapters.Add(ToAdapterRow(adapter));
        }

        foreach (var item in catalog.Items)
        {
            db.CatalogItems.Add(new CatalogItemRow
            {
                Id = item.Id,
                NameZh = item.NameZh,
                DataType = item.DataType,
                Unit = item.Unit,
                Category = item.Category,
                BrandSpecific = item.BrandSpecific,
                BrandId = item.BrandId
            });
        }

        if (meta is null)
        {
            db.CatalogMeta.Add(new CatalogMetaRow
            {
                Id = 1,
                Version = catalog.Version,
                Source = catalog.Source
            });
        }
        else
        {
            meta.Version = catalog.Version;
            meta.Source = catalog.Source;
        }

        db.SaveChanges();
    }

    private static void ReplaceSlot(GatewayDbContext db, string slot, ConfigBundle bundle, string hash)
    {
        var previous = db.ConfigBundles.FirstOrDefault(row => row.Slot == slot);
        if (previous is null)
        {
            db.ConfigBundles.Add(new ConfigBundleRow
            {
                Slot = slot,
                Hash = hash,
                Json = JsonSerializer.Serialize(bundle, Json),
                UpdatedUnixMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
            });
        }
        else
        {
            previous.Hash = hash;
            previous.Json = JsonSerializer.Serialize(bundle, Json);
            previous.UpdatedUnixMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        }

        var gateway = bundle.Gateway;
        var gatewayRow = db.ConfigGateways.FirstOrDefault(row => row.Slot == slot);
        if (gatewayRow is null)
        {
            gatewayRow = new ConfigGatewayRow { Slot = slot };
            db.ConfigGateways.Add(gatewayRow);
        }

        gatewayRow.SiteId = gateway.Metadata.SiteId;
        gatewayRow.Name = gateway.Metadata.Name;
        gatewayRow.LogLevel = gateway.Spec.LogLevel;
        gatewayRow.ProgramWrite = gateway.Spec.Features.ProgramWrite;
        gatewayRow.DefaultIntervalMs = gateway.Spec.Acquisition.DefaultIntervalMs;
        gatewayRow.ChangeOnly = gateway.Spec.Acquisition.ChangeOnly;

        db.ConfigDevices.RemoveRange(db.ConfigDevices.Where(row => row.Slot == slot).ToList());
        db.ConfigTemplates.RemoveRange(db.ConfigTemplates.Where(row => row.Slot == slot).ToList());
        db.ConfigTemplatePoints.RemoveRange(db.ConfigTemplatePoints.Where(row => row.Slot == slot).ToList());
        db.ConfigPointSets.RemoveRange(db.ConfigPointSets.Where(row => row.Slot == slot).ToList());
        foreach (var device in bundle.Devices)
        {
            var connection = device.Spec.Connection;
            db.ConfigDevices.Add(new ConfigDeviceRow
            {
                Slot = slot,
                Id = device.Metadata.Id,
                DisplayName = device.Metadata.DisplayName,
                BrandId = device.Spec.BrandId,
                ControllerModelId = device.Spec.ControllerModelId,
                Adapter = device.Spec.Adapter,
                Enabled = device.Spec.Enabled,
                IntervalMs = device.Spec.IntervalMs,
                PointTemplateId = device.Spec.PointTemplateId,
                Host = connection.Host,
                Port = connection.Port,
                TimeoutMs = connection.TimeoutMs ?? connection.FocasTimeoutMs,
                Path = connection.Path,
                Namespace = connection.Namespace,
                Workshop = device.Spec.Workshop,
                Line = device.Spec.Line,
                GroupId = device.Spec.GroupId
            });
            RememberGroup(db, device);
        }

        foreach (var template in bundle.PointTemplates)
        {
            db.ConfigTemplates.Add(new ConfigTemplateRow
            {
                Slot = slot,
                Id = template.Metadata.Id,
                DisplayName = template.Metadata.DisplayName,
                Adapter = template.Spec.Adapter
            });
            var sort = 0;
            foreach (var point in template.Spec.Points)
            {
                db.ConfigTemplatePoints.Add(new ConfigTemplatePointRow
                {
                    Slot = slot,
                    TemplateId = template.Metadata.Id,
                    PointId = point.Id,
                    Address = point.Address,
                    DataType = point.DataType,
                    Unit = point.Unit,
                    Scale = point.Scale,
                    Deadband = point.Deadband,
                    Enabled = point.Enabled,
                    SortOrder = sort++
                });
            }
        }

        foreach (var set in bundle.PointSets)
        {
            var sort = 0;
            foreach (var point in set.Spec.Points)
            {
                db.ConfigPointSets.Add(new ConfigPointSetRow
                {
                    Slot = slot,
                    DeviceId = set.Metadata.DeviceId,
                    PointId = point.Id,
                    Address = point.Address,
                    DataType = point.DataType,
                    Unit = point.Unit,
                    Scale = point.Scale,
                    Deadband = point.Deadband,
                    Enabled = point.Enabled,
                    SortOrder = sort++
                });
            }
        }

        var mqtt = bundle.Mqtt.Spec;
        var mqttRow = db.ConfigMqtt.FirstOrDefault(row => row.Slot == slot);
        if (mqttRow is null)
        {
            mqttRow = new ConfigMqttRow { Slot = slot };
            db.ConfigMqtt.Add(mqttRow);
        }

        mqttRow.BrokerHost = mqtt.Broker.Host;
        mqttRow.BrokerPort = mqtt.Broker.Port;
        mqttRow.ClientId = mqtt.Broker.ClientId;
        mqttRow.UsernameFromEnv = mqtt.Broker.UsernameFromEnv;
        mqttRow.PasswordFromEnv = mqtt.Broker.PasswordFromEnv;
        mqttRow.Tls = mqtt.Broker.Tls;
        mqttRow.Qos = mqtt.Qos;
        mqttRow.Retain = mqtt.Retain;
        mqttRow.TopicTemplate = mqtt.TopicTemplate;
        mqttRow.StatusTopic = mqtt.StatusTopic;
    }

    private static void RememberGroup(GatewayDbContext db, DeviceDocument device)
    {
        var id = device.Spec.GroupId;
        if (string.IsNullOrWhiteSpace(id))
        {
            return;
        }

        var row = db.DeviceGroups.Local.FirstOrDefault(item => item.Id == id)
            ?? db.DeviceGroups.FirstOrDefault(item => item.Id == id);
        if (row is null)
        {
            db.DeviceGroups.Add(new DeviceGroupRow
            {
                Id = id,
                Name = id,
                Workshop = device.Spec.Workshop ?? "",
                Line = device.Spec.Line ?? ""
            });
            return;
        }

        if (!string.IsNullOrWhiteSpace(device.Spec.Workshop))
        {
            row.Workshop = device.Spec.Workshop;
        }

        if (!string.IsNullOrWhiteSpace(device.Spec.Line))
        {
            row.Line = device.Spec.Line;
        }
    }

    private static void RecordAlarm(GatewayDbContext db, Observation observation, string? text, long when)
    {
        if (!IsAlarm(observation, text))
        {
            var open = db.Alarms.Where(row =>
                row.DeviceId == observation.DeviceId && row.PointId == observation.Point && row.Active).ToList();
            foreach (var row in open)
            {
                row.Active = false;
            }

            return;
        }

        var message = text ?? "";
        var current = db.Alarms.FirstOrDefault(row =>
            row.DeviceId == observation.DeviceId && row.PointId == observation.Point && row.Active);
        if (current is not null && string.Equals(current.Message, message, StringComparison.Ordinal))
        {
            return;
        }

        if (current is not null)
        {
            current.Active = false;
        }

        db.Alarms.Add(new AlarmRow
        {
            Id = Guid.NewGuid().ToString("N"),
            DeviceId = observation.DeviceId,
            PointId = observation.Point,
            Message = message,
            Severity = string.Equals(observation.Point, "estop", StringComparison.OrdinalIgnoreCase) ? "estop" : "alarm",
            Active = true,
            RaisedUnixMs = when
        });
    }

    private static bool IsAlarm(Observation observation, string? text)
    {
        var point = observation.Point ?? "";
        if (string.Equals(point, "state", StringComparison.OrdinalIgnoreCase))
        {
            return string.Equals(text, "ALARM", StringComparison.OrdinalIgnoreCase);
        }

        if (string.Equals(point, "estop", StringComparison.OrdinalIgnoreCase))
        {
            return text is "1" or "true" or "True" or "急停";
        }

        if (point.Contains("alarm", StringComparison.OrdinalIgnoreCase)
            || string.Equals(point, "alarmNumber", StringComparison.OrdinalIgnoreCase)
            || point.EndsWith("_warningNumber", StringComparison.Ordinal))
        {
            return !string.IsNullOrWhiteSpace(text)
                && text is not ("0" or "none" or "正常" or "OK" or "false" or "False");
        }

        return false;
    }

    private static AdapterRow ToAdapterRow(CatalogAdapter adapter) => new()
    {
        Id = adapter.Id,
        BrandId = string.IsNullOrWhiteSpace(adapter.BrandId) ? null : adapter.BrandId,
        Kind = adapter.Kind,
        Protocol = adapter.Protocol,
        Phase = adapter.Phase,
        DisplayName = adapter.DisplayName,
        Note = adapter.Note,
        ParametersJson = JsonSerializer.Serialize(adapter.Parameters, Json)
    };

    private static CatalogAdapterView ToAdapterView(AdapterRow row) => new()
    {
        Id = row.Id,
        BrandId = row.BrandId,
        Kind = row.Kind,
        Protocol = row.Protocol,
        Phase = row.Phase,
        DisplayName = row.DisplayName,
        Note = row.Note,
        Parameters = JsonSerializer.Deserialize<List<CatalogParameter>>(row.ParametersJson, Json) ?? []
    };

    private static string? FormatValue(object? value) => value switch
    {
        null => null,
        string text => text,
        bool flag => flag ? "true" : "false",
        IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
        _ => value.ToString()
    };

    private static double? ToNumber(object? value) => value switch
    {
        null => null,
        bool => null,
        string => null,
        byte number => number,
        short number => number,
        int number => number,
        long number => number,
        float number => number,
        double number => number,
        decimal number => (double)number,
        _ => null
    };

    private static bool IsPostgres(string provider) =>
        provider.Equals("Postgres", StringComparison.OrdinalIgnoreCase)
        || provider.Equals("PostgreSQL", StringComparison.OrdinalIgnoreCase);

    private sealed class AccountFileDto
    {
        public string? Mode { get; set; }

        public List<AccountUserDto>? Users { get; set; }
    }

    private sealed class AccountUserDto
    {
        public string? Username { get; set; }

        public string? Role { get; set; }

        public string? PasswordHash { get; set; }

        public bool MustChangePassword { get; set; }
    }
}

public sealed class CatalogOverview
{
    public string Version { get; set; } = "";

    public string Source { get; set; } = "";

    public List<CatalogBrandView> Brands { get; set; } = [];

    public List<CatalogItemView> Items { get; set; } = [];

    public List<CatalogAdapterView> GenericAdapters { get; set; } = [];
}

public sealed class CatalogBrandView
{
    public string Id { get; set; } = "";

    public string NameZh { get; set; } = "";

    public string NameEn { get; set; } = "";

    public List<CatalogModelView> Models { get; set; } = [];

    public List<CatalogAdapterView> Adapters { get; set; } = [];

    public List<CatalogItemView> Items { get; set; } = [];
}

public sealed class CatalogModelView
{
    public string Id { get; set; } = "";

    public string Name { get; set; } = "";
}

public sealed class CatalogItemView
{
    public string Id { get; set; } = "";

    public string NameZh { get; set; } = "";

    public string DataType { get; set; } = "";

    public string Unit { get; set; } = "";

    public string Category { get; set; } = "";

    public bool BrandSpecific { get; set; }

    public List<string> Sources { get; set; } = [];
}

public sealed class CatalogAdapterView
{
    public string Id { get; set; } = "";

    public string? BrandId { get; set; }

    public string Kind { get; set; } = "";

    public string Protocol { get; set; } = "";

    public int Phase { get; set; }

    public string DisplayName { get; set; } = "";

    public string? Note { get; set; }

    public List<CatalogParameter> Parameters { get; set; } = [];
}

public sealed class SampleView
{
    public string DeviceId { get; set; } = "";

    public string PointId { get; set; } = "";

    public string? Value { get; set; }

    public double? NumericValue { get; set; }

    public string Quality { get; set; } = "";

    public string? Unit { get; set; }

    public long TimestampUnixMs { get; set; }
}

public sealed class AlarmView
{
    public string Id { get; set; } = "";

    public string DeviceId { get; set; } = "";

    public string PointId { get; set; } = "";

    public string Message { get; set; } = "";

    public string Severity { get; set; } = "";

    public bool Active { get; set; }

    public long RaisedUnixMs { get; set; }
}
