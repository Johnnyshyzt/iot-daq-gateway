using System.Text.Json;
using Gateway.Abstractions.Contract;
using Gateway.Abstractions.Contracts;
using Gateway.Abstractions.Security;
using IotDaq.Licensing;
using IotDaq.Persistence;
using Studio.Host.Endpoints;
using Studio.Host.Licensing;

namespace Studio.Host.Northbound;

public static class OpcUaEndpoints
{
    public const string SettingKey = "opcua";

    public static void Map(RouteGroupBuilder api)
    {
        api.MapGet("/opcua", (GatewayPersistence database, IEnumerable<IOpcUaControl> controls) =>
            ApiResults.Ok(new
            {
                settings = ToView(Read(database)),
                runtime = controls.FirstOrDefault()?.Current ?? new OpcUaRuntimeInfo()
            }));

        api.MapPut("/opcua", (OpcUaWrite? body, HttpContext http, GatewayPersistence database, IEnumerable<IOpcUaControl> controls, LicenseService licensing) =>
        {
            if (body?.Enabled == true && !licensing.Allows(LicenseFeatures.OpcUa))
            {
                return ApiResults.Error(StatusCodes.Status403Forbidden, "license_feature", licensing.Denial(LicenseFeatures.OpcUa));
            }

            if (body is null)
            {
                return ApiResults.Error(StatusCodes.Status400BadRequest, "invalid_opcua", "缺少 OPC UA 设置");
            }

            if (body.Port is < 1 or > 65535)
            {
                return ApiResults.Error(StatusCodes.Status400BadRequest, "invalid_opcua", "端口需在 1 到 65535 之间");
            }

            if (!body.AllowNone && !body.AllowSignAndEncrypt)
            {
                return ApiResults.Error(StatusCodes.Status400BadRequest, "invalid_opcua", "至少启用一种安全策略");
            }

            var current = Read(database);
            var username = (body.Username ?? "").Trim();
            var hash = current.PasswordHash;
            if (body.ClearPassword || username.Length == 0)
            {
                hash = "";
            }
            else if (!string.IsNullOrEmpty(body.Password) && body.Password != "***")
            {
                hash = SecretHash.Pbkdf2(body.Password);
            }

            if (username.Length > 0 && string.IsNullOrEmpty(hash))
            {
                return ApiResults.Error(StatusCodes.Status400BadRequest, "invalid_opcua", "填写用户名时需要设置密码");
            }

            var stored = new OpcUaStored
            {
                Enabled = body.Enabled,
                Port = body.Port,
                AllowAnonymous = body.AllowAnonymous,
                AllowNone = body.AllowNone,
                AllowSignAndEncrypt = body.AllowSignAndEncrypt,
                Username = username,
                PasswordHash = hash
            };
            database.SetSetting(SettingKey, JsonSerializer.Serialize(stored, NorthboundPayload.Json));
            ConfigAudit.Write(http, database, "opcua.update", "opcua", stored.Enabled ? "启用 OPC UA 服务器，端口 " + stored.Port.ToString(System.Globalization.CultureInfo.InvariantCulture) : "关闭 OPC UA 服务器");
            foreach (var control in controls)
            {
                control.Reload();
            }

            return ApiResults.Ok(new
            {
                settings = ToView(stored),
                runtime = controls.FirstOrDefault()?.Current ?? new OpcUaRuntimeInfo()
            });
        }).RequireWriter();
    }

    public static OpcUaStored Read(GatewayPersistence database)
    {
        var json = database.GetSetting(SettingKey);
        if (string.IsNullOrWhiteSpace(json))
        {
            return new OpcUaStored();
        }

        try
        {
            return JsonSerializer.Deserialize<OpcUaStored>(json, NorthboundPayload.Json) ?? new OpcUaStored();
        }
        catch (JsonException)
        {
            return new OpcUaStored();
        }
    }

    private static object ToView(OpcUaStored stored) => new
    {
        enabled = stored.Enabled,
        port = stored.Port,
        allowAnonymous = stored.AllowAnonymous,
        allowNone = stored.AllowNone,
        allowSignAndEncrypt = stored.AllowSignAndEncrypt,
        username = stored.Username,
        hasPassword = !string.IsNullOrEmpty(stored.PasswordHash)
    };

    private static RouteHandlerBuilder RequireWriter(this RouteHandlerBuilder builder) =>
        builder.AddEndpointFilter(async (context, next) =>
        {
            var role = context.HttpContext.Items["studio.role"] as string;
            if (role is not ("admin" or "engineer"))
            {
                return ApiResults.Error(StatusCodes.Status403Forbidden, "forbidden", "当前角色无权修改配置");
            }

            return await next(context);
        });
}

public sealed class OpcUaStored
{
    public bool Enabled { get; set; }

    public int Port { get; set; } = 48400;

    public bool AllowAnonymous { get; set; } = true;

    public bool AllowNone { get; set; } = true;

    public bool AllowSignAndEncrypt { get; set; } = true;

    public string Username { get; set; } = "";

    public string PasswordHash { get; set; } = "";
}

public sealed class OpcUaWrite
{
    public bool Enabled { get; set; }

    public int Port { get; set; } = 48400;

    public bool AllowAnonymous { get; set; } = true;

    public bool AllowNone { get; set; } = true;

    public bool AllowSignAndEncrypt { get; set; } = true;

    public string? Username { get; set; }

    public string? Password { get; set; }

    public bool ClearPassword { get; set; }
}
