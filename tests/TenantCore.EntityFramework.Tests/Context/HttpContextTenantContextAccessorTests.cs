using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Moq;
using TenantCore.EntityFramework.Abstractions;
using TenantCore.EntityFramework.Context;
using Xunit;

namespace TenantCore.EntityFramework.Tests.Context;

public class HttpContextTenantContextAccessorTests
{
    [Fact]
    public void TenantContext_WhenHttpContextAvailable_ShouldUseHttpContextItems()
    {
        // Arrange
        var httpContext = new DefaultHttpContext();
        var httpContextAccessor = new Mock<IHttpContextAccessor>();
        httpContextAccessor.Setup(x => x.HttpContext).Returns(httpContext);

        var accessor = new HttpContextTenantContextAccessor<string>(httpContextAccessor.Object);
        var tenantContext = new TenantContext<string>("tenant1", "tenant_tenant1");

        // Act
        accessor.SetTenantContext(tenantContext);

        // Assert
        accessor.TenantContext.Should().NotBeNull();
        accessor.TenantContext!.TenantId.Should().Be("tenant1");
        accessor.TenantContext.SchemaName.Should().Be("tenant_tenant1");

        // Verify it was stored in HttpContext.Items
        httpContext.Items.Should().ContainKey("TenantCore.TenantContext");
    }

    [Fact]
    public void TenantContext_WhenHttpContextNotAvailable_ShouldUseAsyncLocalFallback()
    {
        // Arrange
        var httpContextAccessor = new Mock<IHttpContextAccessor>();
        httpContextAccessor.Setup(x => x.HttpContext).Returns((HttpContext?)null);

        var accessor = new HttpContextTenantContextAccessor<string>(httpContextAccessor.Object);
        var tenantContext = new TenantContext<string>("tenant1", "tenant_tenant1");

        // Act
        accessor.SetTenantContext(tenantContext);

        // Assert
        accessor.TenantContext.Should().NotBeNull();
        accessor.TenantContext!.TenantId.Should().Be("tenant1");
    }

    [Fact]
    public void TenantContext_WhenNotSet_ShouldReturnNull()
    {
        // Arrange
        var httpContext = new DefaultHttpContext();
        var httpContextAccessor = new Mock<IHttpContextAccessor>();
        httpContextAccessor.Setup(x => x.HttpContext).Returns(httpContext);

        var accessor = new HttpContextTenantContextAccessor<string>(httpContextAccessor.Object);

        // Act
        var context = accessor.TenantContext;

        // Assert
        context.Should().BeNull();
    }

    [Fact]
    public void SetTenantContext_WithNull_ShouldClearContext()
    {
        // Arrange
        var httpContext = new DefaultHttpContext();
        var httpContextAccessor = new Mock<IHttpContextAccessor>();
        httpContextAccessor.Setup(x => x.HttpContext).Returns(httpContext);

        var accessor = new HttpContextTenantContextAccessor<string>(httpContextAccessor.Object);
        accessor.SetTenantContext(new TenantContext<string>("tenant1"));

        // Act
        accessor.SetTenantContext(null);

        // Assert
        accessor.TenantContext.Should().BeNull();
        httpContext.Items.Should().NotContainKey("TenantCore.TenantContext");
    }

    [Fact]
    public void TenantContext_ShouldPersistAcrossMultipleReads()
    {
        // Arrange
        var httpContext = new DefaultHttpContext();
        var httpContextAccessor = new Mock<IHttpContextAccessor>();
        httpContextAccessor.Setup(x => x.HttpContext).Returns(httpContext);

        var accessor = new HttpContextTenantContextAccessor<string>(httpContextAccessor.Object);
        var tenantContext = new TenantContext<string>("tenant1", "schema1");

        // Act
        accessor.SetTenantContext(tenantContext);

        // Assert - read multiple times
        accessor.TenantContext!.TenantId.Should().Be("tenant1");
        accessor.TenantContext!.TenantId.Should().Be("tenant1");
        accessor.TenantContext!.SchemaName.Should().Be("schema1");
    }

    [Fact]
    public void TenantContext_ShouldAllowOverwrite()
    {
        // Arrange
        var httpContext = new DefaultHttpContext();
        var httpContextAccessor = new Mock<IHttpContextAccessor>();
        httpContextAccessor.Setup(x => x.HttpContext).Returns(httpContext);

        var accessor = new HttpContextTenantContextAccessor<string>(httpContextAccessor.Object);

        // Act
        accessor.SetTenantContext(new TenantContext<string>("tenant1", "schema1"));
        accessor.SetTenantContext(new TenantContext<string>("tenant2", "schema2"));

        // Assert
        accessor.TenantContext!.TenantId.Should().Be("tenant2");
        accessor.TenantContext!.SchemaName.Should().Be("schema2");
    }

    [Fact]
    public void TenantContext_WithGuidKey_ShouldWork()
    {
        // Arrange
        var httpContext = new DefaultHttpContext();
        var httpContextAccessor = new Mock<IHttpContextAccessor>();
        httpContextAccessor.Setup(x => x.HttpContext).Returns(httpContext);

        var accessor = new HttpContextTenantContextAccessor<Guid>(httpContextAccessor.Object);
        var tenantId = Guid.NewGuid();

        // Act
        accessor.SetTenantContext(new TenantContext<Guid>(tenantId, $"tenant_{tenantId:N}"));

        // Assert
        accessor.TenantContext.Should().NotBeNull();
        accessor.TenantContext!.TenantId.Should().Be(tenantId);
    }

    [Fact]
    public void TenantContext_WithIntKey_ShouldWork()
    {
        // Arrange
        var httpContext = new DefaultHttpContext();
        var httpContextAccessor = new Mock<IHttpContextAccessor>();
        httpContextAccessor.Setup(x => x.HttpContext).Returns(httpContext);

        var accessor = new HttpContextTenantContextAccessor<int>(httpContextAccessor.Object);

        // Act
        accessor.SetTenantContext(new TenantContext<int>(123, "tenant_123"));

        // Assert
        accessor.TenantContext.Should().NotBeNull();
        accessor.TenantContext!.TenantId.Should().Be(123);
    }

    [Fact]
    public async Task TenantContext_WithHttpContext_ShouldNotRelyOnAsyncLocal()
    {
        // Arrange
        var httpContext = new DefaultHttpContext();
        var httpContextAccessor = new Mock<IHttpContextAccessor>();
        httpContextAccessor.Setup(x => x.HttpContext).Returns(httpContext);

        var accessor = new HttpContextTenantContextAccessor<string>(httpContextAccessor.Object);
        accessor.SetTenantContext(new TenantContext<string>("tenant1", "schema1"));

        // Act - read from a different task (simulating endpoint handler)
        var result = await Task.Run(() =>
        {
            // In real ASP.NET Core, HttpContext would still be available here
            // because the mock returns the same context
            return accessor.TenantContext?.TenantId;
        });

        // Assert
        result.Should().Be("tenant1");
    }

    [Fact]
    public void DifferentHttpContexts_ShouldHaveIsolatedTenantContexts()
    {
        // Arrange
        var httpContext1 = new DefaultHttpContext();
        var httpContext2 = new DefaultHttpContext();

        var httpContextAccessor1 = new Mock<IHttpContextAccessor>();
        httpContextAccessor1.Setup(x => x.HttpContext).Returns(httpContext1);

        var httpContextAccessor2 = new Mock<IHttpContextAccessor>();
        httpContextAccessor2.Setup(x => x.HttpContext).Returns(httpContext2);

        var accessor1 = new HttpContextTenantContextAccessor<string>(httpContextAccessor1.Object);
        var accessor2 = new HttpContextTenantContextAccessor<string>(httpContextAccessor2.Object);

        // Act
        accessor1.SetTenantContext(new TenantContext<string>("tenant1", "schema1"));
        accessor2.SetTenantContext(new TenantContext<string>("tenant2", "schema2"));

        // Assert - each should see its own tenant
        accessor1.TenantContext!.TenantId.Should().Be("tenant1");
        accessor2.TenantContext!.TenantId.Should().Be("tenant2");
    }

    [Fact]
    public void TenantContext_WhenHttpContextBecomesNull_ShouldFallbackToAsyncLocal()
    {
        // Arrange - start with HttpContext available
        var httpContext = new DefaultHttpContext();
        var httpContextAccessor = new Mock<IHttpContextAccessor>();
        httpContextAccessor.Setup(x => x.HttpContext).Returns(httpContext);

        var accessor = new HttpContextTenantContextAccessor<string>(httpContextAccessor.Object);
        accessor.SetTenantContext(new TenantContext<string>("tenant1", "schema1"));

        // Act - simulate HttpContext becoming unavailable (e.g., background processing)
        httpContextAccessor.Setup(x => x.HttpContext).Returns((HttpContext?)null);

        // Assert - should fallback to AsyncLocal which was also set
        accessor.TenantContext.Should().NotBeNull();
        accessor.TenantContext!.TenantId.Should().Be("tenant1");
    }
}
