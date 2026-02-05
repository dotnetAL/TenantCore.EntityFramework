using FluentAssertions;
using TenantCore.EntityFramework.Configuration;
using Xunit;

namespace TenantCore.EntityFramework.Tests.Configuration;

public class SchemaPerTenantOptionsTests
{
    [Fact]
    public void GenerateSchemaName_WithDefaults_ShouldUsePrefix()
    {
        // Arrange
        var options = new SchemaPerTenantOptions();

        // Act
        var schemaName = options.GenerateSchemaName("tenant1");

        // Assert
        schemaName.Should().Be("tenant_tenant1");
    }

    [Fact]
    public void GenerateSchemaName_WithCustomPrefix_ShouldUseCustomPrefix()
    {
        // Arrange
        var options = new SchemaPerTenantOptions { SchemaPrefix = "org_" };

        // Act
        var schemaName = options.GenerateSchemaName("acme");

        // Assert
        schemaName.Should().Be("org_acme");
    }

    [Fact]
    public void GenerateSchemaName_WithUpperCase_ShouldConvertToLowercase()
    {
        // Arrange
        var options = new SchemaPerTenantOptions();

        // Act
        var schemaName = options.GenerateSchemaName("TENANT1");

        // Assert
        schemaName.Should().Be("tenant_tenant1");
    }

    [Fact]
    public void GenerateSchemaName_WithSpecialCharacters_ShouldSanitize()
    {
        // Arrange
        var options = new SchemaPerTenantOptions();

        // Act
        var schemaName = options.GenerateSchemaName("tenant-with-dashes");

        // Assert
        schemaName.Should().Be("tenant_tenant_with_dashes");
    }

    [Fact]
    public void GenerateSchemaName_WithCustomGenerator_ShouldUseCustomGenerator()
    {
        // Arrange
        var options = new SchemaPerTenantOptions
        {
            SchemaNameGenerator = id => $"custom_{id}"
        };

        // Act
        var schemaName = options.GenerateSchemaName("tenant1");

        // Assert
        schemaName.Should().Be("custom_tenant1");
    }

    [Fact]
    public void GenerateSchemaName_WithTooLongName_ShouldThrow()
    {
        // Arrange
        var options = new SchemaPerTenantOptions();
        var longTenantId = new string('a', 100);

        // Act
        var act = () => options.GenerateSchemaName(longTenantId);

        // Assert
        act.Should().Throw<ArgumentException>()
            .WithMessage("*exceeds maximum length*");
    }

    [Fact]
    public void GenerateSchemaName_WithReservedKeyword_ShouldThrow()
    {
        // Arrange
        var options = new SchemaPerTenantOptions
        {
            SchemaNameGenerator = _ => "public",
            ValidateSchemaNames = true
        };

        // Act
        var act = () => options.GenerateSchemaName("test");

        // Assert
        act.Should().Throw<ArgumentException>()
            .WithMessage("*reserved SQL keyword*");
    }

    [Fact]
    public void GenerateSchemaName_WithValidationDisabled_ShouldSkipValidation()
    {
        // Arrange
        var options = new SchemaPerTenantOptions
        {
            SchemaNameGenerator = _ => "public",
            ValidateSchemaNames = false
        };

        // Act
        var schemaName = options.GenerateSchemaName("test");

        // Assert
        schemaName.Should().Be("public");
    }

    [Fact]
    public void ExtractTenantId_WithPrefix_ShouldExtractCorrectly()
    {
        // Arrange
        var options = new SchemaPerTenantOptions { SchemaPrefix = "tenant_" };

        // Act
        var tenantId = options.ExtractTenantId("tenant_acme");

        // Assert
        tenantId.Should().Be("acme");
    }

    [Fact]
    public void ExtractTenantId_WithoutPrefix_ShouldReturnFull()
    {
        // Arrange
        var options = new SchemaPerTenantOptions { SchemaPrefix = "tenant_" };

        // Act
        var tenantId = options.ExtractTenantId("custom_schema");

        // Assert
        tenantId.Should().Be("custom_schema");
    }

    [Fact]
    public void GenerateSchemaName_WithGuidId_ShouldSanitize()
    {
        // Arrange
        var options = new SchemaPerTenantOptions();
        var guidId = Guid.NewGuid();

        // Act
        var schemaName = options.GenerateSchemaName(guidId);

        // Assert
        schemaName.Should().StartWith("tenant_");
        schemaName.Should().NotContain("-");
    }
}
