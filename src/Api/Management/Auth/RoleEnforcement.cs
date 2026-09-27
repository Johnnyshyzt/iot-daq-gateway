using Studio.Contracts;
using Studio.Host;
using Studio.Host.Endpoints;
using Studio.Host.Licensing;

namespace Studio.Host.Auth;

public sealed class RoleEnforcementMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext http, LicenseService licensing)
    {
        var access = ApiPolicy.Required(http.Request.Method, http.Request.Path.Value);
        if (access is ApiPolicy.Anonymous or ApiPolicy.ApiKey or ApiPolicy.Read)
        {
            await next(http);
            return;
        }

        var role = http.Items["studio.role"] as string ?? "";
        var allowed = access switch
        {
            ApiPolicy.Admin => role == StudioRoles.Admin,
            ApiPolicy.Operate => StudioRoles.CanOperate(role),
            _ => StudioRoles.CanWrite(role)
        };
        if (!allowed)
        {
            await Write(http, StatusCodes.Status403Forbidden, "forbidden", RoleEnforcement.Denied(access));
            return;
        }

        if (ApiPolicy.BlocksConfigWhenTampered(access, http.Request.Path.Value))
        {
            var block = licensing.ConfigBlockMessage();
            if (!string.IsNullOrEmpty(block))
            {
                await Write(http, StatusCodes.Status403Forbidden, "security_block", block);
                return;
            }
        }

        await next(http);
    }

    private static Task Write(HttpContext http, int status, string code, string message)
    {
        http.Response.StatusCode = status;
        return http.Response.WriteAsJsonAsync(new ApiError { Code = code, Message = message }, StudioJson.Options);
    }
}

public static class RoleEnforcement
{
    public static RouteGroupBuilder EnforceRoles(this RouteGroupBuilder api)
    {
        api.AddEndpointFilter(async (context, next) =>
        {
            var http = context.HttpContext;
            var access = ApiPolicy.Required(http.Request.Method, http.Request.Path.Value);
            if (access is ApiPolicy.Anonymous or ApiPolicy.ApiKey or ApiPolicy.Read)
            {
                return await next(context);
            }

            var role = http.Items["studio.role"] as string ?? "";
            var allowed = access switch
            {
                ApiPolicy.Admin => role == StudioRoles.Admin,
                ApiPolicy.Operate => StudioRoles.CanOperate(role),
                _ => StudioRoles.CanWrite(role)
            };
            if (!allowed)
            {
                return ApiResults.Error(StatusCodes.Status403Forbidden, "forbidden", Denied(access));
            }

            if (ApiPolicy.BlocksConfigWhenTampered(access, http.Request.Path.Value))
            {
                var licensing = http.RequestServices.GetService<LicenseService>();
                var block = licensing?.ConfigBlockMessage();
                if (!string.IsNullOrEmpty(block))
                {
                    return ApiResults.Error(StatusCodes.Status403Forbidden, "security_block", block);
                }
            }

            return await next(context);
        });
        return api;
    }

    internal static string Denied(string access) => access switch
    {
        ApiPolicy.Admin => "只有管理员可以执行此操作。",
        ApiPolicy.Operate => "当前角色不能填写停机原因或报废。",
        _ => "当前角色无权修改配置"
    };
}
