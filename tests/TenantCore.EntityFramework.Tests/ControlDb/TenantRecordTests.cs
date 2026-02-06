using FluentAssertions;
using TenantCore.EntityFramework.ControlDb;
using Xunit;

namespace TenantCore.EntityFramework.Tests.ControlDb;

public class TenantRecordTests
{
    [Fact]
    public void Constructor_ShouldSetAllProperties()
    {
        // Arrange
        var tenantId = Guid.NewGuid();
        var slug = "test-tenant";
        var status = TenantStatus.Active;
        var schema = "tenant_test";
        var database = "testdb";
        var server = "localhost";
        var user = "testuser";
        var createdAt = DateTime.UtcNow.AddDays(-1);
        var updatedAt = DateTime.UtcNow;

        // Act
        var record = new TenantRecord(
            tenantId, slug, status, schema, database, server, user, createdAt, updatedAt);

        // Assert
        record.TenantId.Should().Be(tenantId);
        record.TenantSlug.Should().Be(slug);
        record.Status.Should().Be(status);
        record.TenantSchema.Should().Be(schema);
        record.TenantDatabase.Should().Be(database);
        record.TenantDbServer.Should().Be(server);
        record.TenantDbUser.Should().Be(user);
        record.CreatedAt.Should().Be(createdAt);
        record.UpdatedAt.Should().Be(updatedAt);
    }

    [Fact]
    public void Constructor_WithNullOptionalFields_ShouldAllowNull()
    {
        // Arrange
        var tenantId = Guid.NewGuid();
        var createdAt = DateTime.UtcNow;

        // Act
        var record = new TenantRecord(
            tenantId, "slug", TenantStatus.Pending, "schema",
            null, null, null, createdAt, createdAt);

        // Assert
        record.TenantDatabase.Should().BeNull();
        record.TenantDbServer.Should().BeNull();
        record.TenantDbUser.Should().BeNull();
    }

    [Fact]
    public void TwoRecords_WithSameValues_ShouldBeEqual()
    {
        // Arrange
        var tenantId = Guid.NewGuid();
        var createdAt = DateTime.UtcNow;

        var record1 = new TenantRecord(
            tenantId, "slug", TenantStatus.Active, "schema",
            null, null, null, createdAt, createdAt);

        var record2 = new TenantRecord(
            tenantId, "slug", TenantStatus.Active, "schema",
            null, null, null, createdAt, createdAt);

        // Assert
        record1.Should().Be(record2);
    }

    [Fact]
    public void TwoRecords_WithDifferentIds_ShouldNotBeEqual()
    {
        // Arrange
        var createdAt = DateTime.UtcNow;

        var record1 = new TenantRecord(
            Guid.NewGuid(), "slug", TenantStatus.Active, "schema",
            null, null, null, createdAt, createdAt);

        var record2 = new TenantRecord(
            Guid.NewGuid(), "slug", TenantStatus.Active, "schema",
            null, null, null, createdAt, createdAt);

        // Assert
        record1.Should().NotBe(record2);
    }
}
