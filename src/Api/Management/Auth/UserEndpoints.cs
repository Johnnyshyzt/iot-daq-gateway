using Studio.Contracts;
using Studio.Host.Config;
using Studio.Host.Endpoints;

namespace Studio.Host.Auth;

public static class UserEndpoints
{
    public static void Map(RouteGroupBuilder api)
    {
        api.MapGet("/users", (AccountStore accounts) => ApiResults.Ok(new { users = accounts.ListUsers() }));

        api.MapPost("/users", (UserWrite? body, AccountStore accounts, HttpContext http, IotDaq.Persistence.GatewayPersistence database) =>
        {
            if (!accounts.TryCreate(body?.Username, body?.Role, body?.Password, out var error))
            {
                return ApiResults.Error(StatusCodes.Status400BadRequest, "user_invalid", error);
            }

            ConfigAudit.Write(http, database, "user.create", body?.Username ?? "", "创建用户，角色 " + (body?.Role ?? ""));
            return ApiResults.Ok(new { users = accounts.ListUsers() });
        });

        api.MapPut("/users/{name}", (string name, UserWrite? body, AccountStore accounts, HttpContext http, IotDaq.Persistence.GatewayPersistence database) =>
        {
            if (!accounts.TryUpdate(name, body?.Role, body?.Password, out var error))
            {
                return ApiResults.Error(StatusCodes.Status400BadRequest, "user_invalid", error);
            }

            ConfigAudit.Write(http, database, "user.update", name, "更新用户，角色 " + (body?.Role ?? ""));
            return ApiResults.Ok(new { users = accounts.ListUsers() });
        });

        api.MapDelete("/users/{name}", (string name, AccountStore accounts, HttpContext http, IotDaq.Persistence.GatewayPersistence database) =>
        {
            if (!accounts.TryDelete(name, out var error))
            {
                return ApiResults.Error(StatusCodes.Status400BadRequest, "user_invalid", error);
            }

            ConfigAudit.Write(http, database, "user.delete", name, "删除用户");
            return Results.NoContent();
        });
    }
}

public sealed class UserWrite
{
    public string? Username { get; set; }

    public string? Role { get; set; }

    public string? Password { get; set; }
}
