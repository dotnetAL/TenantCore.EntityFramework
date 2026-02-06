using System.Security.Cryptography;
using System.Text;

namespace TenantCore.EntityFramework.ControlDb;

/// <summary>
/// Utility class for computing SHA-256 hashes of API keys.
/// </summary>
public static class ApiKeyHasher
{
    /// <summary>
    /// Computes a SHA-256 hash of the given API key.
    /// </summary>
    /// <param name="apiKey">The API key to hash.</param>
    /// <returns>The SHA-256 hash as a lowercase hexadecimal string (64 characters).</returns>
    public static string ComputeHash(string apiKey)
    {
        ArgumentNullException.ThrowIfNull(apiKey);

        var bytes = Encoding.UTF8.GetBytes(apiKey);
        var hashBytes = SHA256.HashData(bytes);

        return Convert.ToHexString(hashBytes).ToLowerInvariant();
    }
}
