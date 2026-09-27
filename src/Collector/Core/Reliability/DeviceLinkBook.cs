using Gateway.Abstractions.Reliability;

namespace Gateway.Host.Reliability;

public enum DeviceLinkPhase
{
    Connected,
    Connecting,
    Backoff
}

public sealed class DeviceLink
{
    public required string DeviceId { get; init; }

    public DeviceLinkPhase Phase { get; set; } = DeviceLinkPhase.Connecting;

    public int Attempt { get; set; }

    public DateTimeOffset? NextRetryUtc { get; set; }

    public string? LastError { get; set; }

    public DateTimeOffset? CollectStartedUtc { get; set; }

    public DateTimeOffset? LastProgressUtc { get; set; }
}

/// <summary>
/// Per-device connection phase. A device in backoff is not collected until <see cref="DeviceLink.NextRetryUtc"/>.
/// </summary>
public sealed class DeviceLinkBook
{
    private readonly object _gate = new();
    private readonly Dictionary<string, DeviceLink> _links = new(StringComparer.OrdinalIgnoreCase);

    public bool ShouldAttempt(string deviceId, DateTimeOffset now)
    {
        lock (_gate)
        {
            if (!_links.TryGetValue(deviceId, out var link))
            {
                return true;
            }

            return link.Phase != DeviceLinkPhase.Backoff
                || link.NextRetryUtc is null
                || now >= link.NextRetryUtc.Value;
        }
    }

    public void BeginAttempt(string deviceId, DateTimeOffset now)
    {
        lock (_gate)
        {
            var link = GetOrAdd(deviceId);
            link.Phase = DeviceLinkPhase.Connecting;
            link.CollectStartedUtc = now;
        }
    }

    public void Connected(string deviceId, DateTimeOffset now)
    {
        lock (_gate)
        {
            var link = GetOrAdd(deviceId);
            link.Phase = DeviceLinkPhase.Connected;
            link.Attempt = 0;
            link.NextRetryUtc = null;
            link.LastError = null;
            link.CollectStartedUtc = null;
            link.LastProgressUtc = now;
        }
    }

    public void Failed(string deviceId, string error, DateTimeOffset now, ReliabilityOptions options, double jitterUnit)
    {
        lock (_gate)
        {
            var link = GetOrAdd(deviceId);
            link.Attempt = Math.Min(link.Attempt + 1, 10_000);
            link.Phase = DeviceLinkPhase.Backoff;
            link.LastError = Trim(error);
            link.CollectStartedUtc = null;
            link.NextRetryUtc = now + ReconnectBackoff.Delay(
                link.Attempt,
                options.Initial,
                options.ReconnectMultiplier,
                options.Cap,
                options.ReconnectJitter,
                jitterUnit);
        }
    }

    public IReadOnlyList<string> Stalled(DateTimeOffset now, TimeSpan timeout)
    {
        lock (_gate)
        {
            var stalled = new List<string>();
            foreach (var link in _links.Values)
            {
                if (link.Phase == DeviceLinkPhase.Backoff || link.CollectStartedUtc is null)
                {
                    continue;
                }

                if (link.LastProgressUtc is not null && link.LastProgressUtc >= link.CollectStartedUtc)
                {
                    continue;
                }

                if (now - link.CollectStartedUtc.Value >= timeout)
                {
                    stalled.Add(link.DeviceId);
                }
            }

            return stalled;
        }
    }

    public DeviceLink? Find(string deviceId)
    {
        lock (_gate)
        {
            return _links.TryGetValue(deviceId, out var link) ? Copy(link) : null;
        }
    }

    public void Reset()
    {
        lock (_gate)
        {
            _links.Clear();
        }
    }

    private DeviceLink GetOrAdd(string deviceId)
    {
        if (!_links.TryGetValue(deviceId, out var link))
        {
            link = new DeviceLink { DeviceId = deviceId };
            _links[deviceId] = link;
        }

        return link;
    }

    private static DeviceLink Copy(DeviceLink link) => new()
    {
        DeviceId = link.DeviceId,
        Phase = link.Phase,
        Attempt = link.Attempt,
        NextRetryUtc = link.NextRetryUtc,
        LastError = link.LastError,
        CollectStartedUtc = link.CollectStartedUtc,
        LastProgressUtc = link.LastProgressUtc
    };

    private static string Trim(string error)
    {
        var text = (error ?? "").Replace('\r', ' ').Replace('\n', ' ').Trim();
        return text.Length <= 300 ? text : text[..300];
    }
}
