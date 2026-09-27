using System.Security.Cryptography;
using System.Text;

namespace Gateway.Abstractions.Security;

/// <summary>
/// API keys are high-entropy and stored as SHA-256. Passwords use PBKDF2-SHA256.
/// Neither form is reversible.
/// </summary>
public static class SecretHash
{
    private const int Iterations = 100_000;

    public static string Sha256Hex(string secret)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(secret));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    public static bool Sha256Equals(string secret, string storedHex)
    {
        var actual = SHA256.HashData(Encoding.UTF8.GetBytes(secret));
        byte[] expected;
        try
        {
            expected = Convert.FromHexString(storedHex);
        }
        catch (FormatException)
        {
            return false;
        }

        return CryptographicOperations.FixedTimeEquals(actual, expected);
    }

    public static string Pbkdf2(string secret)
    {
        var salt = RandomNumberGenerator.GetBytes(16);
        var hash = Rfc2898DeriveBytes.Pbkdf2(secret, salt, Iterations, HashAlgorithmName.SHA256, 32);
        return $"pbkdf2-sha256${Iterations.ToString(System.Globalization.CultureInfo.InvariantCulture)}${Convert.ToBase64String(salt)}${Convert.ToBase64String(hash)}";
    }

    public static bool VerifyPbkdf2(string secret, string stored)
    {
        var parts = stored.Split('$');
        if (parts.Length != 4 || !string.Equals(parts[0], "pbkdf2-sha256", StringComparison.Ordinal))
        {
            return false;
        }

        if (!int.TryParse(parts[1], System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var iterations)
            || iterations < 1)
        {
            return false;
        }

        byte[] salt;
        byte[] expected;
        try
        {
            salt = Convert.FromBase64String(parts[2]);
            expected = Convert.FromBase64String(parts[3]);
        }
        catch (FormatException)
        {
            return false;
        }

        var actual = Rfc2898DeriveBytes.Pbkdf2(secret, salt, iterations, HashAlgorithmName.SHA256, expected.Length);
        return CryptographicOperations.FixedTimeEquals(actual, expected);
    }
}
