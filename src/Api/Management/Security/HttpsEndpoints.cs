using Studio.Host.Config;
using Studio.Host.Endpoints;

namespace Studio.Host.Security;

public static class HttpsEndpoints
{
    public static void Map(RouteGroupBuilder api)
    {
        api.MapGet("/ops/https", (ConfigStore store) =>
            ApiResults.Ok(HttpsCertificates.Describe(HttpsBindingFile.Load(store.DataDirectory))));

        api.MapPost("/ops/https/self-signed", (HttpsWrite? body, ConfigStore store, HttpContext http) =>
        {
            try
            {
                var binding = HttpsCertificates.CreateSelfSigned(
                    store.DataDirectory,
                    body?.DnsNames ?? ["localhost"],
                    body?.IpAddresses ?? ["127.0.0.1"],
                    body?.HttpPort ?? 5080,
                    body?.HttpsPort ?? 5443,
                    body?.RedirectHttp ?? false,
                    body?.ListenAny ?? false);
                ConfigAudit.Write(http, store.Database, "https.self-signed", binding.Thumbprint, "生成自签证书，重启后生效");
                return ApiResults.Ok(HttpsCertificates.Describe(binding));
            }
            catch (Exception ex) when (ex is InvalidOperationException or System.Security.Cryptography.CryptographicException)
            {
                return ApiResults.Error(StatusCodes.Status400BadRequest, "https_invalid", ex.Message);
            }
        });

        api.MapPost("/ops/https/import", async (HttpContext http, ConfigStore store, CancellationToken cancellationToken) =>
        {
            if (!http.Request.HasFormContentType)
            {
                return ApiResults.Error(StatusCodes.Status400BadRequest, "https_invalid", "请用表单上传 PFX，或同时上传 certificate 与 key 两个 PEM 文件。");
            }

            var form = await http.Request.ReadFormAsync(cancellationToken);
            var httpPort = int.TryParse(form["httpPort"], out var hp) ? hp : 5080;
            var httpsPort = int.TryParse(form["httpsPort"], out var sp) ? sp : 5443;
            var redirect = form["redirectHttp"] == "true" || form["redirectHttp"] == "1";
            var listenAny = form["listenAny"] == "true" || form["listenAny"] == "1";
            try
            {
                var certificate = form.Files.GetFile("certificate") ?? form.Files.GetFile("pfx") ?? form.Files.FirstOrDefault();
                var key = form.Files.GetFile("key");
                HttpsBinding binding;
                if (key is not null && certificate is not null)
                {
                    using var certReader = new StreamReader(certificate.OpenReadStream());
                    using var keyReader = new StreamReader(key.OpenReadStream());
                    binding = HttpsCertificates.ImportPem(store.DataDirectory, await certReader.ReadToEndAsync(cancellationToken), await keyReader.ReadToEndAsync(cancellationToken), httpPort, httpsPort, redirect, listenAny);
                }
                else if (certificate is not null)
                {
                    using var buffer = new MemoryStream();
                    await certificate.CopyToAsync(buffer, cancellationToken);
                    binding = HttpsCertificates.ImportPfx(store.DataDirectory, buffer.ToArray(), form["password"], httpPort, httpsPort, redirect, listenAny);
                }
                else
                {
                    return ApiResults.Error(StatusCodes.Status400BadRequest, "https_invalid", "没有收到证书文件。");
                }

                ConfigAudit.Write(http, store.Database, "https.import", binding.Thumbprint, "导入证书，重启后生效");
                return ApiResults.Ok(HttpsCertificates.Describe(binding));
            }
            catch (Exception ex) when (ex is InvalidOperationException or System.Security.Cryptography.CryptographicException)
            {
                return ApiResults.Error(StatusCodes.Status400BadRequest, "https_invalid", ex.Message);
            }
        });

        api.MapPost("/ops/https/disable", (ConfigStore store, HttpContext http) =>
        {
            var binding = HttpsCertificates.Disable(store.DataDirectory);
            ConfigAudit.Write(http, store.Database, "https.disable", "https", "已关闭 HTTPS，重启后回到 HTTP");
            return ApiResults.Ok(HttpsCertificates.Describe(binding));
        });
    }
}

public sealed class HttpsWrite
{
    public List<string>? DnsNames { get; set; }

    public List<string>? IpAddresses { get; set; }

    public int HttpPort { get; set; } = 5080;

    public int HttpsPort { get; set; } = 5443;

    public bool RedirectHttp { get; set; }

    public bool ListenAny { get; set; }
}
