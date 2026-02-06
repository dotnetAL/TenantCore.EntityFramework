namespace TenantCore.EntityFramework.ControlDb;

/// <summary>
/// Interface for encrypting and decrypting tenant database passwords.
/// Implement this interface for custom encryption implementations.
/// </summary>
public interface ITenantPasswordProtector
{
    /// <summary>
    /// Encrypts a plaintext password.
    /// </summary>
    /// <param name="plaintext">The plaintext password to encrypt.</param>
    /// <returns>The encrypted password as a Base64 string.</returns>
    string Protect(string plaintext);

    /// <summary>
    /// Decrypts an encrypted password.
    /// </summary>
    /// <param name="encrypted">The encrypted password (Base64 string).</param>
    /// <returns>The decrypted plaintext password.</returns>
    string Unprotect(string encrypted);
}
