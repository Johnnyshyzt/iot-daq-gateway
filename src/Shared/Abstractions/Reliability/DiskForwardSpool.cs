using System.Globalization;
using System.Text.Json;

namespace Gateway.Abstractions.Reliability;

public sealed class DiskForwardItem
{
    public long Seq { get; set; }

    public long UnixMs { get; set; }

    public string Payload { get; set; } = "";
}

/// <summary>
/// Bounded on-disk queue used by HTTP push. Same rules as the MQTT spool:
/// oldest-first replay, and the oldest messages are dropped when count, bytes, or age is exceeded.
/// </summary>
public sealed class DiskForwardSpool
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private readonly object _gate = new();
    private readonly int _maxMessages;
    private readonly long _maxBytes;
    private readonly TimeSpan _maxAge;
    private readonly string _directory;
    private readonly List<Stored> _order = [];
    private long _bytes;
    private long _dropped;
    private long _nextSeq = 1;

    public DiskForwardSpool(string directory, int maxMessages = 10_000, long maxBytes = 64L * 1024 * 1024, TimeSpan? maxAge = null)
    {
        _directory = directory;
        _maxMessages = Math.Max(1, maxMessages);
        _maxBytes = Math.Max(1, maxBytes);
        _maxAge = maxAge ?? TimeSpan.FromHours(24);
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

    public void Enqueue(string payload, DateTimeOffset now)
    {
        lock (_gate)
        {
            DropExpired(now);
            var item = new DiskForwardItem
            {
                Seq = _nextSeq++,
                UnixMs = now.ToUnixTimeMilliseconds(),
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

    public DiskForwardItem? PeekOldest(DateTimeOffset now)
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
                    var item = JsonSerializer.Deserialize<DiskForwardItem>(File.ReadAllText(stored.Path), Json);
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
                var item = JsonSerializer.Deserialize<DiskForwardItem>(File.ReadAllText(path), Json);
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
        var cutoff = now.ToUnixTimeMilliseconds() - (long)_maxAge.TotalMilliseconds;
        while (_order.Count > 0 && _order[0].UnixMs < cutoff)
        {
            RemoveAt(0, countDropped: true);
        }
    }

    private void Trim()
    {
        while (_order.Count > _maxMessages || (_order.Count > 0 && _bytes > _maxBytes))
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
