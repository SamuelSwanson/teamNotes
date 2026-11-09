using System.Security.Cryptography;

namespace SharedNotes.Api.Identity;

/// <summary>PBKDF2-SHA256 password hashing. Stored format: v1.{iterations}.{saltBase64}.{hashBase64}.</summary>
public static class PasswordHasher
{
    private const int Iterations = 600_000;   // OWASP guidance for PBKDF2-HMAC-SHA256
    private const int SaltSize = 16;
    private const int KeySize = 32;

    // Verified against when the email is unknown so that response time does not reveal which emails exist.
    private static readonly Lazy<string> DummyHash = new(() => Hash(Guid.NewGuid().ToString()));

    public static string Hash(string password)
    {
        var salt = RandomNumberGenerator.GetBytes(SaltSize);
        var key = Rfc2898DeriveBytes.Pbkdf2(password, salt, Iterations, HashAlgorithmName.SHA256, KeySize);
        return $"v1.{Iterations}.{Convert.ToBase64String(salt)}.{Convert.ToBase64String(key)}";
    }

    public static bool Verify(string password, string? stored)
    {
        var parts = (stored ?? DummyHash.Value).Split('.');
        if (parts.Length != 4 || parts[0] != "v1" || !int.TryParse(parts[1], out var iterations))
            return false;
        var salt = Convert.FromBase64String(parts[2]);
        var expected = Convert.FromBase64String(parts[3]);
        var actual = Rfc2898DeriveBytes.Pbkdf2(password, salt, iterations, HashAlgorithmName.SHA256, expected.Length);
        return CryptographicOperations.FixedTimeEquals(actual, expected) && stored is not null;
    }
}
