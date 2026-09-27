using System.Security.Cryptography;
using System.Text.Json;
using IotDaq.Licensing;
using IotDaq.Persistence;

namespace Studio.Host.Licensing;

public sealed class LicenseGuard
{
    private readonly GatewayPersistence _database;
    private readonly string _directory;
    private readonly int _toleranceMs;
    private readonly Func<DateTimeOffset> _clock;
    private readonly object _gate = new();
    private string _audited = "";

    public LicenseGuard(GatewayPersistence database, string dataDirectory, int toleranceSeconds, Func<DateTimeOffset>? clock = null)
    {
        _database = database;
        _directory = Path.Combine(dataDirectory, "security");
        _toleranceMs = Math.Clamp(toleranceSeconds, 0, 86_400) * 1000;
        _clock = clock ?? (() => DateTimeOffset.UtcNow);
    }

    public string SidecarPath => Path.Combine(_directory, "clock.json");

    public string KeyPath => Path.Combine(_directory, "state.key");

    public LicenseGuardResult Observe(string? document, bool allowDocumentChange, bool clearTamper = false)
    {
        lock (_gate)
        {
            Directory.CreateDirectory(_directory);
            var now = _clock().ToUnixTimeMilliseconds();
            var hash = LicenseStateSeal.DocumentHash(document);
            var createdKey = !File.Exists(KeyPath);
            var key = LoadOrCreateKey();
            var database = Read(_database.ReadSecurityPayload(), key, createdKey);
            var sidecar = Read(File.Exists(SidecarPath) ? File.ReadAllText(SidecarPath) : null, key, createdKey);
            var decision = ClockGuard.Observe(now, database.Snapshot, sidecar.Snapshot, _toleranceMs);
            var stored = NewerValid(database, sidecar);
            var code = decision.Code;
            if (code.Length == 0
                && stored is not null
                && stored.DocumentSha256.Length > 0
                && !string.Equals(stored.DocumentSha256, hash, StringComparison.Ordinal)
                && !allowDocumentChange
                && !clearTamper)
            {
                code = "state_tamper";
            }

            if (stored?.Code == "state_tamper" && code.Length == 0 && !clearTamper)
            {
                code = "state_tamper";
            }

            if (clearTamper && decision.Code != "clock_rollback")
            {
                code = "";
            }

            if (decision.Code == "clock_rollback")
            {
                code = "clock_rollback";
            }

            var sealedDocument = LicenseStateSeal.Create(key, decision.LastSeenUnixMs, hash, code);
            var json = LicenseStateSeal.Serialize(sealedDocument);
            _database.WriteSecurityPayload(json, now);
            var temporary = SidecarPath + ".tmp";
            File.WriteAllText(temporary, json);
            File.Move(temporary, SidecarPath, overwrite: true);
            if (!string.Equals(_audited, code, StringComparison.Ordinal))
            {
                _audited = code;
                if (code.Length > 0)
                {
                    _database.AppendAudit("system", "system", "security." + code, "license", Message(code));
                }
            }

            return new LicenseGuardResult
            {
                Code = code,
                Message = Message(code),
                BlocksConfig = code.Length > 0,
                LastSeenUnixMs = decision.LastSeenUnixMs
            };
        }
    }

    public static string Message(string code) => code switch
    {
        "clock_rollback" => "检测到系统时钟回拨。采集仍在运行，配置修改已暂停。请把系统时间校正到不早于上次记录的时刻（允许少量误差）。",
        "state_tamper" => "本机授权状态校验失败，数据库或旁路文件可能被改过。采集仍在运行，配置修改已暂停。管理员确认现场后可在授权页清除该标记。",
        _ => ""
    };

    private static LicenseStateDocument? NewerValid(StoredClock database, StoredClock sidecar)
    {
        LicenseStateDocument? best = null;
        Consider(database);
        Consider(sidecar);
        return best;

        void Consider(StoredClock snapshot)
        {
            if (!snapshot.Snapshot.MacValid || snapshot.Document is null)
            {
                return;
            }

            if (best is null || snapshot.Document.LastSeenUnixMs >= best.LastSeenUnixMs)
            {
                best = snapshot.Document;
            }
        }
    }

    private static StoredClock Read(string? json, byte[] key, bool keyJustCreated)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return new StoredClock();
        }

        LicenseStateDocument? parsed;
        try
        {
            parsed = JsonSerializer.Deserialize<LicenseStateDocument>(json, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        }
        catch (JsonException)
        {
            return new StoredClock { Snapshot = new ClockSnapshot { Present = true, MacValid = false } };
        }

        if (parsed is null)
        {
            return new StoredClock { Snapshot = new ClockSnapshot { Present = true, MacValid = false } };
        }

        var valid = !keyJustCreated && LicenseStateSeal.Verify(key, parsed.LastSeenUnixMs, parsed.DocumentSha256, parsed.Code, parsed.Mac);
        return new StoredClock
        {
            Document = parsed,
            Snapshot = new ClockSnapshot
            {
                Present = true,
                MacValid = valid,
                LastSeenUnixMs = parsed.LastSeenUnixMs
            }
        };
    }

    private byte[] LoadOrCreateKey()
    {
        if (File.Exists(KeyPath))
        {
            var existing = File.ReadAllBytes(KeyPath);
            if (existing.Length >= 32)
            {
                return existing[..32];
            }
        }

        var key = RandomNumberGenerator.GetBytes(32);
        var temporary = KeyPath + ".tmp";
        File.WriteAllBytes(temporary, key);
        File.Move(temporary, KeyPath, overwrite: true);
        return key;
    }

    private sealed class StoredClock
    {
        public ClockSnapshot Snapshot { get; init; } = new();

        public LicenseStateDocument? Document { get; init; }
    }
}

public sealed class LicenseGuardResult
{
    public string Code { get; init; } = "";

    public string Message { get; init; } = "";

    public bool BlocksConfig { get; init; }

    public long LastSeenUnixMs { get; init; }
}
