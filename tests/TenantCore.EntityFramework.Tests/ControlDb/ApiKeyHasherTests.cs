using FluentAssertions;
using TenantCore.EntityFramework.ControlDb;
using Xunit;

namespace TenantCore.EntityFramework.Tests.ControlDb;

public class ApiKeyHasherTests
{
    [Fact]
    public void ComputeHash_WithValidApiKey_ShouldReturnSha256Hash()
    {
        // Arrange
        var apiKey = "test-api-key-123";

        // Act
        var hash = ApiKeyHasher.ComputeHash(apiKey);

        // Assert
        hash.Should().NotBeNullOrEmpty();
        hash.Should().HaveLength(64); // SHA-256 produces 64 hex characters
        hash.Should().MatchRegex("^[0-9a-f]{64}$"); // lowercase hex
    }

    [Fact]
    public void ComputeHash_WithSameInput_ShouldReturnConsistentHash()
    {
        // Arrange
        var apiKey = "consistent-key";

        // Act
        var hash1 = ApiKeyHasher.ComputeHash(apiKey);
        var hash2 = ApiKeyHasher.ComputeHash(apiKey);

        // Assert
        hash1.Should().Be(hash2);
    }

    [Fact]
    public void ComputeHash_WithDifferentInputs_ShouldReturnDifferentHashes()
    {
        // Arrange
        var apiKey1 = "key-one";
        var apiKey2 = "key-two";

        // Act
        var hash1 = ApiKeyHasher.ComputeHash(apiKey1);
        var hash2 = ApiKeyHasher.ComputeHash(apiKey2);

        // Assert
        hash1.Should().NotBe(hash2);
    }

    [Fact]
    public void ComputeHash_ShouldReturnLowercaseHex()
    {
        // Arrange
        var apiKey = "ABC123";

        // Act
        var hash = ApiKeyHasher.ComputeHash(apiKey);

        // Assert
        hash.Should().Be(hash.ToLowerInvariant());
    }

    [Fact]
    public void ComputeHash_WithEmptyString_ShouldReturnValidHash()
    {
        // Arrange
        var apiKey = "";

        // Act
        var hash = ApiKeyHasher.ComputeHash(apiKey);

        // Assert
        hash.Should().HaveLength(64);
    }

    [Fact]
    public void ComputeHash_WithNullInput_ShouldThrowArgumentNullException()
    {
        // Act
        var act = () => ApiKeyHasher.ComputeHash(null!);

        // Assert
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void ComputeHash_WithKnownInput_ShouldReturnExpectedHash()
    {
        // Arrange - "hello" SHA-256 hash is well-known
        var apiKey = "hello";

        // Act
        var hash = ApiKeyHasher.ComputeHash(apiKey);

        // Assert - SHA-256 of "hello" in lowercase hex
        hash.Should().Be("2cf24dba5fb0a30e26e83b2ac5b9e29e1b161e5c1fa7425e73043362938b9824");
    }
}
