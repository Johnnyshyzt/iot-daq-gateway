using System.Globalization;
using System.Text;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using Cnc.Catalog;
using Gateway.Abstractions.Contracts;
using Gateway.Abstractions.Models;
using IotDaq.Persistence.Visualization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Studio.Contracts;

namespace IotDaq.Persistence;

/// <summary>
/// Opens the edge database, seeds the CNC catalog, stores draft and published
/// configuration, and stores samples. SQLite is the default file under the data directory.
/// PostgreSQL is selected with Database:Provider=Postgres.
/// </summary>
public sealed class GatewayPersistence : ISampleWriter
{
    public const int SchemaVersion = 3;
    public const long MaxBackupBytes = 512L * 1024 * 1024;

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly object _gate = new();
    private readonly DbContextOptions<GatewayDbContext> _options;
    private readonly bool _sqlite;
    private readonly string? _sqlitePath;
    private readonly string? _sqliteConnectionString;
    private readonly int _configuredRetention;
    private event Action? SamplesWritten;

    private GatewayPersistence(
        DbContextOptions<GatewayDbContext> options,
        bool sqlite,
        string provider,
        int historyRetentionDays,
        string? sqlitePath,
        string? sqliteConnectionString)
    {
        _options = options;
        _sqlite = sqlite;
        Provider = provider;
        _configuredRetention = historyRetentionDays;
        _sqlitePath = sqlitePath;
        _sqliteConnectionString = sqliteConnectionString;
    }

    public string Provider { get; }

    public int CurrentSchemaVersion => SchemaVersion;

    public bool SupportsFileBackup => _sqlite && _sqlitePath is not null;

    public int HistoryRetentionDays
    {
        get
        {
            var stored = TryReadSetting("historyRetentionDays");
            if (int.TryParse(stored, NumberStyles.Integer, CultureInfo.InvariantCulture, out var days))
            {
                return Math.Clamp(days, 1, 3650);
            }

            return _configuredRetention;
        }
    }

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
            return new GatewayPersistence(builder.Options, sqlite: false, "Postgres", retention, null, null);
        }

        Directory.CreateDirectory(dataDirectory);
        var path = Path.Combine(dataDirectory, "gateway.db");
        var sqliteConnection = $"Data Source={path};Cache=Shared;Default Timeout=5";
        builder.UseSqlite(sqliteConnection);
        return new GatewayPersistence(builder.Options, sqlite: true, "Sqlite", retention, path, sqliteConnection);
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

            if (_sqlite)
            {
                RepairRevisionIdentity(db);
            }

            SchemaUpgrade.Apply(db, _sqlite);
            var info = db.SchemaInfo.FirstOrDefault(row => row.Id == 1);
            if (info is null)
            {
                db.SchemaInfo.Add(new SchemaInfoRow { Id = 1, Version = SchemaVersion, Provider = Provider });
            }
            else if (info.Version != SchemaVersion)
            {
                info.Version = SchemaVersion;
                info.Provider = Provider;
            }

            if (!db.AppSettings.Any(row => row.Key == "historyRetentionDays"))
            {
                db.AppSettings.Add(new AppSettingRow
                {
                    Key = "historyRetentionDays",
                    Value = _configuredRetention.ToString(CultureInfo.InvariantCulture)
                });
            }

            db.SaveChanges();
            SeedCatalog(db);
        }
    }

    /// <summary>
    /// Earlier unreleased builds used the revision hash as the primary key, so a
    /// later publish of the same content could not record another history row.
    /// Drop that table and recreate it from the current model. History from that
    /// build is not migrated.
    /// </summary>
    private static void RepairRevisionIdentity(GatewayDbContext db)
    {
        var connection = db.Database.GetDbConnection();
        var shouldClose = connection.State != System.Data.ConnectionState.Open;
        if (shouldClose)
        {
            connection.Open();
        }

        var missingIdentity = false;
        try
        {
            using var table = connection.CreateCommand();
            table.CommandText = "SELECT 1 FROM sqlite_master WHERE type = 'table' AND name = 'config_revisions'";
            if (table.ExecuteScalar() is null)
            {
                return;
            }

            using var column = connection.CreateCommand();
            column.CommandText = "SELECT 1 FROM pragma_table_info('config_revisions') WHERE lower(name) = 'id'";
            missingIdentity = column.ExecuteScalar() is null;
            if (!missingIdentity)
            {
                return;
            }

            using var drop = connection.CreateCommand();
            drop.CommandText = "DROP TABLE config_revisions";
            drop.ExecuteNonQuery();
        }
        finally
        {
            if (shouldClose)
            {
                connection.Close();
            }
        }

        if (!missingIdentity)
        {
            return;
        }

        var script = db.Database.GenerateCreateScript();
        var marker = "CREATE TABLE \"config_revisions\"";
        var start = script.IndexOf(marker, StringComparison.Ordinal);
        if (start < 0)
        {
            marker = "CREATE TABLE config_revisions";
            start = script.IndexOf(marker, StringComparison.Ordinal);
        }

        if (start < 0)
        {
            return;
        }

        var next = script.IndexOf("CREATE TABLE", start + marker.Length, StringComparison.Ordinal);
        var piece = (next < 0 ? script[start..] : script[start..next]).Trim();
        db.Database.ExecuteSqlRaw(piece);
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

    public bool TryLoadBundle(string slot, out ConfigBundle bundle, out string hash)
    {
        bundle = new ConfigBundle();
        hash = "";
        EnsureReady();
        lock (_gate)
        {
            using var db = CreateContext();
            var row = db.ConfigBundles.AsNoTracking().FirstOrDefault(item => item.Slot == slot);
            if (row is null || string.IsNullOrWhiteSpace(row.Json))
            {
                return false;
            }

            bundle = JsonSerializer.Deserialize<ConfigBundle>(row.Json, Json) ?? new ConfigBundle();
            hash = row.Hash;
            return true;
        }
    }

    public void AppendRevision(string revision, DateTimeOffset createdAt, string action, string? note, ConfigBundle bundle, string? keepRevision)
    {
        if (string.IsNullOrWhiteSpace(revision))
        {
            return;
        }

        EnsureReady();
        lock (_gate)
        {
            using var db = CreateContext();
            db.ConfigRevisions.Add(new ConfigRevisionRow
            {
                Id = Guid.NewGuid().ToString("N"),
                Revision = revision,
                CreatedUnixMs = createdAt.ToUnixTimeMilliseconds(),
                Action = action,
                Note = note,
                BundleJson = JsonSerializer.Serialize(bundle, Json)
            });
            db.SaveChanges();
            var overflow = db.ConfigRevisions
                .OrderByDescending(row => row.CreatedUnixMs)
                .Skip(30)
                .ToList()
                .Where(row => !string.Equals(row.Revision, keepRevision, StringComparison.Ordinal))
                .ToList();
            if (overflow.Count > 0)
            {
                db.ConfigRevisions.RemoveRange(overflow);
                db.SaveChanges();
            }
        }
    }

    public IReadOnlyList<StoredRevision> ListRevisions(int take)
    {
        EnsureReady();
        lock (_gate)
        {
            using var db = CreateContext();
            return db.ConfigRevisions.AsNoTracking()
                .OrderByDescending(row => row.CreatedUnixMs)
                .Take(Math.Clamp(take, 1, 100))
                .Select(row => new StoredRevision
                {
                    Revision = row.Revision,
                    CreatedUnixMs = row.CreatedUnixMs,
                    Action = row.Action,
                    Note = row.Note
                })
                .ToList();
        }
    }

    public ConfigBundle? TryLoadRevision(string revision)
    {
        EnsureReady();
        lock (_gate)
        {
            using var db = CreateContext();
            var row = db.ConfigRevisions.AsNoTracking()
                .Where(item => item.Revision == revision)
                .OrderBy(item => item.CreatedUnixMs)
                .FirstOrDefault();
            if (row is null || string.IsNullOrWhiteSpace(row.BundleJson))
            {
                return null;
            }

            return JsonSerializer.Deserialize<ConfigBundle>(row.BundleJson, Json);
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
            db.ChangeTracker.AutoDetectChangesEnabled = false;
            var deviceIds = observations.Select(observation => observation.DeviceId).Distinct(StringComparer.Ordinal).ToList();
            var ingest = SampleIngest.Load(db, deviceIds);
            ingest.Apply(observations);
            ingest.Attach(db);
            db.ChangeTracker.DetectChanges();
            db.SaveChanges();
        }

        NotifySamples();
    }

    public void NoteStatus(string deviceId, string status, DateTimeOffset timestamp)
    {
        if (string.IsNullOrWhiteSpace(deviceId))
        {
            return;
        }

        EnsureReady();
        var changed = false;
        lock (_gate)
        {
            using var db = CreateContext();
            var ingest = SampleIngest.Load(db, [deviceId]);
            changed = ingest.ApplyStatus(deviceId, status, timestamp);
            if (changed)
            {
                ingest.Attach(db);
                db.SaveChanges();
            }
        }

        if (changed)
        {
            NotifySamples();
        }
    }

    public IDisposable SubscribeSamples(Action callback)
    {
        SamplesWritten += callback;
        return new Subscription(() => SamplesWritten -= callback);
    }

    public IReadOnlyList<SampleView> Latest(IReadOnlyCollection<string> deviceIds, IReadOnlyCollection<string> pointIds)
    {
        EnsureReady();
        if (deviceIds.Count == 0)
        {
            return [];
        }

        lock (_gate)
        {
            using var db = CreateContext();
            var query = db.SampleLatest.AsNoTracking().Where(row => deviceIds.Contains(row.DeviceId));
            if (pointIds.Count > 0)
            {
                query = query.Where(row => pointIds.Contains(row.PointId));
            }

            return query.Select(row => new SampleView
            {
                DeviceId = row.DeviceId,
                PointId = row.PointId,
                Value = row.ValueText,
                NumericValue = row.NumericValue,
                Quality = row.Quality,
                Unit = row.Unit,
                TimestampUnixMs = row.TimestampUnixMs
            }).ToList();
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

            return ProjectAlarms(query.OrderByDescending(row => row.RaisedUnixMs).Take(limit), DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
        }
    }

    public IReadOnlyList<AlarmView> QueryAlarms(
        string? deviceId,
        bool? active,
        bool? acknowledged,
        string? code,
        long? fromUnixMs,
        long? toUnixMs,
        int limit)
    {
        EnsureReady();
        limit = Math.Clamp(limit, 1, 2000);
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        lock (_gate)
        {
            using var db = CreateContext();
            var query = db.Alarms.AsNoTracking().AsQueryable();
            if (!string.IsNullOrWhiteSpace(deviceId))
            {
                query = query.Where(row => row.DeviceId == deviceId);
            }

            if (active is not null)
            {
                query = query.Where(row => row.Active == active.Value);
            }

            if (acknowledged is not null)
            {
                query = query.Where(row => row.Acknowledged == acknowledged.Value);
            }

            if (!string.IsNullOrWhiteSpace(code))
            {
                query = query.Where(row => row.Code == code);
            }

            if (fromUnixMs is not null)
            {
                query = query.Where(row => row.RaisedUnixMs >= fromUnixMs.Value);
            }

            if (toUnixMs is not null)
            {
                query = query.Where(row => row.RaisedUnixMs <= toUnixMs.Value);
            }

            return ProjectAlarms(query.OrderByDescending(row => row.RaisedUnixMs).Take(limit), now);
        }
    }

    public AlarmView? AcknowledgeAlarm(string id, string user, long whenUnixMs)
    {
        EnsureReady();
        lock (_gate)
        {
            using var db = CreateContext();
            var row = db.Alarms.FirstOrDefault(item => item.Id == id);
            if (row is null)
            {
                return null;
            }

            row.Acknowledged = true;
            row.AcknowledgedBy = user;
            row.AcknowledgedUnixMs = whenUnixMs;
            db.SaveChanges();
            return ProjectAlarms([row], DateTimeOffset.UtcNow.ToUnixTimeMilliseconds())[0];
        }
    }

    public IReadOnlyList<StateSegment> Transitions(IReadOnlyCollection<string> deviceIds, long fromUnixMs, long toUnixMs)
    {
        EnsureReady();
        if (deviceIds.Count == 0)
        {
            return [];
        }

        lock (_gate)
        {
            using var db = CreateContext();
            return db.StateTransitions.AsNoTracking()
                .Where(row => deviceIds.Contains(row.DeviceId)
                    && row.StartedUnixMs <= toUnixMs
                    && (row.EndedUnixMs == null || row.EndedUnixMs >= fromUnixMs))
                .OrderBy(row => row.StartedUnixMs)
                .Select(row => new StateSegment(row.DeviceId, row.State, row.StartedUnixMs, row.EndedUnixMs))
                .ToList();
        }
    }

    public IReadOnlyList<SeriesPoint> AggregateSeries(
        IReadOnlyCollection<string> deviceIds,
        IReadOnlyCollection<string> pointIds,
        long fromUnixMs,
        long toUnixMs,
        long bucketMs)
    {
        EnsureReady();
        if (deviceIds.Count == 0 || pointIds.Count == 0 || toUnixMs < fromUnixMs)
        {
            return [];
        }

        bucketMs = bucketMs <= 0 ? SeriesAggregation.ChooseBucket(fromUnixMs, toUnixMs, 0) : bucketMs;
        lock (_gate)
        {
            using var db = CreateContext();
            var query = db.SampleHistory.AsNoTracking()
                .Where(row => deviceIds.Contains(row.DeviceId)
                    && pointIds.Contains(row.PointId)
                    && row.TimestampUnixMs >= fromUnixMs
                    && row.TimestampUnixMs <= toUnixMs
                    && row.NumericValue != null);
            var bucket = bucketMs;
            return query
                .GroupBy(row => new
                {
                    row.DeviceId,
                    row.PointId,
                    Bucket = row.TimestampUnixMs / bucket
                })
                .Select(group => new SeriesPoint
                {
                    DeviceId = group.Key.DeviceId,
                    PointId = group.Key.PointId,
                    TimestampUnixMs = group.Key.Bucket * bucket,
                    Avg = group.Average(row => row.NumericValue) ?? 0,
                    Min = group.Min(row => row.NumericValue) ?? 0,
                    Max = group.Max(row => row.NumericValue) ?? 0,
                    Count = group.Count()
                })
                .OrderBy(point => point.TimestampUnixMs)
                .ToList();
        }
    }

    public IReadOnlyList<PartSample> PartSamples(IReadOnlyCollection<string> deviceIds, long fromUnixMs, long toUnixMs, long bucketMs)
    {
        EnsureReady();
        if (deviceIds.Count == 0 || toUnixMs < fromUnixMs)
        {
            return [];
        }

        bucketMs = Math.Max(1000, bucketMs);
        lock (_gate)
        {
            using var db = CreateContext();
            var bucket = bucketMs;
            return db.SampleHistory.AsNoTracking()
                .Where(row => deviceIds.Contains(row.DeviceId)
                    && row.PointId == "partCount"
                    && row.NumericValue != null
                    && row.TimestampUnixMs >= fromUnixMs
                    && row.TimestampUnixMs <= toUnixMs)
                .GroupBy(row => new { row.DeviceId, Bucket = row.TimestampUnixMs / bucket })
                .Select(group => new PartSample(
                    group.Key.DeviceId,
                    group.Max(row => row.TimestampUnixMs),
                    group.OrderByDescending(row => row.TimestampUnixMs).Select(row => row.NumericValue).FirstOrDefault() ?? 0))
                .ToList();
        }
    }

    public string? GetSetting(string key) => TryReadSetting(key);

    public void SetSetting(string key, string value)
    {
        EnsureReady();
        lock (_gate)
        {
            using var db = CreateContext();
            var row = db.AppSettings.FirstOrDefault(item => item.Key == key);
            if (row is null)
            {
                db.AppSettings.Add(new AppSettingRow { Key = key, Value = value });
            }
            else
            {
                row.Value = value;
            }

            db.SaveChanges();
        }
    }

    public void AppendAudit(string username, string role, string action, string target, string detail)
    {
        EnsureReady();
        var text = detail.Replace('\r', ' ').Replace('\n', ' ').Trim();
        if (text.Length > 400)
        {
            text = text[..400];
        }

        lock (_gate)
        {
            using var db = CreateContext();
            db.AuditEvents.Add(new AuditEventRow
            {
                UnixMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                Username = TrimAudit(username, 80),
                Role = TrimAudit(role, 32),
                Action = TrimAudit(action, 64),
                Target = TrimAudit(target, 120),
                Detail = text
            });
            db.SaveChanges();
            var stale = db.AuditEvents.OrderByDescending(row => row.Id).Skip(500).Select(row => row.Id).FirstOrDefault();
            if (stale > 0)
            {
                db.AuditEvents.Where(row => row.Id <= stale).ExecuteDelete();
            }
        }
    }

    public IReadOnlyList<AuditEventRow> ListAudit(int limit)
    {
        EnsureReady();
        limit = Math.Clamp(limit, 1, 200);
        lock (_gate)
        {
            using var db = CreateContext();
            return db.AuditEvents.AsNoTracking()
                .OrderByDescending(row => row.Id)
                .Take(limit)
                .ToList();
        }
    }

    public bool HasSamples()
    {
        EnsureReady();
        lock (_gate)
        {
            using var db = CreateContext();
            return db.SampleLatest.Any() || db.SampleHistory.Any();
        }
    }

    public void WriteSqliteBackup(Stream destination)
    {
        var temp = Path.Combine(Path.GetTempPath(), "iot-daq-backup-" + Guid.NewGuid().ToString("N") + ".db");
        try
        {
            WriteSqliteBackupFile(temp);
            using var file = File.OpenRead(temp);
            file.CopyTo(destination);
        }
        finally
        {
            DeleteIfExists(temp);
        }
    }

    public void WriteSqliteBackupFile(string destinationPath)
    {
        if (!_sqlite || string.IsNullOrWhiteSpace(_sqliteConnectionString))
        {
            throw new InvalidOperationException("只有 SQLite 支持在页面备份。PostgreSQL 请在数据库服务器上备份。");
        }

        var directory = Path.GetDirectoryName(Path.GetFullPath(destinationPath));
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        DeleteIfExists(destinationPath);
        lock (_gate)
        {
            using var source = new SqliteConnection(_sqliteConnectionString);
            source.Open();
            using var dest = new SqliteConnection($"Data Source={destinationPath}");
            dest.Open();
            source.BackupDatabase(dest);
        }
    }

    public void RestoreSqlite(Stream upload)
    {
        if (!_sqlite || string.IsNullOrWhiteSpace(_sqlitePath) || string.IsNullOrWhiteSpace(_sqliteConnectionString))
        {
            throw new InvalidOperationException("当前数据库不是 SQLite，不能在页面恢复。");
        }

        var temp = Path.Combine(Path.GetTempPath(), "iot-daq-restore-" + Guid.NewGuid().ToString("N") + ".db");
        try
        {
            using (var file = File.Create(temp))
            {
                var buffer = new byte[81920];
                long total = 0;
                int read;
                while ((read = upload.Read(buffer, 0, buffer.Length)) > 0)
                {
                    total += read;
                    if (total > MaxBackupBytes)
                    {
                        throw new InvalidOperationException("备份文件超过 512 MB，已拒绝。");
                    }

                    file.Write(buffer, 0, read);
                }
            }

            ValidateSqliteBackup(temp);
            lock (_gate)
            {
                SqliteConnection.ClearAllPools();
                var backup = _sqlitePath + ".bak";
                File.Copy(_sqlitePath, backup, overwrite: true);
                try
                {
                    DeleteIfExists(_sqlitePath + "-wal");
                    DeleteIfExists(_sqlitePath + "-shm");
                    File.Copy(temp, _sqlitePath, overwrite: true);
                    EnsureReady();
                }
                catch
                {
                    File.Copy(backup, _sqlitePath, overwrite: true);
                    DeleteIfExists(_sqlitePath + "-wal");
                    DeleteIfExists(_sqlitePath + "-shm");
                    EnsureReady();
                    throw;
                }
            }
        }
        finally
        {
            DeleteIfExists(temp);
        }
    }

    public bool HasHistory()
    {
        EnsureReady();
        lock (_gate)
        {
            using var db = CreateContext();
            return db.SampleHistory.Any() || db.StateTransitions.Any();
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
            var states = db.StateTransitions.Where(row => row.EndedUnixMs != null && row.EndedUnixMs < unix).ExecuteDelete();
            return samples + alarms + states;
        }
    }

    private static List<AlarmView> ProjectAlarms(IEnumerable<AlarmRow> rows, long nowUnixMs) =>
        rows.Select(row => new AlarmView
        {
            Id = row.Id,
            DeviceId = row.DeviceId,
            PointId = row.PointId,
            Code = row.Code,
            Message = row.Message,
            Severity = row.Severity,
            Active = row.Active,
            RaisedUnixMs = row.RaisedUnixMs,
            ClearedUnixMs = row.ClearedUnixMs,
            DurationMs = AlarmLogic.Duration(row.RaisedUnixMs, row.ClearedUnixMs, nowUnixMs, row.Active),
            Acknowledged = row.Acknowledged,
            AcknowledgedBy = row.AcknowledgedBy,
            AcknowledgedUnixMs = row.AcknowledgedUnixMs
        }).ToList();

    private static void ValidateSqliteBackup(string path)
    {
        var header = new byte[16];
        using (var stream = File.OpenRead(path))
        {
            if (stream.Read(header, 0, header.Length) < 16
                || !Encoding.ASCII.GetString(header).StartsWith("SQLite format 3", StringComparison.Ordinal))
            {
                throw new InvalidOperationException("文件不是 SQLite 数据库。");
            }
        }

        try
        {
            using var connection = new SqliteConnection($"Data Source={path};Mode=ReadOnly");
            connection.Open();
            using var versionCommand = connection.CreateCommand();
            versionCommand.CommandText = "SELECT Version FROM schema_info WHERE Id = 1";
            var version = versionCommand.ExecuteScalar();
            if (version is null or DBNull)
            {
                throw new InvalidOperationException("备份里没有 schema_info，不是本网关的数据库。");
            }

            var number = Convert.ToInt32(version, CultureInfo.InvariantCulture);
            if (number < 1 || number > SchemaVersion)
            {
                throw new InvalidOperationException($"备份的数据库版本是 {number.ToString(CultureInfo.InvariantCulture)}，当前 Host 只接受 1 到 {SchemaVersion.ToString(CultureInfo.InvariantCulture)}。");
            }

            using var slot = connection.CreateCommand();
            slot.CommandText = "SELECT 1 FROM config_bundles WHERE Slot = 'published'";
            if (slot.ExecuteScalar() is null)
            {
                throw new InvalidOperationException("备份里没有已发布配置。");
            }
        }
        catch (InvalidOperationException)
        {
            throw;
        }
        catch (SqliteException ex)
        {
            throw new InvalidOperationException("无法读取这份 SQLite 备份。", ex);
        }
    }

    private static string TrimAudit(string value, int max)
    {
        var text = (value ?? "").Replace('\r', ' ').Replace('\n', ' ').Trim();
        return text.Length <= max ? text : text[..max];
    }

    private static void DeleteIfExists(string path)
    {
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }

    private string? TryReadSetting(string key)
    {
        EnsureReady();
        lock (_gate)
        {
            using var db = CreateContext();
            return db.AppSettings.AsNoTracking().Where(row => row.Key == key).Select(row => row.Value).FirstOrDefault();
        }
    }

    private void NotifySamples()
    {
        var handler = SamplesWritten;
        handler?.Invoke();
    }

    private sealed class Subscription(Action dispose) : IDisposable
    {
        public void Dispose() => dispose();
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

public sealed class StoredRevision
{
    public string Revision { get; set; } = "";

    public long CreatedUnixMs { get; set; }

    public string Action { get; set; } = "";

    public string? Note { get; set; }
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

    public string Code { get; set; } = "";

    public string Message { get; set; } = "";

    public string Severity { get; set; } = "";

    public bool Active { get; set; }

    public long RaisedUnixMs { get; set; }

    public long? ClearedUnixMs { get; set; }

    public long? DurationMs { get; set; }

    public bool Acknowledged { get; set; }

    public string? AcknowledgedBy { get; set; }

    public long? AcknowledgedUnixMs { get; set; }
}
