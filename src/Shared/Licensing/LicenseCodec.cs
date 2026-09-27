using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace IotDaq.Licensing;

public static class LicenseFeatures
{
    public const string OpcUa = "opcua";
    public const string HttpPush = "http-push";
    public const string AlarmNotifications = "alarm-notifications";
    public const string ScheduledReports = "scheduled-reports";
    public const string QueryApi = "query-api";
    public const string All = "*";

    public static readonly string[] DefaultGated =
    [
        OpcUa,
        HttpPush,
        AlarmNotifications,
        ScheduledReports,
        QueryApi
    ];

    public static string Title(string feature) => feature switch
    {
        OpcUa => "OPC UA 服务器",
        HttpPush => "HTTP 推送",
        AlarmNotifications => "报警通知",
        ScheduledReports => "定时报表",
        QueryApi => "只读查询接口",
        _ => feature
    };
}

public sealed class LicensePayload
{
    public int V { get; set; } = 1;

    public string Customer { get; set; } = "";

    public string Edition { get; set; } = "community";

    public int? DeviceLimit { get; set; }

    public int? PointLimit { get; set; }

    public string? ExpiresAt { get; set; }

    public List<string> Features { get; set; } = [];

    public string? MachineFingerprint { get; set; }

    public string IssuedAt { get; set; } = "";
}

public sealed class LicenseCheck
{
    public bool Ok { get; init; }

    public string Code { get; init; } = "";

    public string Message { get; init; } = "";

    public LicensePayload? Payload { get; init; }

    public string Canonical { get; init; } = "";
}

/// <summary>
/// Signed license file. The payload is canonical JSON; the signature is ECDSA P-256 over those bytes.
/// </summary>
public static class LicenseCodec
{
    public static string Canonical(LicensePayload payload)
    {
        var features = (payload.Features ?? [])
            .Select(feature => (feature ?? "").Trim().ToLowerInvariant())
            .Where(feature => feature.Length > 0)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToList();
        var customer = (payload.Customer ?? "").Trim();
        var edition = (payload.Edition ?? "").Trim().ToLowerInvariant();
        var issued = NormalizeTime(payload.IssuedAt);
        var expires = string.IsNullOrWhiteSpace(payload.ExpiresAt) ? null : NormalizeTime(payload.ExpiresAt);
        var fingerprint = string.IsNullOrWhiteSpace(payload.MachineFingerprint)
            ? null
            : payload.MachineFingerprint.Trim().ToLowerInvariant();
        var builder = new StringBuilder(256);
        builder.Append("{\"customer\":").Append(Json(customer));
        builder.Append(",\"deviceLimit\":").Append(Num(payload.DeviceLimit));
        builder.Append(",\"edition\":").Append(Json(edition));
        builder.Append(",\"expiresAt\":").Append(expires is null ? "null" : Json(expires));
        builder.Append(",\"features\":[");
        for (var i = 0; i < features.Count; i++)
        {
            if (i > 0)
            {
                builder.Append(',');
            }

            builder.Append(Json(features[i]));
        }

        builder.Append("],\"issuedAt\":").Append(Json(issued));
        builder.Append(",\"machineFingerprint\":").Append(fingerprint is null ? "null" : Json(fingerprint));
        builder.Append(",\"pointLimit\":").Append(Num(payload.PointLimit));
        builder.Append(",\"v\":").Append(payload.V.ToString(CultureInfo.InvariantCulture));
        builder.Append('}');
        return builder.ToString();
    }

    public static string Sign(LicensePayload payload, ECDsa privateKey)
    {
        var canonical = Canonical(payload);
        var signature = privateKey.SignData(Encoding.UTF8.GetBytes(canonical), HashAlgorithmName.SHA256);
        return JsonSerializer.Serialize(new LicenseFile
        {
            Alg = ProductKeys.Algorithm,
            Payload = Base64Url(Encoding.UTF8.GetBytes(canonical)),
            Signature = Base64Url(signature)
        }, JsonOptions);
    }

    public static LicenseCheck Verify(string document, ECDsa publicKey)
    {
        LicenseFile? file;
        try
        {
            file = JsonSerializer.Deserialize<LicenseFile>(document, JsonOptions);
        }
        catch (JsonException)
        {
            return Fail("license_malformed", "许可证文件不是合法的 JSON。");
        }

        if (file is null || string.IsNullOrWhiteSpace(file.Payload) || string.IsNullOrWhiteSpace(file.Signature))
        {
            return Fail("license_malformed", "许可证文件缺少 payload 或 signature。");
        }

        if (!string.Equals(file.Alg, ProductKeys.Algorithm, StringComparison.Ordinal))
        {
            return Fail("license_alg", "许可证算法不是 ES256。");
        }

        byte[] payloadBytes;
        byte[] signature;
        try
        {
            payloadBytes = FromBase64Url(file.Payload);
            signature = FromBase64Url(file.Signature);
        }
        catch (FormatException)
        {
            return Fail("license_malformed", "许可证的 payload 或签名不是 base64url。");
        }

        var canonical = Encoding.UTF8.GetString(payloadBytes);
        if (!publicKey.VerifyData(payloadBytes, signature, HashAlgorithmName.SHA256))
        {
            return Fail("license_signature", "许可证签名无效。请确认文件来自厂商，且本机使用的是对应公钥。");
        }

        LicensePayload? payload;
        try
        {
            payload = JsonSerializer.Deserialize<LicensePayload>(canonical, JsonOptions);
        }
        catch (JsonException)
        {
            return Fail("license_malformed", "许可证正文无法解析。");
        }

        if (payload is null)
        {
            return Fail("license_malformed", "许可证正文为空。");
        }

        var normalized = Canonical(payload);
        if (!string.Equals(normalized, canonical, StringComparison.Ordinal))
        {
            return Fail("license_canonical", "许可证正文字段顺序或取值不符合约定，已拒绝。");
        }

        var error = Validate(payload);
        if (error is not null)
        {
            return Fail("license_fields", error);
        }

        return new LicenseCheck { Ok = true, Payload = payload, Canonical = canonical };
    }

    public static string? Validate(LicensePayload payload)
    {
        if (payload.V != 1)
        {
            return "不认识的许可证版本。";
        }

        var customer = (payload.Customer ?? "").Trim();
        if (customer.Length is < 1 or > 120)
        {
            return "客户名称不能为空，且不超过 120 个字符。";
        }

        var edition = (payload.Edition ?? "").Trim().ToLowerInvariant();
        if (edition is not ("community" or "commercial"))
        {
            return "版本只能是 community 或 commercial。";
        }

        if (payload.DeviceLimit is < 0 || payload.PointLimit is < 0)
        {
            return "设备数和点位数上限不能为负数。留空表示不限制。";
        }

        if (!TryTime(payload.IssuedAt, out _))
        {
            return "issuedAt 不是 UTC 时间。";
        }

        if (!string.IsNullOrWhiteSpace(payload.ExpiresAt) && !TryTime(payload.ExpiresAt, out _))
        {
            return "expiresAt 不是 UTC 时间。留空表示不过期。";
        }

        foreach (var feature in payload.Features ?? [])
        {
            var token = (feature ?? "").Trim().ToLowerInvariant();
            if (token.Length is < 1 or > 40 || token.Any(ch => !(char.IsAsciiLetterOrDigit(ch) || ch is '-' or '*')))
            {
                return "功能标记只允许字母、数字、连字符，或 *。";
            }
        }

        if (!string.IsNullOrWhiteSpace(payload.MachineFingerprint))
        {
            var fingerprint = payload.MachineFingerprint.Trim().ToLowerInvariant();
            if (fingerprint.Length is < 8 or > 128 || fingerprint.Any(ch => !Uri.IsHexDigit(ch)))
            {
                return "机器指纹必须是 8 到 128 位十六进制。留空表示不绑定机器。";
            }
        }

        return null;
    }

    public static bool TryTime(string? text, out DateTimeOffset value)
    {
        value = default;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        return DateTimeOffset.TryParse(
            text,
            CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
            out value);
    }

    private static string NormalizeTime(string? text)
    {
        if (!TryTime(text, out var value))
        {
            return "";
        }

        return value.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);
    }

    private static string Json(string value) => JsonSerializer.Serialize(value);

    private static string Num(int? value) =>
        value?.ToString(CultureInfo.InvariantCulture) ?? "null";

    private static LicenseCheck Fail(string code, string message) =>
        new() { Ok = false, Code = code, Message = message };

    public static string Base64Url(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    public static byte[] FromBase64Url(string text)
    {
        var padded = text.Replace('-', '+').Replace('_', '/');
        switch (padded.Length % 4)
        {
            case 2: padded += "=="; break;
            case 3: padded += "="; break;
        }

        return Convert.FromBase64String(padded);
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    private sealed class LicenseFile
    {
        public string Alg { get; set; } = "";

        public string Payload { get; set; } = "";

        public string Signature { get; set; } = "";
    }
}
