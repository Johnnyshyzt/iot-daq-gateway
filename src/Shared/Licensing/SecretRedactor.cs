using System.Text;
using System.Text.RegularExpressions;

namespace IotDaq.Licensing;

public static partial class SecretRedactor
{
    private static readonly string[] KeyNames =
    [
        "password", "passwd", "pwd", "secret", "token", "authorization", "pfxpassword",
        "privatekey", "connectionstring", "apikey", "signingkey", "bootstrap"
    ];

    public static string Redact(string? text, IEnumerable<string>? secrets = null)
    {
        if (string.IsNullOrEmpty(text))
        {
            return text ?? "";
        }

        var value = CredentialPattern().Replace(text, "$1$2***");
        value = BearerPattern().Replace(value, "Bearer ***");
        if (secrets is null)
        {
            return value;
        }

        foreach (var secret in secrets)
        {
            if (string.IsNullOrEmpty(secret) || secret.Length < 4)
            {
                continue;
            }

            value = value.Replace(secret, "***", StringComparison.Ordinal);
        }

        return value;
    }

    public static string Hex(ReadOnlySpan<byte> raw, IEnumerable<string>? secrets = null)
    {
        var bytes = raw.ToArray();
        if (secrets is not null)
        {
            foreach (var secret in secrets)
            {
                if (string.IsNullOrEmpty(secret) || secret.Length < 4)
                {
                    continue;
                }

                Replace(bytes, Encoding.UTF8.GetBytes(secret));
            }
        }

        ReplaceLabeledAscii(bytes);
        if (bytes.Length > 256)
        {
            bytes = bytes[..256];
        }

        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    public static string RedactJson(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return json ?? "";
        }

        try
        {
            using var document = System.Text.Json.JsonDocument.Parse(json);
            using var stream = new MemoryStream();
            using (var writer = new System.Text.Json.Utf8JsonWriter(stream))
            {
                Write(writer, document.RootElement);
            }

            return Encoding.UTF8.GetString(stream.ToArray());
        }
        catch (System.Text.Json.JsonException)
        {
            return Redact(json);
        }
    }

    private static void Write(System.Text.Json.Utf8JsonWriter writer, System.Text.Json.JsonElement element)
    {
        switch (element.ValueKind)
        {
            case System.Text.Json.JsonValueKind.Object:
                writer.WriteStartObject();
                foreach (var property in element.EnumerateObject())
                {
                    writer.WritePropertyName(property.Name);
                    if (IsSensitiveName(property.Name) && property.Value.ValueKind == System.Text.Json.JsonValueKind.String)
                    {
                        var text = property.Value.GetString() ?? "";
                        writer.WriteStringValue(text.Length == 0 ? "" : "***");
                    }
                    else
                    {
                        Write(writer, property.Value);
                    }
                }

                writer.WriteEndObject();
                break;
            case System.Text.Json.JsonValueKind.Array:
                writer.WriteStartArray();
                foreach (var item in element.EnumerateArray())
                {
                    Write(writer, item);
                }

                writer.WriteEndArray();
                break;
            default:
                element.WriteTo(writer);
                break;
        }
    }

    private static bool IsSensitiveName(string name)
    {
        var token = name.Replace("_", "", StringComparison.Ordinal).Replace("-", "", StringComparison.Ordinal);
        return KeyNames.Any(key => token.Contains(key, StringComparison.OrdinalIgnoreCase));
    }

    private static void ReplaceLabeledAscii(byte[] bytes)
    {
        var ascii = Encoding.ASCII.GetString(bytes);
        foreach (Match match in CredentialPattern().Matches(ascii))
        {
            if (!match.Groups[3].Success)
            {
                continue;
            }

            var start = match.Groups[3].Index;
            var length = match.Groups[3].Length;
            for (var i = 0; i < length && start + i < bytes.Length; i++)
            {
                bytes[start + i] = (byte)'*';
            }
        }
    }

    private static void Replace(byte[] buffer, byte[] needle)
    {
        if (needle.Length == 0 || needle.Length > buffer.Length)
        {
            return;
        }

        for (var i = 0; i <= buffer.Length - needle.Length; i++)
        {
            var found = true;
            for (var j = 0; j < needle.Length; j++)
            {
                if (buffer[i + j] != needle[j])
                {
                    found = false;
                    break;
                }
            }

            if (!found)
            {
                continue;
            }

            for (var j = 0; j < needle.Length; j++)
            {
                buffer[i + j] = (byte)'*';
            }

            i += needle.Length - 1;
        }
    }

    [GeneratedRegex(@"(?i)(password|passwd|pwd|secret|token|authorization|pfxPassword)(['""]?\s*[:=]\s*['""]?)(\S+)", RegexOptions.CultureInvariant)]
    private static partial Regex CredentialPattern();

    [GeneratedRegex(@"(?i)Bearer\s+\S+", RegexOptions.CultureInvariant)]
    private static partial Regex BearerPattern();
}
