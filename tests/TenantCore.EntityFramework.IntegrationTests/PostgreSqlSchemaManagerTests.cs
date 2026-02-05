using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using TenantCore.EntityFramework.PostgreSql;
using Xunit;

namespace TenantCore.EntityFramework.IntegrationTests;

[Collection("PostgreSql")]
public class PostgreSqlSchemaManagerTests
{
    private readonly PostgreSqlFixture _fixture;

    public PostgreSqlSchemaManagerTests(PostgreSqlFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task CreateSchemaAsync_ShouldCreateSchema()
    {
        // Arrange
        var schemaManager = new PostgreSqlSchemaManager(NullLogger<PostgreSqlSchemaManager>.Instance);
        var schemaName = $"test_create_{Guid.NewGuid():N}"[..20];

        var options = new DbContextOptionsBuilder<TestDbContext>()
            .UseNpgsql(_fixture.ConnectionString)
            .Options;

        await using var context = new TestDbContext(options);

        try
        {
            // Act
            await schemaManager.CreateSchemaAsync(context, schemaName);

            // Assert
            var exists = await schemaManager.SchemaExistsAsync(context, schemaName);
            exists.Should().BeTrue();
        }
        finally
        {
            // Cleanup
            await schemaManager.DropSchemaAsync(context, schemaName, cascade: true);
        }
    }

    [Fact]
    public async Task DropSchemaAsync_ShouldDropSchema()
    {
        // Arrange
        var schemaManager = new PostgreSqlSchemaManager(NullLogger<PostgreSqlSchemaManager>.Instance);
        var schemaName = $"test_drop_{Guid.NewGuid():N}"[..20];

        var options = new DbContextOptionsBuilder<TestDbContext>()
            .UseNpgsql(_fixture.ConnectionString)
            .Options;

        await using var context = new TestDbContext(options);
        await schemaManager.CreateSchemaAsync(context, schemaName);

        // Act
        await schemaManager.DropSchemaAsync(context, schemaName, cascade: true);

        // Assert
        var exists = await schemaManager.SchemaExistsAsync(context, schemaName);
        exists.Should().BeFalse();
    }

    [Fact]
    public async Task SchemaExistsAsync_WithExistingSchema_ShouldReturnTrue()
    {
        // Arrange
        var schemaManager = new PostgreSqlSchemaManager(NullLogger<PostgreSqlSchemaManager>.Instance);

        var options = new DbContextOptionsBuilder<TestDbContext>()
            .UseNpgsql(_fixture.ConnectionString)
            .Options;

        await using var context = new TestDbContext(options);

        // Act
        var exists = await schemaManager.SchemaExistsAsync(context, "public");

        // Assert
        exists.Should().BeTrue();
    }

    [Fact]
    public async Task SchemaExistsAsync_WithNonExistingSchema_ShouldReturnFalse()
    {
        // Arrange
        var schemaManager = new PostgreSqlSchemaManager(NullLogger<PostgreSqlSchemaManager>.Instance);

        var options = new DbContextOptionsBuilder<TestDbContext>()
            .UseNpgsql(_fixture.ConnectionString)
            .Options;

        await using var context = new TestDbContext(options);

        // Act
        var exists = await schemaManager.SchemaExistsAsync(context, "nonexistent_schema_xyz");

        // Assert
        exists.Should().BeFalse();
    }

    [Fact]
    public async Task GetSchemasAsync_ShouldReturnMatchingSchemas()
    {
        // Arrange
        var schemaManager = new PostgreSqlSchemaManager(NullLogger<PostgreSqlSchemaManager>.Instance);
        var prefix = $"test_list_{Guid.NewGuid():N}"[..15];
        var schema1 = $"{prefix}_1";
        var schema2 = $"{prefix}_2";

        var options = new DbContextOptionsBuilder<TestDbContext>()
            .UseNpgsql(_fixture.ConnectionString)
            .Options;

        await using var context = new TestDbContext(options);

        try
        {
            await schemaManager.CreateSchemaAsync(context, schema1);
            await schemaManager.CreateSchemaAsync(context, schema2);

            // Act
            var schemas = await schemaManager.GetSchemasAsync(context, prefix);

            // Assert
            schemas.Should().Contain(schema1);
            schemas.Should().Contain(schema2);
        }
        finally
        {
            // Cleanup
            await schemaManager.DropSchemaAsync(context, schema1, cascade: true);
            await schemaManager.DropSchemaAsync(context, schema2, cascade: true);
        }
    }

    [Fact]
    public async Task RenameSchemaAsync_ShouldRenameSchema()
    {
        // Arrange
        var schemaManager = new PostgreSqlSchemaManager(NullLogger<PostgreSqlSchemaManager>.Instance);
        var oldName = $"test_old_{Guid.NewGuid():N}"[..20];
        var newName = $"test_new_{Guid.NewGuid():N}"[..20];

        var options = new DbContextOptionsBuilder<TestDbContext>()
            .UseNpgsql(_fixture.ConnectionString)
            .Options;

        await using var context = new TestDbContext(options);

        try
        {
            await schemaManager.CreateSchemaAsync(context, oldName);

            // Act
            await schemaManager.RenameSchemaAsync(context, oldName, newName);

            // Assert
            var oldExists = await schemaManager.SchemaExistsAsync(context, oldName);
            var newExists = await schemaManager.SchemaExistsAsync(context, newName);

            oldExists.Should().BeFalse();
            newExists.Should().BeTrue();
        }
        finally
        {
            // Cleanup
            await schemaManager.DropSchemaAsync(context, newName, cascade: true);
        }
    }

    [Fact]
    public async Task CreateSchemaAsync_WithInvalidName_ShouldThrow()
    {
        // Arrange
        var schemaManager = new PostgreSqlSchemaManager(NullLogger<PostgreSqlSchemaManager>.Instance);

        var options = new DbContextOptionsBuilder<TestDbContext>()
            .UseNpgsql(_fixture.ConnectionString)
            .Options;

        await using var context = new TestDbContext(options);

        // Act
        var act = () => schemaManager.CreateSchemaAsync(context, "invalid-name-with-dashes");

        // Assert
        await act.Should().ThrowAsync<ArgumentException>();
    }
}
