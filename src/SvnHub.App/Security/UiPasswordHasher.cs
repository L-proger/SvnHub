using System.Security.Cryptography;
using System.Text;

namespace SvnHub.App.Security;

public static class UiPasswordHasher
{
    private const int SaltSize = 16;
    private const int KeySize = 32;
    private const int Iterations = 310_000;
    private const string Algorithm = "SHA256";

    public static string Hash(string password)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(password);

        var salt = RandomNumberGenerator.GetBytes(SaltSize);
        var key = Pbkdf2(password, salt, Iterations, KeySize);

        return string.Join(
            "$",
            "pbkdf2",
            Algorithm,
            Iterations.ToString(),
            Convert.ToBase64String(salt),
            Convert.ToBase64String(key)
        );
    }

    public static bool Verify(string hash, string password)
    {
        if (string.IsNullOrWhiteSpace(hash) || string.IsNullOrEmpty(password))
        {
            return false;
        }

        var parts = hash.Split('$', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length != 5)
        {
            return false;
        }

        if (!string.Equals(parts[0], "pbkdf2", StringComparison.Ordinal))
        {
            return false;
        }

        if (!string.Equals(parts[1], Algorithm, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (!int.TryParse(parts[2], out var iterations) || iterations <= 0)
        {
            return false;
        }

        byte[] salt;
        byte[] expectedKey;
        try
        {
            salt = Convert.FromBase64String(parts[3]);
            expectedKey = Convert.FromBase64String(parts[4]);
        }
        catch (FormatException)
        {
            return false;
        }

        var actualKey = Pbkdf2(password, salt, iterations, expectedKey.Length);
        return CryptographicOperations.FixedTimeEquals(actualKey, expectedKey);
    }

    private static byte[] Pbkdf2(string password, byte[] salt, int iterations, int keySize)
    {
        using var deriveBytes = new Rfc2898DeriveBytes(
            password: Encoding.UTF8.GetBytes(password),
            salt: salt,
            iterations: iterations,
            hashAlgorithm: HashAlgorithmName.SHA256
        );
        return deriveBytes.GetBytes(keySize);
    }
}
