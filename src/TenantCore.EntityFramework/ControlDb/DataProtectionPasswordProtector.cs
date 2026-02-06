using Microsoft.AspNetCore.DataProtection;

namespace TenantCore.EntityFramework.ControlDb;

/// <summary>
/// Default implementation of <see cref="ITenantPasswordProtector"/> using ASP.NET Core Data Protection API.
/// </summary>
public class DataProtectionPasswordProtector : ITenantPasswordProtector
{
    private const string Purpose = "TenantCore.TenantDbPassword.v1";
    private readonly IDataProtector _protector;

    /// <summary>
    /// Initializes a new instance of the <see cref="DataProtectionPasswordProtector"/> class.
    /// </summary>
    /// <param name="dataProtectionProvider">The data protection provider.</param>
    public DataProtectionPasswordProtector(IDataProtectionProvider dataProtectionProvider)
    {
        _protector = dataProtectionProvider.CreateProtector(Purpose);
    }

    /// <inheritdoc />
    public string Protect(string plaintext)
    {
        ArgumentNullException.ThrowIfNull(plaintext);
        return _protector.Protect(plaintext);
    }

    /// <inheritdoc />
    public string Unprotect(string encrypted)
    {
        ArgumentNullException.ThrowIfNull(encrypted);
        return _protector.Unprotect(encrypted);
    }
}
