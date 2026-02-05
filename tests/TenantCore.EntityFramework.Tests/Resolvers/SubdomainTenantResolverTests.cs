using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Moq;
using TenantCore.EntityFramework.Resolvers;
using Xunit;

namespace TenantCore.EntityFramework.Tests.Resolvers;

public class SubdomainTenantResolverTests
{
    [Fact]
    public async Task ResolveTenantAsync_WithValidSubdomain_ShouldReturnTenantId()
    {
        // Arrange
        var httpContext = new DefaultHttpContext();
        httpContext.Request.Host = new HostString("tenant1.example.com");

        var accessor = new Mock<IHttpContextAccessor>();
        accessor.Setup(x => x.HttpContext).Returns(httpContext);

        var resolver = new SubdomainTenantResolver<string>(accessor.Object, "example.com");

        // Act
        var tenantId = await resolver.ResolveTenantAsync();

        // Assert
        tenantId.Should().Be("tenant1");
    }

    [Fact]
    public async Task ResolveTenantAsync_WithNoSubdomain_ShouldReturnNull()
    {
        // Arrange
        var httpContext = new DefaultHttpContext();
        httpContext.Request.Host = new HostString("example.com");

        var accessor = new Mock<IHttpContextAccessor>();
        accessor.Setup(x => x.HttpContext).Returns(httpContext);

        var resolver = new SubdomainTenantResolver<string>(accessor.Object, "example.com");

        // Act
        var tenantId = await resolver.ResolveTenantAsync();

        // Assert
        tenantId.Should().BeNull();
    }

    [Fact]
    public async Task ResolveTenantAsync_WithWwwSubdomain_ShouldReturnNull()
    {
        // Arrange
        var httpContext = new DefaultHttpContext();
        httpContext.Request.Host = new HostString("www.example.com");

        var accessor = new Mock<IHttpContextAccessor>();
        accessor.Setup(x => x.HttpContext).Returns(httpContext);

        var resolver = new SubdomainTenantResolver<string>(accessor.Object, "example.com");

        // Act
        var tenantId = await resolver.ResolveTenantAsync();

        // Assert
        tenantId.Should().BeNull();
    }

    [Fact]
    public async Task ResolveTenantAsync_WithApiSubdomain_ShouldReturnNull()
    {
        // Arrange
        var httpContext = new DefaultHttpContext();
        httpContext.Request.Host = new HostString("api.example.com");

        var accessor = new Mock<IHttpContextAccessor>();
        accessor.Setup(x => x.HttpContext).Returns(httpContext);

        var resolver = new SubdomainTenantResolver<string>(accessor.Object, "example.com");

        // Act
        var tenantId = await resolver.ResolveTenantAsync();

        // Assert
        tenantId.Should().BeNull();
    }

    [Fact]
    public async Task ResolveTenantAsync_WithMultiLevelSubdomain_ShouldReturnFirstPart()
    {
        // Arrange
        var httpContext = new DefaultHttpContext();
        httpContext.Request.Host = new HostString("tenant1.api.example.com");

        var accessor = new Mock<IHttpContextAccessor>();
        accessor.Setup(x => x.HttpContext).Returns(httpContext);

        var resolver = new SubdomainTenantResolver<string>(accessor.Object, "example.com");

        // Act
        var tenantId = await resolver.ResolveTenantAsync();

        // Assert
        tenantId.Should().Be("tenant1");
    }

    [Fact]
    public async Task ResolveTenantAsync_WithDifferentDomain_ShouldReturnNull()
    {
        // Arrange
        var httpContext = new DefaultHttpContext();
        httpContext.Request.Host = new HostString("tenant1.different.com");

        var accessor = new Mock<IHttpContextAccessor>();
        accessor.Setup(x => x.HttpContext).Returns(httpContext);

        var resolver = new SubdomainTenantResolver<string>(accessor.Object, "example.com");

        // Act
        var tenantId = await resolver.ResolveTenantAsync();

        // Assert
        tenantId.Should().BeNull();
    }

    [Fact]
    public async Task ResolveTenantAsync_WithBaseDomainWithDot_ShouldHandleCorrectly()
    {
        // Arrange
        var httpContext = new DefaultHttpContext();
        httpContext.Request.Host = new HostString("tenant1.example.com");

        var accessor = new Mock<IHttpContextAccessor>();
        accessor.Setup(x => x.HttpContext).Returns(httpContext);

        // Pass base domain with leading dot
        var resolver = new SubdomainTenantResolver<string>(accessor.Object, ".example.com");

        // Act
        var tenantId = await resolver.ResolveTenantAsync();

        // Assert
        tenantId.Should().Be("tenant1");
    }

    [Fact]
    public async Task ResolveTenantAsync_WithNoHttpContext_ShouldReturnNull()
    {
        // Arrange
        var accessor = new Mock<IHttpContextAccessor>();
        accessor.Setup(x => x.HttpContext).Returns(default(HttpContext));

        var resolver = new SubdomainTenantResolver<string>(accessor.Object, "example.com");

        // Act
        var tenantId = await resolver.ResolveTenantAsync();

        // Assert
        tenantId.Should().BeNull();
    }
}
