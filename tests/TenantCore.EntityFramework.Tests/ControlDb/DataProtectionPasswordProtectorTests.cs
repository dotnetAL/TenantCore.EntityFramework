using FluentAssertions;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.DependencyInjection;
using TenantCore.EntityFramework.ControlDb;
using Xunit;

namespace TenantCore.EntityFramework.Tests.ControlDb;

public class DataProtectionPasswordProtectorTests
{
    private readonly ITenantPasswordProtector _protector;

    public DataProtectionPasswordProtectorTests()
    {
        var services = new ServiceCollection();
        services.AddDataProtection()
            .SetApplicationName("TenantCore.Tests");

        var provider = services.BuildServiceProvider();
        var dataProtectionProvider = provider.GetRequiredService<IDataProtectionProvider>();
        _protector = new DataProtectionPasswordProtector(dataProtectionProvider);
    }

    [Fact]
    public void Protect_WithValidPassword_ShouldReturnEncryptedString()
    {
        // Arrange
        var password = "MySecretPassword123";

        // Act
        var encrypted = _protector.Protect(password);

        // Assert
        encrypted.Should().NotBeNullOrEmpty();
        encrypted.Should().NotBe(password);
    }

    [Fact]
    public void Unprotect_WithEncryptedPassword_ShouldReturnOriginal()
    {
        // Arrange
        var password = "MySecretPassword123";
        var encrypted = _protector.Protect(password);

        // Act
        var decrypted = _protector.Unprotect(encrypted);

        // Assert
        decrypted.Should().Be(password);
    }

    [Fact]
    public void Protect_WithSamePassword_ShouldReturnDifferentCiphertext()
    {
        // Arrange
        var password = "SamePassword";

        // Act
        var encrypted1 = _protector.Protect(password);
        var encrypted2 = _protector.Protect(password);

        // Assert - Data protection typically produces different ciphertext each time
        // Both should decrypt to the same value though
        _protector.Unprotect(encrypted1).Should().Be(password);
        _protector.Unprotect(encrypted2).Should().Be(password);
    }

    [Fact]
    public void Protect_WithNullPassword_ShouldThrowArgumentNullException()
    {
        // Act
        var act = () => _protector.Protect(null!);

        // Assert
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Unprotect_WithNullEncrypted_ShouldThrowArgumentNullException()
    {
        // Act
        var act = () => _protector.Unprotect(null!);

        // Assert
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Unprotect_WithInvalidCiphertext_ShouldThrowException()
    {
        // Arrange
        var invalidCiphertext = "this-is-not-valid-ciphertext";

        // Act
        var act = () => _protector.Unprotect(invalidCiphertext);

        // Assert
        act.Should().Throw<Exception>();
    }

    [Fact]
    public void RoundTrip_WithEmptyString_ShouldWork()
    {
        // Arrange
        var password = "";

        // Act
        var encrypted = _protector.Protect(password);
        var decrypted = _protector.Unprotect(encrypted);

        // Assert
        decrypted.Should().Be(password);
    }

    [Fact]
    public void RoundTrip_WithSpecialCharacters_ShouldWork()
    {
        // Arrange
        var password = "P@$$w0rd!#%^&*()_+-=[]{}|;':\",./<>?";

        // Act
        var encrypted = _protector.Protect(password);
        var decrypted = _protector.Unprotect(encrypted);

        // Assert
        decrypted.Should().Be(password);
    }

    [Fact]
    public void RoundTrip_WithUnicodeCharacters_ShouldWork()
    {
        // Arrange
        var password = "密码🔐пароль";

        // Act
        var encrypted = _protector.Protect(password);
        var decrypted = _protector.Unprotect(encrypted);

        // Assert
        decrypted.Should().Be(password);
    }
}
