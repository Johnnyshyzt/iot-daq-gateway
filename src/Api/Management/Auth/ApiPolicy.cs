namespace Studio.Host.Auth;

public static class StudioRoles
{
    public const string Admin = "admin";
    public const string Engineer = "engineer";
    public const string Operator = "operator";
    public const string Viewer = "viewer";

    public static bool IsKnown(string? role) =>
        role is Admin or Engineer or Operator or Viewer;

    public static bool CanWrite(string? role) => role is Admin or Engineer;

    public static bool CanOperate(string? role) => role is Admin or Engineer or Operator;

    public static bool IsReadOnly(string? role) => role is Viewer;
}

public static class ApiPolicy
{
    public const string Anonymous = "anonymous";
    public const string ApiKey = "api-key";
    public const string Read = "read";
    public const string Write = "write";
    public const string Operate = "operate";
    public const string Admin = "admin";

    public static string Required(string method, string? path)
    {
        var route = (path ?? "").TrimEnd('/');
        if (route.Length == 0)
        {
            route = "/";
        }

        var verb = (method ?? "GET").ToUpperInvariant();
        if (route.Equals("/healthz", StringComparison.OrdinalIgnoreCase))
        {
            return Anonymous;
        }

        if (route.StartsWith("/api/contract", StringComparison.OrdinalIgnoreCase))
        {
            return Anonymous;
        }

        if (route.Equals("/api/query/v1/openapi.json", StringComparison.OrdinalIgnoreCase))
        {
            return Anonymous;
        }

        if (route.StartsWith("/api/query", StringComparison.OrdinalIgnoreCase))
        {
            return ApiKey;
        }

        if (route.StartsWith("/api/central/v1", StringComparison.OrdinalIgnoreCase))
        {
            return Anonymous;
        }

        if (verb == "POST" && route.Equals("/api/v1/auth/login", StringComparison.OrdinalIgnoreCase))
        {
            return Anonymous;
        }

        if (verb == "GET" && route.Equals("/api/v1/auth/posture", StringComparison.OrdinalIgnoreCase))
        {
            return Anonymous;
        }

        if (IsAdmin(verb, route))
        {
            return Admin;
        }

        if (verb is "GET" or "HEAD")
        {
            return Read;
        }

        if (verb == "POST" && (route.Equals("/api/v1/auth/password", StringComparison.OrdinalIgnoreCase)
            || route.StartsWith("/api/v1/onboarding/", StringComparison.OrdinalIgnoreCase)))
        {
            return Read;
        }

        if (verb == "POST" && (route.Equals("/api/v1/downtime/assign", StringComparison.OrdinalIgnoreCase)
            || route.Equals("/api/v1/downtime/bulk", StringComparison.OrdinalIgnoreCase)
            || route.Equals("/api/v1/oee/scrap", StringComparison.OrdinalIgnoreCase)
            || route.Equals("/api/v1/tools/change", StringComparison.OrdinalIgnoreCase)
            || route.Equals("/api/v1/tools/count", StringComparison.OrdinalIgnoreCase)))
        {
            return Operate;
        }

        return Write;
    }

    public static bool BlocksConfigWhenTampered(string access, string? path)
    {
        if (access != Write)
        {
            return false;
        }

        var route = path ?? "";
        if (route.Contains("/self-test", StringComparison.OrdinalIgnoreCase)
            || route.Contains("/trace", StringComparison.OrdinalIgnoreCase)
            || route.EndsWith("/ops/diagnose", StringComparison.OrdinalIgnoreCase)
            || route.Contains("/auth/", StringComparison.OrdinalIgnoreCase)
            || route.Contains("/agent/", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return true;
    }

    private static bool IsAdmin(string method, string path)
    {
        if (path.StartsWith("/api/v1/users", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (path.StartsWith("/api/v1/ops/https", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (path.Equals("/api/v1/ops/backup", StringComparison.OrdinalIgnoreCase)
            || path.Equals("/api/v1/ops/restore", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (path.StartsWith("/api/v1/ops/upgrade", StringComparison.OrdinalIgnoreCase) && method != "GET")
        {
            return true;
        }

        if (path.Equals("/api/v1/ops/demo", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (path.Equals("/api/v1/license", StringComparison.OrdinalIgnoreCase) && method is "POST" or "DELETE")
        {
            return true;
        }

        if (path.Equals("/api/v1/license/security/ack", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (method == "POST" && path.StartsWith("/api/v1/programs/", StringComparison.OrdinalIgnoreCase)
            && path.EndsWith("/approve", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (path.StartsWith("/api/v1/central/tokens", StringComparison.OrdinalIgnoreCase) && method != "GET")
        {
            return true;
        }

        if (path.StartsWith("/api/v1/central/rollouts", StringComparison.OrdinalIgnoreCase) && method != "GET")
        {
            return true;
        }

        return false;
    }
}

public static class PasswordPolicy
{
    public static string? Check(string? password, string? username)
    {
        if (string.IsNullOrEmpty(password) || password.Length < 8)
        {
            return "新密码至少 8 位。";
        }

        if (password.Length > 128)
        {
            return "新密码不能超过 128 位。";
        }

        if (password.Contains('\n', StringComparison.Ordinal) || password.Contains('\r', StringComparison.Ordinal))
        {
            return "新密码不能包含换行。";
        }

        if (string.Equals(password, username, StringComparison.OrdinalIgnoreCase) || IsDemo(password))
        {
            return "不能把密码改成演示口令（admin、engineer、operator、viewer）或与用户名相同。";
        }

        if (!password.Any(char.IsLetter) || !password.Any(char.IsDigit))
        {
            return "新密码需要同时包含字母和数字。";
        }

        return null;
    }

    public static bool IsDemo(string password) =>
        password.Equals("admin", StringComparison.Ordinal)
        || password.Equals("engineer", StringComparison.Ordinal)
        || password.Equals("operator", StringComparison.Ordinal)
        || password.Equals("viewer", StringComparison.Ordinal);
}
