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
    /// <returns>The encrypted password string.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="plaintext"/> is null.</exception>
    string Protect(string plaintext);

    /// <summary>
    /// Decrypts an encrypted password.
    /// </summary>
    /// <param name="encrypted">The encrypted password string.</param>
    /// <returns>The decrypted plaintext password.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="encrypted"/> is null.</exception>
    string Unprotect(string encrypted);
}
