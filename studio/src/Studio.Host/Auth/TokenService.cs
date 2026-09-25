using System.Security.Cryptography;
using System.Text;

namespace Studio.Host.Auth;

public sealed class TokenService
{
    public const string DevSigningKey = "studio-m1-dev-signing-key";
    private readonly string _key;
    private readonly IReadOnlyList<StudioUser> _users;

    public TokenService(IConfiguration configuration, ILogger<TokenService> logger)
    {
        var key = configuration["Studio:SigningKey"];
        if (string.IsNullOrWhiteSpace(key))
        {
            key = DevSigningKey;
            logger.LogWarning(
                "Studio:SigningKey is empty; using the development fallback. Set Studio:SigningKey or STUDIO_SIGNING_KEY before sharing this host.");
        }

        _key = key;
        var configured = configuration.GetSection("Studio:Users").Get<List<StudioUser>>() ?? [];
        if (configured.Count == 0)
        {
            configured =
            [
                new StudioUser { Username = "admin", Password = "admin", Role = "admin" },
                new StudioUser { Username = "engineer", Password = "engineer", Role = "engineer" },
                new StudioUser { Username = "viewer", Password = "viewer", Role = "viewer" }
            ];
        }

        _users = configured;
    }

    public IReadOnlyList<StudioUser> Users => _users;

    public bool TryLogin(string? username, string? password, out string token, out string role, out DateTimeOffset expiresAt)
    {
        token = "";
        role = "";
        expiresAt = default;
        if (string.IsNullOrWhiteSpace(username) || password is null || username.Contains('\n', StringComparison.Ordinal))
        {
            return false;
        }

        var user = _users.FirstOrDefault(item => string.Equals(item.Username, username, StringComparison.Ordinal));
        if (user is null || user.Password is null || !Roles.Contains(user.Role) || !FixedEquals(user.Password, password))
        {
            return false;
        }

        expiresAt = DateTimeOffset.UtcNow.AddHours(12);
        role = user.Role;
        token = Issue(user.Username, user.Role, expiresAt);
        return true;
    }

    public bool TryRead(string token, out string username, out string role)
    {
        username = "";
        role = "";
        var parts = token.Split('.');
        if (parts.Length != 2)
        {
            return false;
        }

        string payload;
        try
        {
            payload = Encoding.UTF8.GetString(FromBase64Url(parts[0]));
        }
        catch (FormatException)
        {
            return false;
        }

        var fields = payload.Split('\n');
        if (fields.Length != 3 || !long.TryParse(fields[2], out var exp))
        {
            return false;
        }

        if (DateTimeOffset.UtcNow.ToUnixTimeSeconds() >= exp)
        {
            return false;
        }

        if (!Roles.Contains(fields[1]))
        {
            return false;
        }

        var expected = Sign(payload);
        byte[] actual;
        try
        {
            actual = Convert.FromHexString(parts[1]);
        }
        catch (FormatException)
        {
            return false;
        }

        if (!CryptographicOperations.FixedTimeEquals(expected, actual))
        {
            return false;
        }

        username = fields[0];
        role = fields[1];
        return true;
    }

    private string Issue(string username, string role, DateTimeOffset expiresAt)
    {
        var payload = $"{username}\n{role}\n{expiresAt.ToUnixTimeSeconds().ToString(System.Globalization.CultureInfo.InvariantCulture)}";
        var sig = Convert.ToHexString(Sign(payload)).ToLowerInvariant();
        return $"{ToBase64Url(Encoding.UTF8.GetBytes(payload))}.{sig}";
    }

    private byte[] Sign(string payload) =>
        HMACSHA256.HashData(Encoding.UTF8.GetBytes(_key), Encoding.UTF8.GetBytes(payload));

    private static bool FixedEquals(string left, string right)
    {
        var a = SHA256.HashData(Encoding.UTF8.GetBytes(left));
        var b = SHA256.HashData(Encoding.UTF8.GetBytes(right));
        return CryptographicOperations.FixedTimeEquals(a, b);
    }

    private static string ToBase64Url(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static byte[] FromBase64Url(string value)
    {
        var padded = value.Replace('-', '+').Replace('_', '/');
        padded = (padded.Length % 4) switch
        {
            2 => padded + "==",
            3 => padded + "=",
            _ => padded
        };
        return Convert.FromBase64String(padded);
    }

    private static readonly HashSet<string> Roles = new(StringComparer.Ordinal) { "admin", "engineer", "viewer" };
}

public sealed class StudioAuthMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context, TokenService tokens)
    {
        if (!context.Request.Path.StartsWithSegments("/api"))
        {
            await next(context);
            return;
        }

        if (HttpMethods.IsPost(context.Request.Method)
            && context.Request.Path.Equals("/api/v1/auth/login", StringComparison.OrdinalIgnoreCase))
        {
            await next(context);
            return;
        }

        var header = context.Request.Headers.Authorization.ToString();
        const string prefix = "Bearer ";
        if (!header.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
            || !tokens.TryRead(header[prefix.Length..].Trim(), out var username, out var role))
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            await context.Response.WriteAsJsonAsync(
                new Studio.Contracts.ApiError { Code = "unauthorized", Message = "需要登录" },
                StudioJson.Options);
            return;
        }

        context.Items["studio.user"] = username;
        context.Items["studio.role"] = role;
        await next(context);
    }
}
