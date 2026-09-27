using Gateway.Abstractions.Contract;
using Gateway.Abstractions.Contracts;
using IotDaq.Persistence;
using IotDaq.Persistence.Visualization;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Studio.Host.Visualization;

namespace Studio.Host.Northbound;

/// <summary>
/// Publishes alarm, part-count, and utilization documents on MQTT.
/// Point values and device status stay on their existing topics.
/// </summary>
public sealed class ContractMqttWorker(
    IEnumerable<IContractPublisher> publishers,
    GatewayPersistence database,
    VisualizationService visualization,
    ILogger<ContractMqttWorker> logger) : BackgroundService
{
    private readonly Dictionary<string, (double Count, double? Total, string Quality, long Unix)> _parts = new(StringComparer.Ordinal);
    private long _lastUtilization;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await PublishAsync(stoppingToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning(ex, "Contract MQTT publish failed");
            }

            try
            {
                await Task.Delay(TimeSpan.FromSeconds(1), stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    private async Task PublishAsync(CancellationToken cancellationToken)
    {
        EnsureCursors();
        var publisher = publishers.FirstOrDefault();
        if (publisher is null || !publisher.TryLease(out var lease) || lease is null)
        {
            return;
        }

        await PublishAlarmsAsync(lease, cancellationToken).ConfigureAwait(false);
        await PublishPartsAsync(lease, cancellationToken).ConfigureAwait(false);
        await PublishUtilizationAsync(lease, cancellationToken).ConfigureAwait(false);
        await PublishRuleEventsAsync(lease, cancellationToken).ConfigureAwait(false);
    }

    private async Task PublishAlarmsAsync(ContractLease lease, CancellationToken cancellationToken)
    {
        var raiseKey = "contract.mqtt.raise";
        var clearKey = "contract.mqtt.clear";
        var raiseCursor = NorthboundTime.ReadCursor(database.GetSetting(raiseKey));
        foreach (var alarm in database.AlarmsRaisedAfter(raiseCursor, 100))
        {
            var json = NorthboundPayload.Alarm(
                lease.GatewayId,
                lease.Site,
                alarm.DeviceId,
                alarm.Id,
                "raise",
                alarm.Code,
                alarm.Message,
                alarm.Severity,
                alarm.PointId,
                DateTimeOffset.FromUnixTimeMilliseconds(alarm.RaisedUnixMs));
            await lease.PublishAsync(NorthboundTopics.Alarm(lease.TopicTemplate, lease.Site, alarm.DeviceId), json, cancellationToken).ConfigureAwait(false);
            raiseCursor = alarm.RaisedUnixMs;
            database.SetSetting(raiseKey, NorthboundTime.Cursor(raiseCursor)!);
        }

        var clearCursor = NorthboundTime.ReadCursor(database.GetSetting(clearKey));
        foreach (var alarm in database.AlarmsClearedAfter(clearCursor, 100))
        {
            var when = alarm.ClearedUnixMs ?? alarm.RaisedUnixMs;
            var json = NorthboundPayload.Alarm(
                lease.GatewayId,
                lease.Site,
                alarm.DeviceId,
                alarm.Id,
                "clear",
                alarm.Code,
                alarm.Message,
                alarm.Severity,
                alarm.PointId,
                DateTimeOffset.FromUnixTimeMilliseconds(when));
            await lease.PublishAsync(NorthboundTopics.Alarm(lease.TopicTemplate, lease.Site, alarm.DeviceId), json, cancellationToken).ConfigureAwait(false);
            clearCursor = when;
            database.SetSetting(clearKey, NorthboundTime.Cursor(clearCursor)!);
        }
    }

    private async Task PublishPartsAsync(ContractLease lease, CancellationToken cancellationToken)
    {
        var latest = database.LatestAll(20_000)
            .Where(row => row.PointId.Equals("partCount", StringComparison.OrdinalIgnoreCase)
                || row.PointId.Equals("partCountTotal", StringComparison.OrdinalIgnoreCase))
            .ToList();
        foreach (var deviceId in latest.Select(row => row.DeviceId).Distinct(StringComparer.Ordinal))
        {
            var countRow = latest.FirstOrDefault(row => row.DeviceId == deviceId && row.PointId.Equals("partCount", StringComparison.OrdinalIgnoreCase));
            var totalRow = latest.FirstOrDefault(row => row.DeviceId == deviceId && row.PointId.Equals("partCountTotal", StringComparison.OrdinalIgnoreCase));
            if (countRow is null && totalRow is null)
            {
                continue;
            }

            var count = countRow?.NumericValue ?? 0;
            double? total = totalRow?.NumericValue;
            var quality = countRow?.Quality ?? totalRow?.Quality ?? "good";
            var unix = Math.Max(countRow?.TimestampUnixMs ?? 0, totalRow?.TimestampUnixMs ?? 0);
            if (_parts.TryGetValue(deviceId, out var previous)
                && previous.Count == count
                && previous.Total == total
                && previous.Unix == unix)
            {
                continue;
            }

            _parts[deviceId] = (count, total, quality, unix);
            var json = NorthboundPayload.PartCount(
                lease.GatewayId,
                lease.Site,
                deviceId,
                count,
                total,
                string.IsNullOrWhiteSpace(quality) ? "good" : quality,
                DateTimeOffset.FromUnixTimeMilliseconds(unix == 0 ? DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() : unix));
            await lease.PublishAsync(NorthboundTopics.Parts(lease.TopicTemplate, lease.Site, deviceId), json, cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task PublishUtilizationAsync(ContractLease lease, CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        if (now - _lastUtilization < 60_000)
        {
            return;
        }

        _lastUtilization = now;
        var from = now - 24L * 60 * 60 * 1000;
        var report = visualization.Utilization(from, now, null);
        var devices = report.Shifts
            .GroupBy(row => row.DeviceId, StringComparer.Ordinal)
            .Select(group =>
            {
                var first = group.First();
                var run = group.Sum(row => row.RunMs);
                var planned = group.Sum(row => row.PlannedMs);
                return new NorthboundPayload.UtilizationDevice
                {
                    DeviceId = first.DeviceId,
                    Workshop = first.Workshop,
                    Line = first.Line,
                    RunningMs = run,
                    IdleMs = group.Sum(row => row.IdleMs),
                    AlarmMs = group.Sum(row => row.AlarmMs),
                    OfflineMs = group.Sum(row => row.OfflineMs),
                    Utilization = UtilizationMath.Ratio(run, planned),
                    PartCount = group.Sum(row => row.PartCount)
                };
            })
            .ToList();
        var json = NorthboundPayload.Utilization(
            lease.GatewayId,
            lease.Site,
            DateTimeOffset.FromUnixTimeMilliseconds(from),
            DateTimeOffset.FromUnixTimeMilliseconds(now),
            devices,
            DateTimeOffset.FromUnixTimeMilliseconds(now));
        await lease.PublishAsync(NorthboundTopics.Utilization(lease.TopicTemplate, lease.Site), json, cancellationToken).ConfigureAwait(false);
    }

    private async Task PublishRuleEventsAsync(ContractLease lease, CancellationToken cancellationToken)
    {
        var pending = database.PendingRuleEvents(50);
        if (pending.Count == 0)
        {
            return;
        }

        var published = new List<long>();
        foreach (var row in pending)
        {
            var json = NorthboundPayload.RuleEvent(
                lease.GatewayId,
                lease.Site,
                row.DeviceId,
                row.RuleId,
                row.Name,
                row.Message,
                DateTimeOffset.FromUnixTimeMilliseconds(row.UnixMs));
            await lease.PublishAsync(NorthboundTopics.Event(lease.TopicTemplate, lease.Site, row.DeviceId), json, cancellationToken).ConfigureAwait(false);
            published.Add(row.Id);
        }

        database.MarkRuleEventsPublished(published);
    }

    private void EnsureCursors()
    {
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString(System.Globalization.CultureInfo.InvariantCulture);
        foreach (var key in new[] { "contract.mqtt.raise", "contract.mqtt.clear" })
        {
            if (database.GetSetting(key) is null)
            {
                database.SetSetting(key, now);
            }
        }
    }
}
