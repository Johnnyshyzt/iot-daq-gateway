using System.Net;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;

namespace Studio.Host.Security;

public sealed class HttpsBinding
{
    public string Mode { get; set; } = "off";

    public int HttpPort { get; set; } = 5080;

    public int HttpsPort { get; set; } = 5443;

    public bool RedirectHttp { get; set; }

    public bool ListenAny { get; set; }

    public string CertificatePath { get; set; } = "";

    public string PfxPassword { get; set; } = "";

    public List<string> DnsNames { get; set; } = [];

    public List<string> IpAddresses { get; set; } = [];

    public string Subject { get; set; } = "";

    public string Thumbprint { get; set; } = "";

    public string NotAfter { get; set; } = "";

    public bool Enabled => !string.Equals(Mode, "off", StringComparison.OrdinalIgnoreCase) && CertificatePath.Length > 0;
}

public static class HttpsBindingFile
{
    public static string PathFor(string dataDirectory) => Path.Combine(dataDirectory, "https", "binding.json");

    public static string CertificatePath(string dataDirectory) => Path.Combine(dataDirectory, "https", "gateway.pfx");

    public static HttpsBinding Load(string dataDirectory)
    {
        var path = PathFor(dataDirectory);
        if (!File.Exists(path))
        {
            return new HttpsBinding();
        }

        try
        {
            return JsonSerializer.Deserialize<HttpsBinding>(File.ReadAllText(path), Json) ?? new HttpsBinding();
        }
        catch (JsonException)
        {
            return new HttpsBinding();
        }
    }

    public static void Save(string dataDirectory, HttpsBinding binding)
    {
        var path = PathFor(dataDirectory);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporary = path + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(binding, Json));
        File.Move(temporary, path, overwrite: true);
    }

    public static X509Certificate2? TryLoadCertificate(HttpsBinding binding)
    {
        if (!binding.Enabled || !File.Exists(binding.CertificatePath))
        {
            return null;
        }

        try
        {
            return X509CertificateLoader.LoadPkcs12FromFile(binding.CertificatePath, binding.PfxPassword, X509KeyStorageFlags.Exportable);
        }
        catch (CryptographicException)
        {
            return null;
        }
    }

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true };
}

public static class HttpsCertificates
{
    public static HttpsBinding CreateSelfSigned(string dataDirectory, IReadOnlyList<string> dnsNames, IReadOnlyList<string> ipAddresses, int httpPort, int httpsPort, bool redirect, bool listenAny)
    {
        var dns = dnsNames.Where(item => !string.IsNullOrWhiteSpace(item)).Select(item => item.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var ips = new List<IPAddress>();
        foreach (var text in ipAddresses)
        {
            if (IPAddress.TryParse(text?.Trim(), out var ip))
            {
                ips.Add(ip);
            }
        }

        if (dns.Count == 0 && ips.Count == 0)
        {
            dns.Add("localhost");
            ips.Add(IPAddress.Loopback);
        }

        if (!dns.Contains("localhost", StringComparer.OrdinalIgnoreCase))
        {
            dns.Insert(0, "localhost");
        }

        using var rsa = RSA.Create(2048);
        var subject = "CN=" + dns[0];
        var request = new CertificateRequest(subject, rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        var san = new SubjectAlternativeNameBuilder();
        foreach (var name in dns)
        {
            san.AddDnsName(name);
        }

        foreach (var ip in ips)
        {
            san.AddIpAddress(ip);
        }

        request.CertificateExtensions.Add(san.Build());
        request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.KeyEncipherment, critical: false));
        request.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(new OidCollection { new Oid("1.3.6.1.5.5.7.3.1") }, critical: false));
        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, critical: false));
        var certificate = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddYears(2));
        return Store(dataDirectory, certificate, "self-signed", dns, ips.Select(ip => ip.ToString()).ToList(), httpPort, httpsPort, redirect, listenAny);
    }

    public static HttpsBinding ImportPfx(string dataDirectory, byte[] pfx, string? password, int httpPort, int httpsPort, bool redirect, bool listenAny)
    {
        var certificate = X509CertificateLoader.LoadPkcs12(pfx, password, X509KeyStorageFlags.Exportable);
        if (!certificate.HasPrivateKey)
        {
            throw new InvalidOperationException("PFX 里没有私钥，不能当作服务器证书。");
        }

        return Store(dataDirectory, certificate, "imported", Names(certificate), Ips(certificate), httpPort, httpsPort, redirect, listenAny);
    }

    public static HttpsBinding ImportPem(string dataDirectory, string certificatePem, string keyPem, int httpPort, int httpsPort, bool redirect, bool listenAny)
    {
        var certificate = X509Certificate2.CreateFromPem(certificatePem, keyPem);
        var exported = X509CertificateLoader.LoadPkcs12(certificate.Export(X509ContentType.Pfx), null, X509KeyStorageFlags.Exportable);
        return Store(dataDirectory, exported, "imported", Names(certificate), Ips(certificate), httpPort, httpsPort, redirect, listenAny);
    }

    public static HttpsBinding Disable(string dataDirectory)
    {
        var binding = HttpsBindingFile.Load(dataDirectory);
        binding.Mode = "off";
        binding.RedirectHttp = false;
        HttpsBindingFile.Save(dataDirectory, binding);
        return binding;
    }

    public static object Describe(HttpsBinding binding) => new
    {
        mode = binding.Mode,
        enabled = binding.Enabled,
        httpPort = binding.HttpPort,
        httpsPort = binding.HttpsPort,
        redirectHttp = binding.RedirectHttp,
        listenAny = binding.ListenAny,
        dnsNames = binding.DnsNames,
        ipAddresses = binding.IpAddresses,
        subject = binding.Subject,
        thumbprint = binding.Thumbprint,
        notAfter = binding.NotAfter,
        restartRequired = true,
        passwordStored = binding.PfxPassword.Length > 0,
        note = "默认仍是 HTTP。保存证书后要重启进程才会按 HTTPS 监听。Docker 需要同时发布 HTTPS 端口；systemd 和 Windows 服务重启后生效。"
    };

    private static HttpsBinding Store(
        string dataDirectory,
        X509Certificate2 certificate,
        string mode,
        List<string> dns,
        List<string> ips,
        int httpPort,
        int httpsPort,
        bool redirect,
        bool listenAny)
    {
        var password = Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant();
        var path = HttpsBindingFile.CertificatePath(dataDirectory);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, certificate.Export(X509ContentType.Pfx, password));
        var binding = new HttpsBinding
        {
            Mode = mode,
            HttpPort = Math.Clamp(httpPort, 1, 65535),
            HttpsPort = Math.Clamp(httpsPort, 1, 65535),
            RedirectHttp = redirect,
            ListenAny = listenAny,
            CertificatePath = path,
            PfxPassword = password,
            DnsNames = dns,
            IpAddresses = ips,
            Subject = certificate.Subject,
            Thumbprint = certificate.Thumbprint ?? "",
            NotAfter = certificate.NotAfter.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", System.Globalization.CultureInfo.InvariantCulture)
        };
        HttpsBindingFile.Save(dataDirectory, binding);
        return binding;
    }

    private static List<string> Names(X509Certificate2 certificate)
    {
        var names = new List<string>();
        foreach (var extension in certificate.Extensions.OfType<X509SubjectAlternativeNameExtension>())
        {
            names.AddRange(extension.EnumerateDnsNames());
        }

        return names;
    }

    private static List<string> Ips(X509Certificate2 certificate)
    {
        var names = new List<string>();
        foreach (var extension in certificate.Extensions.OfType<X509SubjectAlternativeNameExtension>())
        {
            names.AddRange(extension.EnumerateIPAddresses().Select(ip => ip.ToString()));
        }

        return names;
    }
}
