using System.Security.Cryptography;
using System.Text;

namespace IotDaq.Persistence.Shop;

public static class ContentHash
{
    public static string Sha256(string? content)
    {
        var text = (content ?? "").Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text))).ToLowerInvariant();
    }
}
