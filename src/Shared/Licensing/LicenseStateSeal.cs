using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace IotDaq.Licensing;

/// <summary>
/// HMAC-SHA256 over the local license clock state. The key lives outside the database.
/// Editing the JSON without the key fails closed. This does not prove the disk to a remote party.
/// </summary>
public static class LicenseStateSeal
{
    public static string Canonical(long lastSeenUnixMs, string? documentSha256, string? code) =>
        "v1|" + lastSeenUnixMs.ToString(System.Globalization.CultureInfo.InvariantCulture)
        + "|" + (documentSha256 ?? "").Trim().ToLowerInvariant()
        + "|" + (code ?? "").Trim().ToLowerInvariant();

    public static string Sign(byte[] key, long lastSeenUnixMs, string? documentSha256, string? code)
    {
        var mac = HMACSHA256.HashData(key, Encoding.UTF8.GetBytes(Canonical(lastSeenUnixMs, documentSha256, code)));
        return Convert.ToHexString(mac).ToLowerInvariant();
    }

    public static bool Verify(byte[] key, long lastSeenUnixMs, string? documentSha256, string? code, string? mac)
    {
        if (string.IsNullOrWhiteSpace(mac))
        {
            return false;
        }

        byte[] actual;
        try
        {
            actual = Convert.FromHexString(mac.Trim());
        }
        catch (FormatException)
        {
            return false;
        }

        var expected = HMACSHA256.HashData(key, Encoding.UTF8.GetBytes(Canonical(lastSeenUnixMs, documentSha256, code)));
        return CryptographicOperations.FixedTimeEquals(expected, actual);
    }

    public static string DocumentHash(string? document)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(document ?? ""));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    public static LicenseStateDocument Create(byte[] key, long lastSeenUnixMs, string? documentSha256, string? code)
    {
        var normalizedCode = (code ?? "").Trim().ToLowerInvariant();
        var hash = (documentSha256 ?? "").Trim().ToLowerInvariant();
        return new LicenseStateDocument
        {
            V = 1,
            LastSeenUnixMs = lastSeenUnixMs,
            DocumentSha256 = hash,
            Code = normalizedCode,
            Mac = Sign(key, lastSeenUnixMs, hash, normalizedCode)
        };
    }

    public static string Serialize(LicenseStateDocument document) =>
        JsonSerializer.Serialize(document, Json);

    public static bool TryRead(string? json, byte[] key, out LicenseStateDocument document)
    {
        document = new LicenseStateDocument();
        if (string.IsNullOrWhiteSpace(json))
        {
            return false;
        }

        try
        {
            var parsed = JsonSerializer.Deserialize<LicenseStateDocument>(json, Json);
            if (parsed is null)
            {
                return false;
            }

            document = parsed;
            return Verify(key, parsed.LastSeenUnixMs, parsed.DocumentSha256, parsed.Code, parsed.Mac);
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
}

public sealed class LicenseStateDocument
{
    public int V { get; set; } = 1;

    public long LastSeenUnixMs { get; set; }

    public string DocumentSha256 { get; set; } = "";

    public string Code { get; set; } = "";

    public string Mac { get; set; } = "";
}
