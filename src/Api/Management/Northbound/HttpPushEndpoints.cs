using IotDaq.Licensing;
using IotDaq.Persistence;
using Studio.Host.Endpoints;
using Studio.Host.Licensing;

namespace Studio.Host.Northbound;

public static class HttpPushEndpoints
{
    private static readonly HashSet<string> AuthKinds = new(StringComparer.OrdinalIgnoreCase)
    {
        "none", "basic", "bearer", "hmac-sha256"
    };

    public static void Map(RouteGroupBuilder api)
    {
        api.MapGet("/http-push", (HttpPushDispatcher dispatcher, GatewayPersistence database) =>
            ApiResults.Ok(new { targets = database.ListHttpPushTargets().Select(row => ToView(row, dispatcher)).ToList() }));

        api.MapPut("/http-push", (HttpPushWrite? body, HttpContext http, GatewayPersistence database, LicenseService licensing) =>
        {
            if (body?.Enabled == true && !licensing.Allows(LicenseFeatures.HttpPush))
            {
                return ApiResults.Error(StatusCodes.Status403Forbidden, "license_feature", licensing.Denial(LicenseFeatures.HttpPush));
            }

            var error = Validate(body);
            if (error is not null)
            {
                return ApiResults.Error(StatusCodes.Status400BadRequest, "invalid_http_push", error);
            }

            var id = string.IsNullOrWhiteSpace(body!.Id) ? Guid.NewGuid().ToString("N") : body.Id.Trim();
            var existing = database.FindHttpPushTarget(id);
            var secret = ResolveSecret(body, existing);
            var headers = MergeHeaders(body.Headers, existing);
            var row = new HttpPushTargetRow
            {
                Id = id,
                Name = body.Name!.Trim(),
                Enabled = body.Enabled,
                Url = body.Url!.Trim(),
                Method = body.Method!.Trim().ToUpperInvariant(),
                HeadersJson = HttpPushDispatcher.WriteHeaders(headers),
                AuthKind = body.AuthKind!.Trim().ToLowerInvariant(),
                AuthUser = (body.AuthUser ?? "").Trim(),
                AuthSecret = secret,
                SignatureHeader = string.IsNullOrWhiteSpace(body.SignatureHeader) ? "X-DAQ-Signature" : body.SignatureHeader.Trim(),
                SendValues = body.SendValues,
                ValueMode = string.Equals(body.ValueMode, "periodic", StringComparison.OrdinalIgnoreCase) ? "periodic" : "change",
                PeriodicSeconds = Math.Clamp(body.PeriodicSeconds <= 0 ? 30 : body.PeriodicSeconds, 1, 86_400),
                SendStatus = body.SendStatus,
                SendAlarms = body.SendAlarms,
                BatchMax = Math.Clamp(body.BatchMax <= 0 ? 50 : body.BatchMax, 1, 500),
                BatchIntervalMs = Math.Clamp(body.BatchIntervalMs <= 0 ? 1000 : body.BatchIntervalMs, 200, 60_000),
                TimeoutMs = Math.Clamp(body.TimeoutMs <= 0 ? 8000 : body.TimeoutMs, 200, 120_000),
                MaxRetries = Math.Clamp(body.MaxRetries <= 0 ? 8 : body.MaxRetries, 1, 100),
                BackoffInitialMs = Math.Clamp(body.BackoffInitialMs <= 0 ? 1000 : body.BackoffInitialMs, 50, 600_000),
                BackoffMaxMs = Math.Clamp(body.BackoffMaxMs <= 0 ? 60_000 : body.BackoffMaxMs, 50, 3_600_000),
                UpdatedUnixMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
            };
            if (row.BackoffMaxMs < row.BackoffInitialMs)
            {
                row.BackoffMaxMs = row.BackoffInitialMs;
            }

            database.SaveHttpPushTarget(row);
            ConfigAudit.Write(http, database, "httppush.save", id, "保存 HTTP 推送 " + row.Name);
            return ApiResults.Ok(ToView(database.FindHttpPushTarget(id)!, null));
        }).RequireWriter();

        api.MapDelete("/http-push/{id}", (string id, HttpContext http, GatewayPersistence database, HttpPushDispatcher dispatcher) =>
        {
            if (!database.DeleteHttpPushTarget(id))
            {
                return ApiResults.Error(StatusCodes.Status404NotFound, "not_found", "HTTP 推送目标不存在");
            }

            dispatcher.Forget(id);
            ConfigAudit.Write(http, database, "httppush.delete", id, "删除 HTTP 推送目标");
            return ApiResults.Ok(new { deleted = true });
        }).RequireWriter();

        api.MapPost("/http-push/{id}/test", async (string id, HttpContext http, HttpPushDispatcher dispatcher, GatewayPersistence database, LicenseService licensing, CancellationToken cancellationToken) =>
        {
            if (!licensing.Allows(LicenseFeatures.HttpPush))
            {
                return ApiResults.Error(StatusCodes.Status403Forbidden, "license_feature", licensing.Denial(LicenseFeatures.HttpPush));
            }

            var error = await dispatcher.SendTestAsync(id, cancellationToken);
            if (database.FindHttpPushTarget(id) is null)
            {
                return ApiResults.Error(StatusCodes.Status404NotFound, "not_found", "HTTP 推送目标不存在");
            }

            ConfigAudit.Write(http, database, "httppush.test", id, error is null ? "测试推送成功" : "测试推送失败");
            if (error is not null)
            {
                return ApiResults.Error(StatusCodes.Status400BadRequest, "test_failed", error);
            }

            return ApiResults.Ok(new { ok = true });
        }).RequireWriter();
    }

    private static object ToView(HttpPushTargetRow row, HttpPushDispatcher? dispatcher)
    {
        var headers = HttpPushDispatcher.ReadHeaders(row.HeadersJson)
            .Select(header => new HeaderPair { Name = header.Name, Value = string.IsNullOrEmpty(header.Value) ? "" : "***" })
            .ToList();
        return new
        {
            id = row.Id,
            name = row.Name,
            enabled = row.Enabled,
            url = row.Url,
            method = row.Method,
            headers,
            authKind = row.AuthKind,
            authUser = row.AuthUser,
            hasSecret = !string.IsNullOrEmpty(row.AuthSecret),
            signatureHeader = row.SignatureHeader,
            sendValues = row.SendValues,
            valueMode = row.ValueMode,
            periodicSeconds = row.PeriodicSeconds,
            sendStatus = row.SendStatus,
            sendAlarms = row.SendAlarms,
            batchMax = row.BatchMax,
            batchIntervalMs = row.BatchIntervalMs,
            timeoutMs = row.TimeoutMs,
            maxRetries = row.MaxRetries,
            backoffInitialMs = row.BackoffInitialMs,
            backoffMaxMs = row.BackoffMaxMs,
            delivered = row.Delivered,
            failed = row.Failed,
            dropped = row.SpoolDropped,
            spoolDepth = dispatcher?.SpoolDepth(row.Id) ?? 0,
            lastSuccessUnixMs = row.LastSuccessUnixMs,
            lastAttemptUnixMs = row.LastAttemptUnixMs,
            lastError = row.LastError,
            updatedUnixMs = row.UpdatedUnixMs
        };
    }

    private static string? Validate(HttpPushWrite? body)
    {
        if (body is null || string.IsNullOrWhiteSpace(body.Name))
        {
            return "请填写名称";
        }

        if (!Uri.TryCreate(body.Url?.Trim(), UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            return "URL 需要是 http 或 https";
        }

        var method = (body.Method ?? "POST").Trim();
        if (!method.Equals("POST", StringComparison.OrdinalIgnoreCase) && !method.Equals("PUT", StringComparison.OrdinalIgnoreCase))
        {
            return "方法只能是 POST 或 PUT";
        }

        if (!AuthKinds.Contains((body.AuthKind ?? "none").Trim()))
        {
            return "认证只能是 none、basic、bearer 或 hmac-sha256";
        }

        return null;
    }

    private static string ResolveSecret(HttpPushWrite body, HttpPushTargetRow? existing)
    {
        if (body.ClearSecret)
        {
            return "";
        }

        if (string.IsNullOrEmpty(body.Secret) || body.Secret == "***")
        {
            return existing?.AuthSecret ?? "";
        }

        return body.Secret;
    }

    private static List<HeaderPair> MergeHeaders(List<HeaderPair>? incoming, HttpPushTargetRow? existing)
    {
        var previous = HttpPushDispatcher.ReadHeaders(existing?.HeadersJson);
        var merged = new List<HeaderPair>();
        foreach (var header in incoming ?? [])
        {
            if (string.IsNullOrWhiteSpace(header.Name))
            {
                continue;
            }

            var value = header.Value ?? "";
            if (value.Length == 0 || value == "***")
            {
                var old = previous.FirstOrDefault(item => item.Name.Equals(header.Name, StringComparison.OrdinalIgnoreCase));
                value = old?.Value ?? "";
            }

            merged.Add(new HeaderPair { Name = header.Name.Trim(), Value = value });
        }

        return merged;
    }

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

public sealed class HttpPushWrite
{
    public string? Id { get; set; }

    public string? Name { get; set; }

    public bool Enabled { get; set; } = true;

    public string? Url { get; set; }

    public string? Method { get; set; }

    public List<HeaderPair>? Headers { get; set; }

    public string? AuthKind { get; set; }

    public string? AuthUser { get; set; }

    public string? Secret { get; set; }

    public bool ClearSecret { get; set; }

    public string? SignatureHeader { get; set; }

    public bool SendValues { get; set; } = true;

    public string? ValueMode { get; set; }

    public int PeriodicSeconds { get; set; } = 30;

    public bool SendStatus { get; set; } = true;

    public bool SendAlarms { get; set; } = true;

    public int BatchMax { get; set; } = 50;

    public int BatchIntervalMs { get; set; } = 1000;

    public int TimeoutMs { get; set; } = 8000;

    public int MaxRetries { get; set; } = 8;

    public int BackoffInitialMs { get; set; } = 1000;

    public int BackoffMaxMs { get; set; } = 60_000;
}
