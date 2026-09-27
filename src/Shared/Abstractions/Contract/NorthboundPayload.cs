using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using Gateway.Abstractions.Topics;

namespace Gateway.Abstractions.Contract;

/// <summary>
/// Versioned northbound JSON. Legacy MQTT point and status documents omit schema and kind
/// so existing subscribers keep the same fields. Contract v1 adds those two fields.
/// Alarm, part-count, utilization, and batch documents are always v1.
/// </summary>
public static class NorthboundPayload
{
    public const string SchemaId = "northbound/1.0";
    public const string Legacy = "legacy";
    public const string V1 = "v1";

    public static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never
    };

    public static bool IsV1(string? contractVersion) =>
        string.Equals(contractVersion?.Trim(), V1, StringComparison.OrdinalIgnoreCase);

    public static string Normalize(string? contractVersion) => IsV1(contractVersion) ? V1 : Legacy;

    public static string Point(
        string gatewayId,
        string site,
        string deviceId,
        string point,
        object? value,
        string quality,
        string? unit,
        DateTimeOffset timestamp,
        bool versioned,
        bool computed = false) =>
        Serialize(new PointDocument
        {
            Schema = versioned ? SchemaId : null,
            Kind = versioned ? "pointValue" : null,
            GatewayId = gatewayId,
            Site = site,
            DeviceId = deviceId,
            Point = point,
            Value = value,
            Quality = quality,
            Unit = unit,
            Ts = timestamp,
            Computed = computed
        });

    public static string Status(
        string gatewayId,
        string site,
        string deviceId,
        string status,
        string? message,
        DateTimeOffset timestamp,
        bool versioned) =>
        Serialize(new StatusDocument
        {
            Schema = versioned ? SchemaId : null,
            Kind = versioned ? "deviceStatus" : null,
            GatewayId = gatewayId,
            Site = site,
            DeviceId = deviceId,
            Status = status,
            Message = message,
            Ts = timestamp
        });

    public static string Alarm(
        string gatewayId,
        string site,
        string deviceId,
        string alarmId,
        string action,
        string code,
        string message,
        string severity,
        string point,
        DateTimeOffset timestamp) =>
        Serialize(new AlarmDocument
        {
            Schema = SchemaId,
            Kind = "alarm",
            GatewayId = gatewayId,
            Site = site,
            DeviceId = deviceId,
            AlarmId = alarmId,
            Action = action,
            Code = code,
            Message = message,
            Severity = severity,
            Point = point,
            Ts = timestamp
        });

    public static string PartCount(
        string gatewayId,
        string site,
        string deviceId,
        double count,
        double? total,
        string quality,
        DateTimeOffset timestamp) =>
        Serialize(new PartDocument
        {
            Schema = SchemaId,
            Kind = "partCount",
            GatewayId = gatewayId,
            Site = site,
            DeviceId = deviceId,
            Count = count,
            Total = total,
            Quality = quality,
            Ts = timestamp
        });

    public static string Utilization(
        string gatewayId,
        string site,
        DateTimeOffset from,
        DateTimeOffset to,
        IReadOnlyList<UtilizationDevice> devices,
        DateTimeOffset timestamp) =>
        Serialize(new UtilizationDocument
        {
            Schema = SchemaId,
            Kind = "utilization",
            GatewayId = gatewayId,
            Site = site,
            From = from,
            To = to,
            Devices = devices,
            Ts = timestamp
        });

    public static string RuleEvent(
        string gatewayId,
        string site,
        string deviceId,
        string ruleId,
        string name,
        string message,
        DateTimeOffset timestamp) =>
        Serialize(new RuleEventDocument
        {
            Schema = SchemaId,
            Kind = "ruleEvent",
            GatewayId = gatewayId,
            Site = site,
            DeviceId = deviceId,
            RuleId = ruleId,
            Name = name,
            Message = message,
            Ts = timestamp
        });

    public static string Batch(
        string gatewayId,
        string site,
        DateTimeOffset sentAt,
        IReadOnlyList<JsonElement> events) =>
        Serialize(new BatchDocument
        {
            Schema = SchemaId,
            Kind = "batch",
            GatewayId = gatewayId,
            Site = site,
            SentAt = sentAt,
            Events = events
        });

    public static JsonElement Parse(string json) =>
        JsonSerializer.Deserialize<JsonElement>(json, Json);

    private static string Serialize<T>(T value) => JsonSerializer.Serialize(value, Json);

    public sealed class PointDocument
    {
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? Schema { get; set; }

        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? Kind { get; set; }

        public required string GatewayId { get; set; }

        public required string Site { get; set; }

        public required string DeviceId { get; set; }

        public required string Point { get; set; }

        public object? Value { get; set; }

        public required string Quality { get; set; }

        public string? Unit { get; set; }

        public DateTimeOffset Ts { get; set; }

        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
        public bool Computed { get; set; }
    }

    public sealed class StatusDocument
    {
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? Schema { get; set; }

        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? Kind { get; set; }

        public required string GatewayId { get; set; }

        public required string Site { get; set; }

        public required string DeviceId { get; set; }

        public required string Status { get; set; }

        public string? Message { get; set; }

        public DateTimeOffset Ts { get; set; }
    }

    public sealed class AlarmDocument
    {
        public required string Schema { get; set; }

        public required string Kind { get; set; }

        public required string GatewayId { get; set; }

        public required string Site { get; set; }

        public required string DeviceId { get; set; }

        public required string AlarmId { get; set; }

        public required string Action { get; set; }

        public required string Code { get; set; }

        public required string Message { get; set; }

        public required string Severity { get; set; }

        public required string Point { get; set; }

        public DateTimeOffset Ts { get; set; }
    }

    public sealed class PartDocument
    {
        public required string Schema { get; set; }

        public required string Kind { get; set; }

        public required string GatewayId { get; set; }

        public required string Site { get; set; }

        public required string DeviceId { get; set; }

        public double Count { get; set; }

        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public double? Total { get; set; }

        public required string Quality { get; set; }

        public DateTimeOffset Ts { get; set; }
    }

    public sealed class UtilizationDevice
    {
        public required string DeviceId { get; set; }

        public string Workshop { get; set; } = "";

        public string Line { get; set; } = "";

        public long RunningMs { get; set; }

        public long IdleMs { get; set; }

        public long AlarmMs { get; set; }

        public long OfflineMs { get; set; }

        public double Utilization { get; set; }

        public double PartCount { get; set; }

        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public double? Availability { get; set; }

        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public double? Performance { get; set; }

        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public double? Quality { get; set; }

        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public double? Oee { get; set; }

        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? OeeFlag { get; set; }
    }

    public sealed class UtilizationDocument
    {
        public required string Schema { get; set; }

        public required string Kind { get; set; }

        public required string GatewayId { get; set; }

        public required string Site { get; set; }

        public DateTimeOffset From { get; set; }

        public DateTimeOffset To { get; set; }

        public required IReadOnlyList<UtilizationDevice> Devices { get; set; }

        public DateTimeOffset Ts { get; set; }
    }

    public sealed class RuleEventDocument
    {
        public required string Schema { get; set; }

        public required string Kind { get; set; }

        public required string GatewayId { get; set; }

        public required string Site { get; set; }

        public required string DeviceId { get; set; }

        public required string RuleId { get; set; }

        public required string Name { get; set; }

        public string Message { get; set; } = "";

        public DateTimeOffset Ts { get; set; }
    }

    public sealed class BatchDocument
    {
        public required string Schema { get; set; }

        public required string Kind { get; set; }

        public required string GatewayId { get; set; }

        public required string Site { get; set; }

        public DateTimeOffset SentAt { get; set; }

        public required IReadOnlyList<JsonElement> Events { get; set; }
    }
}

public static class NorthboundTopics
{
    public static string Alarm(string? template, string site, string deviceId) =>
        DaqTopics.PointTopic(template, site, deviceId, "$alarm");

    public static string Parts(string? template, string site, string deviceId) =>
        DaqTopics.PointTopic(template, site, deviceId, "$parts");

    public static string Event(string? template, string site, string deviceId) =>
        DaqTopics.PointTopic(template, site, deviceId, "$event");

    public static string Utilization(string? template, string site)
    {
        var sample = DaqTopics.PointTopic(template, site, "device", "point");
        var marker = "/" + site + "/";
        var index = sample.IndexOf(marker, StringComparison.Ordinal);
        if (index < 0)
        {
            return "daq/" + site + "/$utilization";
        }

        return string.Concat(sample.AsSpan(0, index), "/", site, "/$utilization");
    }
}

public static class NorthboundTime
{
    public static string? Cursor(long unixMs) =>
        unixMs.ToString(CultureInfo.InvariantCulture);

    public static long ReadCursor(string? text)
    {
        if (long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value))
        {
            return value;
        }

        return 0;
    }
}
