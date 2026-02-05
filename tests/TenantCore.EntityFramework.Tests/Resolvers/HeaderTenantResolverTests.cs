using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Moq;
using TenantCore.EntityFramework.Resolvers;
using Xunit;

namespace TenantCore.EntityFramework.Tests.Resolvers;

public class HeaderTenantResolverTests
{
    [Fact]
    public async Task ResolveTenantAsync_WithValidHeader_ShouldReturnTenantId()
    {
        // Arrange
        var httpContext = new DefaultHttpContext();
        httpContext.Request.Headers["X-Tenant-Id"] = "tenant1";

        var accessor = new Mock<IHttpContextAccessor>();
        accessor.Setup(x => x.HttpContext).Returns(httpContext);

        var resolver = new HeaderTenantResolver<string>(accessor.Object);

        // Act
        var tenantId = await resolver.ResolveTenantAsync();

        // Assert
        tenantId.Should().Be("tenant1");
    }

    [Fact]
    public async Task ResolveTenantAsync_WithMissingHeader_ShouldReturnNull()
    {
        // Arrange
        var httpContext = new DefaultHttpContext();

        var accessor = new Mock<IHttpContextAccessor>();
        accessor.Setup(x => x.HttpContext).Returns(httpContext);

        var resolver = new HeaderTenantResolver<string>(accessor.Object);

        // Act
        var tenantId = await resolver.ResolveTenantAsync();

        // Assert
        tenantId.Should().BeNull();
    }

    [Fact]
    public async Task ResolveTenantAsync_WithEmptyHeader_ShouldReturnNull()
    {
        // Arrange
        var httpContext = new DefaultHttpContext();
        httpContext.Request.Headers["X-Tenant-Id"] = "";

        var accessor = new Mock<IHttpContextAccessor>();
        accessor.Setup(x => x.HttpContext).Returns(httpContext);

        var resolver = new HeaderTenantResolver<string>(accessor.Object);

        // Act
        var tenantId = await resolver.ResolveTenantAsync();

        // Assert
        tenantId.Should().BeNull();
    }

    [Fact]
    public async Task ResolveTenantAsync_WithCustomHeaderName_ShouldUseCustomHeader()
    {
        // Arrange
        var httpContext = new DefaultHttpContext();
        httpContext.Request.Headers["Custom-Tenant"] = "custom-tenant1";

        var accessor = new Mock<IHttpContextAccessor>();
        accessor.Setup(x => x.HttpContext).Returns(httpContext);

        var resolver = new HeaderTenantResolver<string>(accessor.Object, "Custom-Tenant");

        // Act
        var tenantId = await resolver.ResolveTenantAsync();

        // Assert
        tenantId.Should().Be("custom-tenant1");
    }

    [Fact]
    public async Task ResolveTenantAsync_WithGuidTenantId_ShouldParse()
    {
        // Arrange
        var expectedGuid = Guid.NewGuid();
        var httpContext = new DefaultHttpContext();
        httpContext.Request.Headers["X-Tenant-Id"] = expectedGuid.ToString();

        var accessor = new Mock<IHttpContextAccessor>();
        accessor.Setup(x => x.HttpContext).Returns(httpContext);

        var resolver = new HeaderTenantResolver<Guid>(accessor.Object);

        // Act
        var tenantId = await resolver.ResolveTenantAsync();

        // Assert
        tenantId.Should().Be(expectedGuid);
    }

    [Fact]
    public async Task ResolveTenantAsync_WithInvalidGuid_ShouldReturnDefault()
    {
        // Arrange
        var httpContext = new DefaultHttpContext();
        httpContext.Request.Headers["X-Tenant-Id"] = "not-a-guid";

        var accessor = new Mock<IHttpContextAccessor>();
        accessor.Setup(x => x.HttpContext).Returns(httpContext);

        var resolver = new HeaderTenantResolver<Guid>(accessor.Object);

        // Act
        var tenantId = await resolver.ResolveTenantAsync();

        // Assert
        tenantId.Should().Be(Guid.Empty);
    }

    [Fact]
    public async Task ResolveTenantAsync_WithIntTenantId_ShouldParse()
    {
        // Arrange
        var httpContext = new DefaultHttpContext();
        httpContext.Request.Headers["X-Tenant-Id"] = "123";

        var accessor = new Mock<IHttpContextAccessor>();
        accessor.Setup(x => x.HttpContext).Returns(httpContext);

        var resolver = new HeaderTenantResolver<int>(accessor.Object);

        // Act
        var tenantId = await resolver.ResolveTenantAsync();

        // Assert
        tenantId.Should().Be(123);
    }

    [Fact]
    public async Task ResolveTenantAsync_WithNoHttpContext_ShouldReturnNull()
    {
        // Arrange
        var accessor = new Mock<IHttpContextAccessor>();
        accessor.Setup(x => x.HttpContext).Returns(default(HttpContext));

        var resolver = new HeaderTenantResolver<string>(accessor.Object);

        // Act
        var tenantId = await resolver.ResolveTenantAsync();

        // Assert
        tenantId.Should().BeNull();
    }

    [Fact]
    public async Task ResolveTenantAsync_WithCustomParser_ShouldUseParser()
    {
        // Arrange
        var httpContext = new DefaultHttpContext();
        httpContext.Request.Headers["X-Tenant-Id"] = "PREFIX_tenant1";

        var accessor = new Mock<IHttpContextAccessor>();
        accessor.Setup(x => x.HttpContext).Returns(httpContext);

        var resolver = new HeaderTenantResolver<string>(
            accessor.Object,
            "X-Tenant-Id",
            value => value.Replace("PREFIX_", ""));

        // Act
        var tenantId = await resolver.ResolveTenantAsync();

        // Assert
        tenantId.Should().Be("tenant1");
    }
}
