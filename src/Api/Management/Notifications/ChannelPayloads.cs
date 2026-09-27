using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using IotDaq.Persistence;

namespace Studio.Host.Notifications;

public sealed class OutboundMessage
{
    public string Title { get; init; } = "采集网关";

    public string Text { get; init; } = "";

    public string Kind { get; init; } = "alarm.raised";

    public string DeviceId { get; init; } = "";

    public string Severity { get; init; } = "";

    public string Code { get; init; } = "";

    public long UnixMs { get; init; }
}

public sealed class PreparedRequest
{
    public required string Url { get; init; }

    public required string Body { get; init; }

    public Dictionary<string, string> Headers { get; init; } = new(StringComparer.OrdinalIgnoreCase);
}

public static class ChannelPayloads
{
    private static readonly JsonSerializerOptions Json = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    private static readonly JsonSerializerOptions Camel = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    public static string ResolveSecret(NotificationChannelRow channel)
    {
        if (!string.IsNullOrWhiteSpace(channel.SecretFromEnv))
        {
            var fromEnv = Environment.GetEnvironmentVariable(channel.SecretFromEnv.Trim());
            if (!string.IsNullOrEmpty(fromEnv))
            {
                return fromEnv;
            }
        }

        return channel.Secret ?? "";
    }

    public static string RedactUrl(string? url)
    {
        if (string.IsNullOrWhiteSpace(url) || !Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            return url ?? "";
        }

        var query = uri.Query.TrimStart('?');
        if (query.Length == 0)
        {
            return url;
        }

        var sensitive = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "key", "access_token", "secret", "sign", "timestamp" };
        var parts = new List<string>();
        var changed = false;
        foreach (var pair in query.Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var eq = pair.IndexOf('=');
            var name = eq < 0 ? pair : pair[..eq];
            var value = eq < 0 ? "" : pair[(eq + 1)..];
            if (sensitive.Contains(Uri.UnescapeDataString(name)) && value.Length > 0 && value != "***")
            {
                parts.Add(name + "=***");
                changed = true;
            }
            else
            {
                parts.Add(pair);
            }
        }

        if (!changed)
        {
            return url;
        }

        var builder = new UriBuilder(uri) { Query = string.Join("&", parts) };
        return builder.Uri.AbsoluteUri;
    }

    public static bool LooksRedacted(string? url) =>
        !string.IsNullOrEmpty(url) && url.Contains("=***", StringComparison.Ordinal);

    public static string DingTalkSign(string secret, long timestampMs)
    {
        var stringToSign = timestampMs.ToString(CultureInfo.InvariantCulture) + "\n" + secret;
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
        return Convert.ToBase64String(hmac.ComputeHash(Encoding.UTF8.GetBytes(stringToSign)));
    }

    public static string FeishuSign(string secret, long timestampSeconds)
    {
        var stringToSign = timestampSeconds.ToString(CultureInfo.InvariantCulture) + "\n" + secret;
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(stringToSign));
        return Convert.ToBase64String(hmac.ComputeHash([]));
    }

    public static string GenericSignature(string secret, string body)
    {
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
        return Convert.ToHexString(hmac.ComputeHash(Encoding.UTF8.GetBytes(body))).ToLowerInvariant();
    }

    public static PreparedRequest? Prepare(NotificationChannelRow channel, string secret, OutboundMessage message, DateTimeOffset now)
    {
        var kind = channel.Kind.Trim().ToLowerInvariant();
        return kind switch
        {
            "wecom" => WeCom(channel, message),
            "dingtalk" => DingTalk(channel, secret, message, now),
            "feishu" or "lark" => Feishu(channel, secret, message, now),
            "webhook" => Webhook(channel, secret, message, now),
            _ => null
        };
    }

    private static PreparedRequest WeCom(NotificationChannelRow channel, OutboundMessage message)
    {
        var body = JsonSerializer.Serialize(new
        {
            msgtype = "markdown",
            markdown = new { content = message.Text }
        }, Json);
        return new PreparedRequest { Url = channel.WebhookUrl, Body = body };
    }

    private static PreparedRequest DingTalk(NotificationChannelRow channel, string secret, OutboundMessage message, DateTimeOffset now)
    {
        var url = channel.WebhookUrl;
        if (!string.IsNullOrEmpty(secret))
        {
            var timestamp = now.ToUnixTimeMilliseconds();
            var sign = Uri.EscapeDataString(DingTalkSign(secret, timestamp));
            var separator = url.Contains('?', StringComparison.Ordinal) ? "&" : "?";
            url += separator + "timestamp=" + timestamp.ToString(CultureInfo.InvariantCulture) + "&sign=" + sign;
        }

        var body = JsonSerializer.Serialize(new
        {
            msgtype = "markdown",
            markdown = new { title = message.Title, text = message.Text }
        }, Json);
        return new PreparedRequest { Url = url, Body = body };
    }

    private static PreparedRequest Feishu(NotificationChannelRow channel, string secret, OutboundMessage message, DateTimeOffset now)
    {
        var timestamp = now.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture);
        if (string.IsNullOrEmpty(secret))
        {
            var plain = JsonSerializer.Serialize(new
            {
                msg_type = "text",
                content = new { text = message.Text }
            }, Json);
            return new PreparedRequest { Url = channel.WebhookUrl, Body = plain };
        }

        var body = JsonSerializer.Serialize(new
        {
            timestamp,
            sign = FeishuSign(secret, now.ToUnixTimeSeconds()),
            msg_type = "text",
            content = new { text = message.Text }
        }, Json);
        return new PreparedRequest { Url = channel.WebhookUrl, Body = body };
    }

    private static PreparedRequest Webhook(NotificationChannelRow channel, string secret, OutboundMessage message, DateTimeOffset now)
    {
        var body = JsonSerializer.Serialize(new
        {
            source = "iot-daq-gateway",
            kind = message.Kind,
            deviceId = message.DeviceId,
            severity = message.Severity,
            code = message.Code,
            message = message.Text,
            unixMs = message.UnixMs
        }, Camel);
        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["X-Daq-Timestamp"] = now.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture)
        };
        if (!string.IsNullOrEmpty(secret))
        {
            headers["X-Daq-Signature"] = "sha256=" + GenericSignature(secret, body);
        }

        return new PreparedRequest { Url = channel.WebhookUrl, Body = body, Headers = headers };
    }

    public static string FormatAlarm(AlarmNotice notice, string? site)
    {
        var action = notice.Kind == "clear" ? "报警恢复" : notice.Kind == "escalation" ? "报警升级" : "报警";
        var where = string.Join(" / ", new[] { notice.Workshop, notice.Line }.Where(item => !string.IsNullOrWhiteSpace(item)));
        var lines = new List<string>
        {
            "【采集网关】" + action,
            "设备：" + notice.DeviceId + (where.Length == 0 ? "" : "（" + where + "）"),
            "级别：" + (string.IsNullOrWhiteSpace(notice.Severity) ? "alarm" : notice.Severity),
            "代码：" + (string.IsNullOrWhiteSpace(notice.Code) ? "-" : notice.Code),
            "内容：" + notice.Message
        };
        if (!string.IsNullOrWhiteSpace(site))
        {
            lines.Add("站点：" + site);
        }

        return string.Join('\n', lines);
    }

    public static string SmtpBody(string subject, string text) =>
        "Subject: " + subject + "\r\n\r\n" + text;

    public static string Escape(string value) => WebUtility.HtmlEncode(value);
}
