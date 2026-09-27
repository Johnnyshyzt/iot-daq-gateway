using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace Studio.Host.Central;

public static class CentralSession
{
    public static string Hash(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();

    public static string Issue(string key, string gatewayId, long expiresUnixMs)
    {
        var nonce = Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant();
        var payload = string.Join('.', "v1", gatewayId, expiresUnixMs.ToString(CultureInfo.InvariantCulture), nonce);
        return payload + "." + Sign(key, payload);
    }

    public static bool TryParse(string key, string? token, out string gatewayId, out long expiresUnixMs)
    {
        gatewayId = "";
        expiresUnixMs = 0;
        if (string.IsNullOrWhiteSpace(token))
        {
            return false;
        }

        var parts = token.Split('.');
        if (parts.Length != 5 || parts[0] != "v1" || parts[4].Length != 64)
        {
            return false;
        }

        var payload = string.Join('.', parts[0], parts[1], parts[2], parts[3]);
        var expected = Sign(key, payload);
        if (!CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(expected), Encoding.ASCII.GetBytes(parts[4])))
        {
            return false;
        }

        if (!long.TryParse(parts[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out expiresUnixMs))
        {
            return false;
        }

        gatewayId = parts[1];
        return true;
    }

    private static string Sign(string key, string payload) =>
        Convert.ToHexString(HMACSHA256.HashData(Encoding.UTF8.GetBytes(key), Encoding.UTF8.GetBytes(payload))).ToLowerInvariant();
}

public interface ICentralTransport
{
    Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken);
}

public sealed class HttpCentralTransport(IConfiguration configuration, IHttpClientFactory factory) : ICentralTransport
{
    public Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var url = configuration["Central:Url"];
        if (string.IsNullOrWhiteSpace(url))
        {
            throw new InvalidOperationException("未配置 Central:Url。边缘继续独立运行。");
        }

        var relative = request.RequestUri ?? throw new InvalidOperationException("缺少请求地址。");
        var absolute = relative.IsAbsoluteUri
            ? relative
            : new Uri(new Uri(url.TrimEnd('/') + "/"), relative);
        request.RequestUri = absolute;
        return factory.CreateClient("central").SendAsync(request, cancellationToken);
    }
}

public sealed class HostMode
{
    public bool Central { get; init; }
}
