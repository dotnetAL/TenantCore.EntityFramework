using FluentAssertions;
using TenantCore.EntityFramework.Abstractions;
using TenantCore.EntityFramework.Context;
using Xunit;

namespace TenantCore.EntityFramework.Tests.Context;

public class TenantContextAccessorTests
{
    [Fact]
    public void TenantContext_WhenNotSet_ShouldReturnNull()
    {
        // Arrange
        var accessor = new TenantContextAccessor<string>();

        // Act
        var context = accessor.TenantContext;

        // Assert
        context.Should().BeNull();
    }

    [Fact]
    public void SetTenantContext_ShouldSetContext()
    {
        // Arrange
        var accessor = new TenantContextAccessor<string>();
        var tenantContext = new TenantContext<string>("tenant1", "tenant_tenant1");

        // Act
        accessor.SetTenantContext(tenantContext);

        // Assert
        accessor.TenantContext.Should().NotBeNull();
        accessor.TenantContext!.TenantId.Should().Be("tenant1");
        accessor.TenantContext.SchemaName.Should().Be("tenant_tenant1");
    }

    [Fact]
    public void SetTenantContext_WithNull_ShouldClearContext()
    {
        // Arrange
        var accessor = new TenantContextAccessor<string>();
        accessor.SetTenantContext(new TenantContext<string>("tenant1"));

        // Act
        accessor.SetTenantContext(null);

        // Assert
        accessor.TenantContext.Should().BeNull();
    }

    [Fact]
    public void TenantContext_IsValid_ShouldReturnTrueForValidTenant()
    {
        // Arrange
        var context = new TenantContext<string>("tenant1");

        // Act & Assert
        context.IsValid.Should().BeTrue();
    }

    [Fact]
    public void TenantContext_IsValid_ShouldReturnFalseForDefaultTenant()
    {
        // Arrange
        var context = new TenantContext<string>(null!);

        // Act & Assert
        context.IsValid.Should().BeFalse();
    }

    [Fact]
    public void TenantContext_Properties_ShouldBeAccessible()
    {
        // Arrange
        var context = new TenantContext<string>("tenant1");

        // Act
        context.Properties["key1"] = "value1";
        context.Properties["key2"] = 42;

        // Assert
        context.Properties.Should().ContainKey("key1");
        context.Properties["key1"].Should().Be("value1");
        context.Properties["key2"].Should().Be(42);
    }

    [Fact]
    public async Task TenantContext_ShouldFlowAcrossAsyncCalls()
    {
        // Arrange
        var accessor = new TenantContextAccessor<string>();
        var expectedTenant = "tenant1";
        accessor.SetTenantContext(new TenantContext<string>(expectedTenant));

        // Act
        var result = await Task.Run(() => accessor.TenantContext?.TenantId);

        // Assert
        result.Should().Be(expectedTenant);
    }

    [Fact]
    public void TenantContext_WithGuidKey_ShouldWork()
    {
        // Arrange
        var accessor = new TenantContextAccessor<Guid>();
        var tenantId = Guid.NewGuid();
        var tenantContext = new TenantContext<Guid>(tenantId);

        // Act
        accessor.SetTenantContext(tenantContext);

        // Assert
        accessor.TenantContext.Should().NotBeNull();
        accessor.TenantContext!.TenantId.Should().Be(tenantId);
    }

    [Fact]
    public void TenantContext_WithIntKey_ShouldWork()
    {
        // Arrange
        var accessor = new TenantContextAccessor<int>();
        var tenantContext = new TenantContext<int>(123);

        // Act
        accessor.SetTenantContext(tenantContext);

        // Assert
        accessor.TenantContext.Should().NotBeNull();
        accessor.TenantContext!.TenantId.Should().Be(123);
    }
}
