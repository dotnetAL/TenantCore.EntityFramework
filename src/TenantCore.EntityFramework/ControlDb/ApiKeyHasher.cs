using System.Security.Cryptography;

namespace TenantCore.EntityFramework.ControlDb;

/// <summary>
/// Utility class for securely hashing and verifying API keys using PBKDF2 with per-key salts.
/// </summary>
public static class ApiKeyHasher
{
    private const int SaltSize = 16; // 128 bits
    private const int HashSize = 32; // 256 bits
    private const int Iterations = 100_000; // OWASP recommended minimum for PBKDF2-SHA256
    private static readonly HashAlgorithmName Algorithm = HashAlgorithmName.SHA256;

    /// <summary>
    /// Computes a salted PBKDF2-SHA256 hash of the given API key.
    /// </summary>
    /// <param name="apiKey">The API key to hash.</param>
    /// <returns>
    /// A base64-encoded string containing the salt, iteration count, and hash.
    /// Format: {iterations}.{base64(salt)}.{base64(hash)}
    /// </returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="apiKey"/> is null.</exception>
    /// <exception cref="ArgumentException">Thrown when <paramref name="apiKey"/> is empty or whitespace.</exception>
    public static string ComputeHash(string apiKey)
    {
        ArgumentNullException.ThrowIfNull(apiKey);
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            throw new ArgumentException("API key cannot be empty or whitespace.", nameof(apiKey));
        }

        // Generate a cryptographically secure random salt
        var salt = RandomNumberGenerator.GetBytes(SaltSize);

        // Derive the key using PBKDF2
        var hash = Rfc2898DeriveBytes.Pbkdf2(
            apiKey,
            salt,
            Iterations,
            Algorithm,
            HashSize);

        // Format: iterations.salt.hash (all base64 encoded except iterations)
        return $"{Iterations}.{Convert.ToBase64String(salt)}.{Convert.ToBase64String(hash)}";
    }

    /// <summary>
    /// Verifies an API key against a stored hash.
    /// </summary>
    /// <param name="apiKey">The API key to verify.</param>
    /// <param name="storedHash">The stored hash to verify against.</param>
    /// <returns>True if the API key matches the stored hash; otherwise, false.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="apiKey"/> or <paramref name="storedHash"/> is null.</exception>
    public static bool VerifyHash(string apiKey, string storedHash)
    {
        ArgumentNullException.ThrowIfNull(apiKey);
        ArgumentNullException.ThrowIfNull(storedHash);

        if (string.IsNullOrWhiteSpace(apiKey))
        {
            return false;
        }

        try
        {
            // Parse the stored hash format: iterations.salt.hash
            var parts = storedHash.Split('.');
            if (parts.Length != 3)
            {
                return false;
            }

            if (!int.TryParse(parts[0], out var iterations) || iterations <= 0)
            {
                return false;
            }

            var salt = Convert.FromBase64String(parts[1]);
            var expectedHash = Convert.FromBase64String(parts[2]);

            // Derive the key using the same parameters
            var actualHash = Rfc2898DeriveBytes.Pbkdf2(
                apiKey,
                salt,
                iterations,
                Algorithm,
                expectedHash.Length);

            // Use constant-time comparison to prevent timing attacks
            return CryptographicOperations.FixedTimeEquals(actualHash, expectedHash);
        }
        catch (FormatException)
        {
            // Invalid base64 encoding
            return false;
        }
    }

    /// <summary>
    /// Checks if a stored hash needs to be rehashed due to outdated parameters.
    /// </summary>
    /// <param name="storedHash">The stored hash to check.</param>
    /// <returns>True if the hash should be rehashed with current parameters; otherwise, false.</returns>
    public static bool NeedsRehash(string storedHash)
    {
        if (string.IsNullOrEmpty(storedHash))
        {
            return true;
        }

        try
        {
            var parts = storedHash.Split('.');
            if (parts.Length != 3)
            {
                return true; // Invalid format, needs rehash
            }

            if (!int.TryParse(parts[0], out var iterations))
            {
                return true;
            }

            // Rehash if iterations are below current minimum
            return iterations < Iterations;
        }
        catch
        {
            return true;
        }
    }
}
