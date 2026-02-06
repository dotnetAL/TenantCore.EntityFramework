using FluentAssertions;
using TenantCore.EntityFramework.ControlDb;
using Xunit;

namespace TenantCore.EntityFramework.Tests.ControlDb;

public class ApiKeyHasherTests
{
    [Fact]
    public void ComputeHash_WithValidApiKey_ShouldReturnSaltedHash()
    {
        // Arrange
        var apiKey = "test-api-key-123";

        // Act
        var hash = ApiKeyHasher.ComputeHash(apiKey);

        // Assert
        hash.Should().NotBeNullOrEmpty();
        // Format: iterations.salt.hash
        var parts = hash.Split('.');
        parts.Should().HaveCount(3);
        parts[0].Should().Be("100000"); // Current iteration count
    }

    [Fact]
    public void ComputeHash_WithSameInput_ShouldReturnDifferentHashes_DueToRandomSalt()
    {
        // Arrange
        var apiKey = "consistent-key";

        // Act
        var hash1 = ApiKeyHasher.ComputeHash(apiKey);
        var hash2 = ApiKeyHasher.ComputeHash(apiKey);

        // Assert - Different salts should produce different hashes
        hash1.Should().NotBe(hash2);
    }

    [Fact]
    public void VerifyHash_WithCorrectApiKey_ShouldReturnTrue()
    {
        // Arrange
        var apiKey = "my-secret-key";
        var hash = ApiKeyHasher.ComputeHash(apiKey);

        // Act
        var result = ApiKeyHasher.VerifyHash(apiKey, hash);

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public void VerifyHash_WithIncorrectApiKey_ShouldReturnFalse()
    {
        // Arrange
        var apiKey = "correct-key";
        var wrongKey = "wrong-key";
        var hash = ApiKeyHasher.ComputeHash(apiKey);

        // Act
        var result = ApiKeyHasher.VerifyHash(wrongKey, hash);

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public void VerifyHash_WithDifferentInputs_ShouldDistinguishCorrectly()
    {
        // Arrange
        var apiKey1 = "key-one";
        var apiKey2 = "key-two";
        var hash1 = ApiKeyHasher.ComputeHash(apiKey1);
        var hash2 = ApiKeyHasher.ComputeHash(apiKey2);

        // Act & Assert
        ApiKeyHasher.VerifyHash(apiKey1, hash1).Should().BeTrue();
        ApiKeyHasher.VerifyHash(apiKey2, hash2).Should().BeTrue();
        ApiKeyHasher.VerifyHash(apiKey1, hash2).Should().BeFalse();
        ApiKeyHasher.VerifyHash(apiKey2, hash1).Should().BeFalse();
    }

    [Fact]
    public void ComputeHash_WithEmptyString_ShouldThrowArgumentException()
    {
        // Arrange
        var apiKey = "";

        // Act
        var act = () => ApiKeyHasher.ComputeHash(apiKey);

        // Assert
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void ComputeHash_WithWhitespaceOnly_ShouldThrowArgumentException()
    {
        // Arrange
        var apiKey = "   ";

        // Act
        var act = () => ApiKeyHasher.ComputeHash(apiKey);

        // Assert
        act.Should().Throw<ArgumentException>();
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
    public void VerifyHash_WithNullApiKey_ShouldThrowArgumentNullException()
    {
        // Arrange
        var hash = ApiKeyHasher.ComputeHash("test");

        // Act
        var act = () => ApiKeyHasher.VerifyHash(null!, hash);

        // Assert
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void VerifyHash_WithNullHash_ShouldThrowArgumentNullException()
    {
        // Act
        var act = () => ApiKeyHasher.VerifyHash("test", null!);

        // Assert
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void VerifyHash_WithEmptyApiKey_ShouldReturnFalse()
    {
        // Arrange
        var hash = ApiKeyHasher.ComputeHash("test");

        // Act
        var result = ApiKeyHasher.VerifyHash("", hash);

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public void VerifyHash_WithInvalidHashFormat_ShouldReturnFalse()
    {
        // Arrange
        var invalidHashes = new[]
        {
            "not-a-valid-hash",
            "100000.invalid",
            "100000.invalidbase64!.alsoinvalid",
            "notanumber.dGVzdA==.dGVzdA==",
            "-1.dGVzdA==.dGVzdA==",
            ""
        };

        // Act & Assert
        foreach (var invalidHash in invalidHashes)
        {
            ApiKeyHasher.VerifyHash("test", invalidHash).Should().BeFalse();
        }
    }

    [Fact]
    public void NeedsRehash_WithCurrentIterations_ShouldReturnFalse()
    {
        // Arrange
        var hash = ApiKeyHasher.ComputeHash("test-key");

        // Act
        var result = ApiKeyHasher.NeedsRehash(hash);

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public void NeedsRehash_WithLowerIterations_ShouldReturnTrue()
    {
        // Arrange - Simulate an old hash with lower iterations
        var oldHash = "10000.dGVzdHNhbHQ=.dGVzdGhhc2g=";

        // Act
        var result = ApiKeyHasher.NeedsRehash(oldHash);

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public void NeedsRehash_WithInvalidFormat_ShouldReturnTrue()
    {
        // Arrange
        var invalidHashes = new[]
        {
            "invalid",
            "",
            "not.enough",
            "notanumber.salt.hash"
        };

        // Act & Assert
        foreach (var invalidHash in invalidHashes)
        {
            ApiKeyHasher.NeedsRehash(invalidHash).Should().BeTrue();
        }
    }

    [Fact]
    public void NeedsRehash_WithNullOrEmpty_ShouldReturnTrue()
    {
        // Act & Assert
        ApiKeyHasher.NeedsRehash(null!).Should().BeTrue();
        ApiKeyHasher.NeedsRehash("").Should().BeTrue();
    }

    [Fact]
    public void ComputeHash_HashFormat_ShouldContainValidBase64Components()
    {
        // Arrange
        var apiKey = "test-key";

        // Act
        var hash = ApiKeyHasher.ComputeHash(apiKey);
        var parts = hash.Split('.');

        // Assert
        parts.Should().HaveCount(3);

        // Iterations should be a valid integer
        int.TryParse(parts[0], out var iterations).Should().BeTrue();
        iterations.Should().BeGreaterThan(0);

        // Salt and hash should be valid base64
        var salt = Convert.FromBase64String(parts[1]);
        var hashBytes = Convert.FromBase64String(parts[2]);

        salt.Should().HaveCount(16); // 128-bit salt
        hashBytes.Should().HaveCount(32); // 256-bit hash
    }

    [Fact]
    public void VerifyHash_ShouldBeCaseInsensitiveForApiKey()
    {
        // Arrange
        var apiKey = "CaseSensitiveKey";
        var hash = ApiKeyHasher.ComputeHash(apiKey);

        // Act & Assert - API keys should be case-sensitive
        ApiKeyHasher.VerifyHash(apiKey, hash).Should().BeTrue();
        ApiKeyHasher.VerifyHash(apiKey.ToLower(), hash).Should().BeFalse();
        ApiKeyHasher.VerifyHash(apiKey.ToUpper(), hash).Should().BeFalse();
    }

    [Fact]
    public void VerifyHash_WithUnicodeApiKey_ShouldWorkCorrectly()
    {
        // Arrange
        var apiKey = "test-key-\u00E9\u00E8\u00EA"; // Contains accented characters

        // Act
        var hash = ApiKeyHasher.ComputeHash(apiKey);
        var result = ApiKeyHasher.VerifyHash(apiKey, hash);

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public void VerifyHash_WithLongApiKey_ShouldWorkCorrectly()
    {
        // Arrange
        var apiKey = new string('x', 1000); // Very long API key

        // Act
        var hash = ApiKeyHasher.ComputeHash(apiKey);
        var result = ApiKeyHasher.VerifyHash(apiKey, hash);

        // Assert
        result.Should().BeTrue();
    }
}
