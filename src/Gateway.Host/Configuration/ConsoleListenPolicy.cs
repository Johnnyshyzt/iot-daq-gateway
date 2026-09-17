using Gateway.Abstractions.Configuration;

namespace Gateway.Host.Configuration;

internal sealed record ResolvedConsole(
    bool Listen,
    string Bind,
    int Port,
    string? Token,
    bool AuthRequired,
    string? SkipReason);

internal static class ConsoleListenPolicy
{
    public const string TokenEnvironmentVariable = "GATEWAY_CONSOLE_TOKEN";
    public const string PortEnvironmentVariable = "GATEWAY_CONSOLE_PORT";
    public const string BindEnvironmentVariable = "GATEWAY_CONSOLE_BIND";

    public static ResolvedConsole Resolve(ConsoleOptions yaml, Func<string, string?>? getenv = null)
    {
        ArgumentNullException.ThrowIfNull(yaml);
        getenv ??= static name => Environment.GetEnvironmentVariable(name);

        if (!yaml.Enabled)
        {
            return new ResolvedConsole(false, yaml.Bind, yaml.Port, null, false, "console.enabled 为 false");
        }

        var bind = FirstNonEmpty(
            getenv(BindEnvironmentVariable),
            yaml.Bind,
            "127.0.0.1");

        var port = yaml.Port <= 0 ? 8080 : yaml.Port;
        var portEnv = getenv(PortEnvironmentVariable);
        if (!string.IsNullOrWhiteSpace(portEnv))
        {
            if (!int.TryParse(portEnv, out port) || port is < 1 or > 65535)
            {
                return new ResolvedConsole(
                    false,
                    bind,
                    yaml.Port,
                    null,
                    false,
                    $"{PortEnvironmentVariable} 不是有效端口");
            }
        }

        if (port is < 1 or > 65535)
        {
            return new ResolvedConsole(false, bind, port, null, false, "console.port 必须是 1-65535");
        }

        var token = FirstNonEmpty(
            getenv(TokenEnvironmentVariable),
            yaml.Token,
            null);
        if (token.Length == 0)
        {
            token = null;
        }

        var authRequired = !string.IsNullOrEmpty(token);
        if (!IsLoopback(bind) && !authRequired)
        {
            return new ResolvedConsole(
                false,
                bind,
                port,
                null,
                true,
                $"绑定 {bind} 时必须设置 console.token 或环境变量 {TokenEnvironmentVariable}，拒绝无口令的内网管理口");
        }

        return new ResolvedConsole(true, bind, port, token, authRequired, null);
    }

    public static bool IsLoopback(string bind)
    {
        if (string.IsNullOrWhiteSpace(bind))
        {
            return true;
        }

        return bind.Trim() switch
        {
            "127.0.0.1" or "localhost" or "::1" or "[::1]" => true,
            _ => false
        };
    }

    private static string FirstNonEmpty(params string?[] values)
    {
        foreach (var value in values)
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value.Trim();
            }
        }

        return values[^1] ?? string.Empty;
    }
}
