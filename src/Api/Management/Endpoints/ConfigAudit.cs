using IotDaq.Persistence;

namespace Studio.Host.Endpoints;

internal static class ConfigAudit
{
    public static void Write(HttpContext http, GatewayPersistence database, string action, string target, string detail)
    {
        database.AppendAudit(
            http.Items["studio.user"] as string ?? "",
            http.Items["studio.role"] as string ?? "",
            action,
            target,
            detail);
    }
}
