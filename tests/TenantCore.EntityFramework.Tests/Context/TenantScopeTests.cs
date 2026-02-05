using FluentAssertions;
using Moq;
using TenantCore.EntityFramework.Abstractions;
using TenantCore.EntityFramework.Configuration;
using TenantCore.EntityFramework.Context;
using Xunit;

namespace TenantCore.EntityFramework.Tests.Context;

public class TenantScopeTests
{
    [Fact]
    public void TenantScope_ShouldSetTenantContext()
    {
        // Arrange
        var accessor = new TenantContextAccessor<string>();

        // Act
        using (var scope = new TenantScope<string>(accessor, "tenant1", "tenant_tenant1"))
        {
            // Assert
            accessor.TenantContext.Should().NotBeNull();
            accessor.TenantContext!.TenantId.Should().Be("tenant1");
            accessor.TenantContext.SchemaName.Should().Be("tenant_tenant1");
        }
    }

    [Fact]
    public void TenantScope_WhenDisposed_ShouldRestorePreviousContext()
    {
        // Arrange
        var accessor = new TenantContextAccessor<string>();
        var originalContext = new TenantContext<string>("original", "tenant_original");
        accessor.SetTenantContext(originalContext);

        // Act
        using (var scope = new TenantScope<string>(accessor, "temporary", "tenant_temporary"))
        {
            accessor.TenantContext!.TenantId.Should().Be("temporary");
        }

        // Assert
        accessor.TenantContext.Should().NotBeNull();
        accessor.TenantContext!.TenantId.Should().Be("original");
    }

    [Fact]
    public void TenantScope_WhenDisposed_ShouldRestoreNullContext()
    {
        // Arrange
        var accessor = new TenantContextAccessor<string>();

        // Act
        using (var scope = new TenantScope<string>(accessor, "tenant1"))
        {
            accessor.TenantContext.Should().NotBeNull();
        }

        // Assert
        accessor.TenantContext.Should().BeNull();
    }

    [Fact]
    public void TenantScope_NestedScopes_ShouldRestoreCorrectly()
    {
        // Arrange
        var accessor = new TenantContextAccessor<string>();

        // Act & Assert
        using (var scope1 = new TenantScope<string>(accessor, "tenant1"))
        {
            accessor.TenantContext!.TenantId.Should().Be("tenant1");

            using (var scope2 = new TenantScope<string>(accessor, "tenant2"))
            {
                accessor.TenantContext!.TenantId.Should().Be("tenant2");
            }

            accessor.TenantContext!.TenantId.Should().Be("tenant1");
        }

        accessor.TenantContext.Should().BeNull();
    }

    [Fact]
    public void TenantScopeFactory_CreateScope_ShouldGenerateSchemaName()
    {
        // Arrange
        var accessor = new TenantContextAccessor<string>();
        var options = new TenantCoreOptions();
        var factory = new TenantScopeFactory<string>(accessor, options);

        // Act
        using var scope = factory.CreateScope("tenant1");

        // Assert
        accessor.TenantContext.Should().NotBeNull();
        accessor.TenantContext!.TenantId.Should().Be("tenant1");
        accessor.TenantContext.SchemaName.Should().Be("tenant_tenant1");
    }

    [Fact]
    public void TenantScopeFactory_CreateScope_WithCustomPrefix_ShouldGenerateCorrectSchemaName()
    {
        // Arrange
        var accessor = new TenantContextAccessor<string>();
        var options = new TenantCoreOptions();
        options.SchemaPerTenant.SchemaPrefix = "org_";
        var factory = new TenantScopeFactory<string>(accessor, options);

        // Act
        using var scope = factory.CreateScope("acme");

        // Assert
        accessor.TenantContext!.SchemaName.Should().Be("org_acme");
    }

    [Fact]
    public void TenantScope_MultipleDisposes_ShouldBeSafe()
    {
        // Arrange
        var accessor = new TenantContextAccessor<string>();
        var scope = new TenantScope<string>(accessor, "tenant1");

        // Act
        scope.Dispose();
        var act = () => scope.Dispose();

        // Assert
        act.Should().NotThrow();
    }
}
