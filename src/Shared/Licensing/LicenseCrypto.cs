using System.Security.Cryptography;

namespace IotDaq.Licensing;

public static class LicenseCrypto
{
    public static ECDsa CreatePublic(string? spkiBase64 = null)
    {
        var text = string.IsNullOrWhiteSpace(spkiBase64) ? ProductKeys.SpkiBase64 : spkiBase64.Trim();
        var key = ECDsa.Create();
        key.ImportSubjectPublicKeyInfo(Convert.FromBase64String(text), out _);
        return key;
    }

    public static ECDsa CreatePrivate(string pemOrBase64)
    {
        var text = pemOrBase64.Trim();
        var key = ECDsa.Create();
        if (text.Contains("BEGIN", StringComparison.Ordinal))
        {
            key.ImportFromPem(text);
        }
        else
        {
            key.ImportPkcs8PrivateKey(Convert.FromBase64String(text), out _);
        }

        return key;
    }

    public static (string PublicSpki, string PrivatePem) Generate()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var publicSpki = Convert.ToBase64String(key.ExportSubjectPublicKeyInfo());
        var privatePem = key.ExportPkcs8PrivateKeyPem();
        return (publicSpki, privatePem);
    }
}
