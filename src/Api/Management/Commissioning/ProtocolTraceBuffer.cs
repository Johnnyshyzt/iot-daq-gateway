using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using IotDaq.Licensing;

namespace Studio.Host.Commissioning;

public sealed class ProtocolTraceBuffer : Adapters.Cnc.Drivers.IProtocolTraceSink
{
    private readonly string _directory;
    private readonly object _gate = new();
    private readonly Dictionary<string, TraceSession> _sessions = new(StringComparer.OrdinalIgnoreCase);

    public ProtocolTraceBuffer(string dataDirectory)
    {
        _directory = Path.Combine(dataDirectory, "traces");
        Directory.CreateDirectory(_directory);
    }

    public string DirectoryPath => _directory;

    public TraceSession Start(string deviceId, int seconds, int maxBytes, IEnumerable<string>? secrets)
    {
        seconds = Math.Clamp(seconds, 5, 900);
        maxBytes = Math.Clamp(maxBytes, 4_096, 8_000_000);
        var now = DateTimeOffset.UtcNow;
        var session = new TraceSession
        {
            DeviceId = deviceId,
            StartedUnixMs = now.ToUnixTimeMilliseconds(),
            StopAtUnixMs = now.AddSeconds(seconds).ToUnixTimeMilliseconds(),
            MaxBytes = maxBytes,
            Secrets = secrets?.Where(item => !string.IsNullOrWhiteSpace(item)).Distinct(StringComparer.Ordinal).ToList() ?? []
        };
        lock (_gate)
        {
            Directory.CreateDirectory(_directory);
            _sessions[deviceId] = session;
            File.WriteAllText(SessionPath(deviceId), JsonSerializer.Serialize(session, Json));
            File.WriteAllText(FilePath(deviceId), "");
        }

        return session;
    }

    public TraceSession? Stop(string deviceId, string reason)
    {
        lock (_gate)
        {
            if (!_sessions.TryGetValue(deviceId, out var session))
            {
                session = ReadSession(deviceId);
            }

            if (session is null)
            {
                return null;
            }

            session.Stopped = true;
            session.Reason = reason;
            _sessions[deviceId] = session;
            File.WriteAllText(SessionPath(deviceId), JsonSerializer.Serialize(session, Json));
            return session;
        }
    }

    public TraceView Read(string deviceId, int limit)
    {
        lock (_gate)
        {
            var session = Current(deviceId);
            var lines = ReadLines(deviceId).TakeLast(Math.Clamp(limit, 1, 500)).ToList();
            return new TraceView
            {
                DeviceId = deviceId,
                Active = session is { Stopped: false } && DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() < session.StopAtUnixMs,
                StartedUnixMs = session?.StartedUnixMs ?? 0,
                StopAtUnixMs = session?.StopAtUnixMs ?? 0,
                MaxBytes = session?.MaxBytes ?? 0,
                Reason = session?.Reason ?? "",
                Bytes = File.Exists(FilePath(deviceId)) ? new FileInfo(FilePath(deviceId)).Length : 0,
                Lines = lines
            };
        }
    }

    public string? FileForDownload(string deviceId)
    {
        var path = FilePath(deviceId);
        return File.Exists(path) ? path : null;
    }

    public void Write(string deviceId, string direction, ReadOnlySpan<byte> raw, string? decoded)
    {
        Note(deviceId, direction, decoded, raw);
    }

    public void Note(string deviceId, string direction, string? decoded, ReadOnlySpan<byte> raw = default)
    {
        lock (_gate)
        {
            var session = Current(deviceId);
            if (session is null || session.Stopped)
            {
                return;
            }

            var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            if (now >= session.StopAtUnixMs)
            {
                session.Stopped = true;
                session.Reason = "到时自动停止";
                File.WriteAllText(SessionPath(deviceId), JsonSerializer.Serialize(session, Json));
                return;
            }

            var text = SecretRedactor.Redact(decoded, session.Secrets);
            var hex = raw.Length == 0 ? "" : SecretRedactor.Hex(raw, session.Secrets);
            var line = JsonSerializer.Serialize(new TraceLine
            {
                T = now,
                Dir = direction,
                Hex = hex,
                Text = text
            }, Json);
            var path = FilePath(deviceId);
            File.AppendAllText(path, line + "\n", Encoding.UTF8);
            var length = new FileInfo(path).Length;
            if (length > session.MaxBytes)
            {
                Trim(path, session.MaxBytes);
                if (new FileInfo(path).Length > session.MaxBytes)
                {
                    session.Stopped = true;
                    session.Reason = "达到大小上限";
                    File.WriteAllText(SessionPath(deviceId), JsonSerializer.Serialize(session, Json));
                }
            }
        }
    }

    private TraceSession? Current(string deviceId)
    {
        if (!_sessions.TryGetValue(deviceId, out var session))
        {
            session = ReadSession(deviceId);
            if (session is not null)
            {
                _sessions[deviceId] = session;
            }
        }

        return session;
    }

    private TraceSession? ReadSession(string deviceId)
    {
        var path = SessionPath(deviceId);
        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<TraceSession>(File.ReadAllText(path), Json);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static void Trim(string path, int maxBytes)
    {
        var lines = File.ReadAllLines(path);
        var kept = new List<string>();
        var size = 0;
        for (var i = lines.Length - 1; i >= 0; i--)
        {
            var bytes = Encoding.UTF8.GetByteCount(lines[i]) + 1;
            if (size + bytes > maxBytes * 3 / 4 && kept.Count > 0)
            {
                break;
            }

            kept.Add(lines[i]);
            size += bytes;
        }

        kept.Reverse();
        File.WriteAllLines(path, kept);
    }

    private List<TraceLine> ReadLines(string deviceId)
    {
        var path = FilePath(deviceId);
        if (!File.Exists(path))
        {
            return [];
        }

        var lines = new List<TraceLine>();
        foreach (var line in File.ReadLines(path))
        {
            if (line.Length == 0)
            {
                continue;
            }

            try
            {
                var parsed = JsonSerializer.Deserialize<TraceLine>(line, Json);
                if (parsed is not null)
                {
                    lines.Add(parsed);
                }
            }
            catch (JsonException)
            {
                continue;
            }
        }

        return lines;
    }

    private string FilePath(string deviceId) => Path.Combine(_directory, Safe(deviceId) + ".jsonl");

    private string SessionPath(string deviceId) => Path.Combine(_directory, Safe(deviceId) + ".session.json");

    private static string Safe(string deviceId)
    {
        var chars = deviceId.Select(ch => char.IsAsciiLetterOrDigit(ch) || ch is '-' or '_' ? ch : '_').ToArray();
        var text = new string(chars);
        return text.Length == 0 ? "device" : text;
    }

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
}

public sealed class TraceSession
{
    public string DeviceId { get; set; } = "";

    public long StartedUnixMs { get; set; }

    public long StopAtUnixMs { get; set; }

    public int MaxBytes { get; set; }

    public bool Stopped { get; set; }

    public string Reason { get; set; } = "";

    [JsonIgnore]
    public List<string> Secrets { get; set; } = [];
}

public sealed class TraceLine
{
    public long T { get; set; }

    public string Dir { get; set; } = "";

    public string Hex { get; set; } = "";

    public string Text { get; set; } = "";
}

public sealed class TraceView
{
    public string DeviceId { get; set; } = "";

    public bool Active { get; set; }

    public long StartedUnixMs { get; set; }

    public long StopAtUnixMs { get; set; }

    public int MaxBytes { get; set; }

    public string Reason { get; set; } = "";

    public long Bytes { get; set; }

    public List<TraceLine> Lines { get; set; } = [];
}
