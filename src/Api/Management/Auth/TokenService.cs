using System.Security.Cryptography;
using System.Text;

namespace Studio.Host.Auth;

public sealed class TokenService
{
    public const string DevSigningKey = "studio-m1-dev-signing-key";
    private readonly string _key;
    private readonly AccountStore _accounts;
    private readonly IConfiguration _configuration;
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, long> _lastSeen = new();

    public TokenService(IConfiguration configuration, AccountStore accounts, ILogger<TokenService> logger)
    {
        _accounts = accounts;
        _configuration = configuration;
        _accounts.EnsureInitialized();
        _key = _accounts.SigningKey;
        if (string.Equals(_key, DevSigningKey, StringComparison.Ordinal)
            && string.IsNullOrWhiteSpace(configuration["Studio:SigningKey"]))
        {
            logger.LogWarning(
                "Studio:SigningKey is empty; using the development fallback. Set Studio:SigningKey before sharing this host. Field packages generate a key under data/auth.");
        }
    }

    public bool MustChangePassword(string? username) => _accounts.MustChangePassword(username);

    public LoginOutcome Login(string? username, string? password)
    {
        var attempt = _accounts.Authenticate(username, password);
        if (!attempt.Ok)
        {
            return new LoginOutcome { Code = attempt.Code, Message = attempt.Message };
        }

        var minutes = SessionMinutes;
        var expiresAt = DateTimeOffset.UtcNow.AddMinutes(minutes);
        var token = Issue(username!.Trim(), attempt.Role, expiresAt);
        return new LoginOutcome
        {
            Ok = true,
            Token = token,
            Role = attempt.Role,
            ExpiresAt = expiresAt,
            Message = ""
        };
    }

    public bool TryLogin(string? username, string? password, out string token, out string role, out DateTimeOffset expiresAt)
    {
        var outcome = Login(username, password);
        token = outcome.Token;
        role = outcome.Role;
        expiresAt = outcome.ExpiresAt;
        return outcome.Ok;
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

        var key = fields[0] + ":" + fields[2];
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var seen = _lastSeen.GetOrAdd(key, now);
        if (SessionRules.Expired(now, exp, seen, IdleMinutes))
        {
            return false;
        }

        _lastSeen[key] = now;
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

    private int SessionMinutes =>
        int.TryParse(_configuration["Studio:SessionMinutes"], System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var minutes)
            ? Math.Clamp(minutes, 0, 10_080)
            : 720;

    private int IdleMinutes =>
        int.TryParse(_configuration["Studio:IdleMinutes"], System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var minutes)
            ? Math.Clamp(minutes, 0, 10_080)
            : 120;

    private byte[] Sign(string payload) =>
        HMACSHA256.HashData(Encoding.UTF8.GetBytes(_key), Encoding.UTF8.GetBytes(payload));

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

    private static readonly HashSet<string> Roles = new(StringComparer.Ordinal) { "admin", "engineer", "operator", "viewer" };
}

public static class SessionRules
{
    public static bool Expired(long nowUnixSeconds, long expiresUnixSeconds, long lastSeenUnixSeconds, int idleMinutes)
    {
        if (nowUnixSeconds >= expiresUnixSeconds)
        {
            return true;
        }

        if (idleMinutes <= 0)
        {
            return false;
        }

        return nowUnixSeconds - lastSeenUnixSeconds >= idleMinutes * 60L;
    }
}

public sealed class LoginOutcome
{
    public bool Ok { get; init; }

    public string Token { get; set; } = "";

    public string Role { get; set; } = "";

    public DateTimeOffset ExpiresAt { get; set; }

    public string Code { get; set; } = "";

    public string Message { get; set; } = "";
}

public sealed class StudioAuthMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context, TokenService tokens, AccountStore accounts)
    {
        if (!context.Request.Path.StartsWithSegments("/api"))
        {
            await next(context);
            return;
        }

        if (context.Request.Path.StartsWithSegments("/api/query")
            || (HttpMethods.IsGet(context.Request.Method) && context.Request.Path.StartsWithSegments("/api/contract")))
        {
            await next(context);
            return;
        }

        if (IsAnonymous(context.Request))
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

        if (accounts.MustChangePassword(username) && !IsAllowedDuringPasswordChange(context.Request))
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            await context.Response.WriteAsJsonAsync(
                new Studio.Contracts.ApiError
                {
                    Code = "password_change_required",
                    Message = "必须先修改密码。请使用一次性口令登录后立刻设置新密码。"
                },
                StudioJson.Options);
            return;
        }

        context.Items["studio.user"] = username;
        context.Items["studio.role"] = role;
        await next(context);
    }

    private static bool IsAnonymous(HttpRequest request) =>
        (HttpMethods.IsPost(request.Method) && request.Path.Equals("/api/v1/auth/login", StringComparison.OrdinalIgnoreCase))
        || (HttpMethods.IsGet(request.Method) && request.Path.Equals("/api/v1/auth/posture", StringComparison.OrdinalIgnoreCase))
        || request.Path.StartsWithSegments("/api/central/v1");

    private static bool IsAllowedDuringPasswordChange(HttpRequest request) =>
        (HttpMethods.IsGet(request.Method) && request.Path.Equals("/api/v1/auth/me", StringComparison.OrdinalIgnoreCase))
        || (HttpMethods.IsGet(request.Method) && request.Path.Equals("/api/v1/auth/posture", StringComparison.OrdinalIgnoreCase))
        || (HttpMethods.IsPost(request.Method) && request.Path.Equals("/api/v1/auth/password", StringComparison.OrdinalIgnoreCase));
}
