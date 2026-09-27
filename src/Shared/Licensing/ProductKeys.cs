namespace IotDaq.Licensing;

/// <summary>
/// ECDSA P-256 public key (SubjectPublicKeyInfo, base64) embedded in the Host.
/// It verifies license files and upgrade packages. The matching private key is
/// not in this repository; <c>tools/license-gen</c> reads it from a path you pass.
/// Replace this constant with your own public key before you issue production licenses.
/// </summary>
public static class ProductKeys
{
    public const string Algorithm = "ES256";

    public const string SpkiBase64 =
        "MFkwEwYHKoZIzj0CAQYIKoZIzj0DAQcDQgAEUDRGI8A1BSa9jPBmbQf2r/ZHwJkNlK1WUZE1kOlqnFgJ4e4YWXNOQzL+eFLYl3tCmSK+hmb/PQ+J8HWtbG27bA==";
}
