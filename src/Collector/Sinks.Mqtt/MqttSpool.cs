using System.Globalization;
using System.Text.Json;

namespace Sinks.Mqtt;

public sealed class MqttSpoolItem
{
    public long Seq { get; set; }

    public long UnixMs { get; set; }

    public string Topic { get; set; } = "";

    public int Qos { get; set; }

    public bool Retain { get; set; }

    public string Payload { get; set; } = "";
}

/// <summary>
/// Bounded on-disk queue. Files are named by sequence so replay is oldest-first.
/// When count, bytes, or age is exceeded, the oldest messages are deleted and <see cref="Dropped"/> increases.
/// </summary>
public sealed class MqttSpool
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private readonly object _gate = new();
    private readonly ReliabilityLimits _limits;
    private readonly string _directory;
    private readonly List<Stored> _order = [];
    private long _bytes;
    private long _dropped;
    private long _nextSeq = 1;

    public MqttSpool(string directory, ReliabilityLimits limits)
    {
        _directory = directory;
        _limits = limits;
        Directory.CreateDirectory(directory);
        Load();
    }

    public int Depth
    {
        get
        {
            lock (_gate)
            {
                return _order.Count;
            }
        }
    }

    public long Dropped
    {
        get
        {
            lock (_gate)
            {
                return _dropped;
            }
        }
    }

    public void Enqueue(string topic, string payload, int qos, bool retain, DateTimeOffset now)
    {
        lock (_gate)
        {
            DropExpired(now);
            var item = new MqttSpoolItem
            {
                Seq = _nextSeq++,
                UnixMs = now.ToUnixTimeMilliseconds(),
                Topic = topic,
                Qos = qos,
                Retain = retain,
                Payload = payload
            };
            var json = JsonSerializer.Serialize(item, Json);
            var path = PathOf(item.Seq);
            File.WriteAllText(path, json);
            var length = new FileInfo(path).Length;
            _order.Add(new Stored(item.Seq, item.UnixMs, length, path));
            _bytes += length;
            Trim();
            SaveMeta();
        }
    }

    public MqttSpoolItem? PeekOldest(DateTimeOffset now)
    {
        lock (_gate)
        {
            DropExpired(now);
            while (_order.Count > 0)
            {
                var stored = _order[0];
                if (!File.Exists(stored.Path))
                {
                    RemoveAt(0, countDropped: true);
                    continue;
                }

                try
                {
                    var item = JsonSerializer.Deserialize<MqttSpoolItem>(File.ReadAllText(stored.Path), Json);
                    if (item is null || item.Seq != stored.Seq)
                    {
                        RemoveAt(0, countDropped: true);
                        continue;
                    }

                    return item;
                }
                catch (JsonException)
                {
                    RemoveAt(0, countDropped: true);
                }
                catch (IOException)
                {
                    return null;
                }
            }

            return null;
        }
    }

    public void Acknowledge(long seq)
    {
        lock (_gate)
        {
            var index = _order.FindIndex(item => item.Seq == seq);
            if (index < 0)
            {
                return;
            }

            RemoveAt(index, countDropped: false);
            SaveMeta();
        }
    }

    private void Load()
    {
        var metaPath = Path.Combine(_directory, "meta.json");
        if (File.Exists(metaPath))
        {
            try
            {
                var meta = JsonSerializer.Deserialize<Meta>(File.ReadAllText(metaPath), Json);
                if (meta is not null)
                {
                    _dropped = Math.Max(0, meta.Dropped);
                    _nextSeq = Math.Max(1, meta.Next);
                }
            }
            catch (JsonException)
            {
                // A damaged meta file is rebuilt from the message files.
            }
        }

        foreach (var path in Directory.EnumerateFiles(_directory, "*.msg.json"))
        {
            var name = Path.GetFileName(path);
            var dot = name.IndexOf('.');
            if (dot <= 0 || !long.TryParse(name[..dot], NumberStyles.None, CultureInfo.InvariantCulture, out var seq))
            {
                TryDelete(path);
                _dropped++;
                continue;
            }

            long unix = 0;
            try
            {
                var item = JsonSerializer.Deserialize<MqttSpoolItem>(File.ReadAllText(path), Json);
                unix = item?.UnixMs ?? 0;
                if (item is null)
                {
                    TryDelete(path);
                    _dropped++;
                    continue;
                }
            }
            catch (JsonException)
            {
                TryDelete(path);
                _dropped++;
                continue;
            }
            catch (IOException)
            {
                continue;
            }

            var length = new FileInfo(path).Length;
            _order.Add(new Stored(seq, unix, length, path));
            _bytes += length;
            if (seq >= _nextSeq)
            {
                _nextSeq = seq + 1;
            }
        }

        _order.Sort((left, right) => left.Seq.CompareTo(right.Seq));
        DropExpired(DateTimeOffset.UtcNow);
        Trim();
        SaveMeta();
    }

    private void DropExpired(DateTimeOffset now)
    {
        var maxAge = _limits.MaxAge();
        var cutoff = now.ToUnixTimeMilliseconds() - (long)maxAge.TotalMilliseconds;
        while (_order.Count > 0 && _order[0].UnixMs < cutoff)
        {
            RemoveAt(0, countDropped: true);
        }
    }

    private void Trim()
    {
        var maxMessages = _limits.MaxMessages();
        var maxBytes = _limits.MaxBytes();
        while (_order.Count > maxMessages || (_order.Count > 0 && _bytes > maxBytes))
        {
            RemoveAt(0, countDropped: true);
        }
    }

    private void RemoveAt(int index, bool countDropped)
    {
        var stored = _order[index];
        _order.RemoveAt(index);
        _bytes = Math.Max(0, _bytes - stored.Bytes);
        TryDelete(stored.Path);
        if (countDropped)
        {
            _dropped++;
        }
    }

    private void SaveMeta()
    {
        var meta = JsonSerializer.Serialize(new Meta { Next = _nextSeq, Dropped = _dropped }, Json);
        var path = Path.Combine(_directory, "meta.json");
        var temp = path + ".tmp";
        File.WriteAllText(temp, meta);
        File.Move(temp, path, overwrite: true);
    }

    private string PathOf(long seq) =>
        Path.Combine(_directory, seq.ToString("D16", CultureInfo.InvariantCulture) + ".msg.json");

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (IOException)
        {
            // The next load will try again.
        }
    }

    private readonly record struct Stored(long Seq, long UnixMs, long Bytes, string Path);

    private sealed class Meta
    {
        public long Next { get; set; }

        public long Dropped { get; set; }
    }
}

public sealed class ReliabilityLimits
{
    public Func<int> MaxMessages { get; init; } = () => 10_000;

    public Func<TimeSpan> MaxAge { get; init; } = () => TimeSpan.FromHours(24);

    public Func<long> MaxBytes { get; init; } = () => 64L * 1024 * 1024;
}
