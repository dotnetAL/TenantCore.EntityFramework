using FluentAssertions;
using TenantCore.EntityFramework.ControlDb;
using TenantCore.EntityFramework.ControlDb.Entities;
using Xunit;

namespace TenantCore.EntityFramework.Tests.ControlDb;

public class TenantEntityTests
{
    [Fact]
    public void DefaultValues_ShouldBeCorrect()
    {
        // Act
        var entity = new TenantEntity();

        // Assert
        entity.TenantId.Should().Be(Guid.Empty);
        entity.TenantSlug.Should().BeEmpty();
        entity.Status.Should().Be(TenantStatus.Pending);
        entity.TenantSchema.Should().BeEmpty();
        entity.TenantDatabase.Should().BeNull();
        entity.TenantDbServer.Should().BeNull();
        entity.TenantDbUser.Should().BeNull();
        entity.TenantDbPasswordEncrypted.Should().BeNull();
        entity.TenantApiKeyHash.Should().BeNull();
    }

    [Fact]
    public void Properties_ShouldBeSettable()
    {
        // Arrange
        var entity = new TenantEntity();
        var tenantId = Guid.NewGuid();
        var now = DateTime.UtcNow;

        // Act
        entity.TenantId = tenantId;
        entity.TenantSlug = "test-tenant";
        entity.Status = TenantStatus.Active;
        entity.TenantSchema = "tenant_test";
        entity.TenantDatabase = "testdb";
        entity.TenantDbServer = "localhost";
        entity.TenantDbUser = "user";
        entity.TenantDbPasswordEncrypted = "encrypted";
        entity.TenantApiKeyHash = "hash123";
        entity.CreatedAt = now;
        entity.UpdatedAt = now;

        // Assert
        entity.TenantId.Should().Be(tenantId);
        entity.TenantSlug.Should().Be("test-tenant");
        entity.Status.Should().Be(TenantStatus.Active);
        entity.TenantSchema.Should().Be("tenant_test");
        entity.TenantDatabase.Should().Be("testdb");
        entity.TenantDbServer.Should().Be("localhost");
        entity.TenantDbUser.Should().Be("user");
        entity.TenantDbPasswordEncrypted.Should().Be("encrypted");
        entity.TenantApiKeyHash.Should().Be("hash123");
        entity.CreatedAt.Should().Be(now);
        entity.UpdatedAt.Should().Be(now);
    }

    [Fact]
    public void ToRecord_ShouldCreateCorrectRecord()
    {
        // Arrange
        var tenantId = Guid.NewGuid();
        var createdAt = DateTime.UtcNow.AddDays(-1);
        var updatedAt = DateTime.UtcNow;

        var entity = new TenantEntity
        {
            TenantId = tenantId,
            TenantSlug = "test-tenant",
            Status = TenantStatus.Active,
            TenantSchema = "tenant_test",
            TenantDatabase = "testdb",
            TenantDbServer = "localhost",
            TenantDbUser = "user",
            TenantDbPasswordEncrypted = "encrypted",
            TenantApiKeyHash = "hash",
            CreatedAt = createdAt,
            UpdatedAt = updatedAt
        };

        // Act
        var record = entity.ToRecord();

        // Assert
        record.TenantId.Should().Be(tenantId);
        record.TenantSlug.Should().Be("test-tenant");
        record.Status.Should().Be(TenantStatus.Active);
        record.TenantSchema.Should().Be("tenant_test");
        record.TenantDatabase.Should().Be("testdb");
        record.TenantDbServer.Should().Be("localhost");
        record.TenantDbUser.Should().Be("user");
        record.CreatedAt.Should().Be(createdAt);
        record.UpdatedAt.Should().Be(updatedAt);
    }

    [Fact]
    public void ToRecord_WithNullOptionalFields_ShouldWork()
    {
        // Arrange
        var entity = new TenantEntity
        {
            TenantId = Guid.NewGuid(),
            TenantSlug = "test",
            TenantSchema = "schema",
            TenantDatabase = null,
            TenantDbServer = null,
            TenantDbUser = null,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        // Act
        var record = entity.ToRecord();

        // Assert
        record.TenantDatabase.Should().BeNull();
        record.TenantDbServer.Should().BeNull();
        record.TenantDbUser.Should().BeNull();
    }
}
