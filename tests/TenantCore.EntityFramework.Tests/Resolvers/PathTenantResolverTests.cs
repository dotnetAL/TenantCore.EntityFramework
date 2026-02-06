using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Moq;
using TenantCore.EntityFramework.Resolvers;
using Xunit;

namespace TenantCore.EntityFramework.Tests.Resolvers;

public class PathTenantResolverTests
{
    [Fact]
    public async Task ResolveTenantAsync_WithTenantAtFirstSegment_ShouldReturnTenantId()
    {
        // Arrange
        var httpContext = new DefaultHttpContext();
        httpContext.Request.Path = "/tenant1/api/products";

        var accessor = new Mock<IHttpContextAccessor>();
        accessor.Setup(x => x.HttpContext).Returns(httpContext);

        var resolver = new PathTenantResolver<string>(accessor.Object, segmentIndex: 0);

        // Act
        var tenantId = await resolver.ResolveTenantAsync();

        // Assert
        tenantId.Should().Be("tenant1");
    }

    [Fact]
    public async Task ResolveTenantAsync_WithTenantAtSecondSegment_ShouldReturnTenantId()
    {
        // Arrange
        var httpContext = new DefaultHttpContext();
        httpContext.Request.Path = "/api/tenant1/products";

        var accessor = new Mock<IHttpContextAccessor>();
        accessor.Setup(x => x.HttpContext).Returns(httpContext);

        var resolver = new PathTenantResolver<string>(accessor.Object, segmentIndex: 1);

        // Act
        var tenantId = await resolver.ResolveTenantAsync();

        // Assert
        tenantId.Should().Be("tenant1");
    }

    [Fact]
    public async Task ResolveTenantAsync_WithTenantAtThirdSegment_ShouldReturnTenantId()
    {
        // Arrange
        var httpContext = new DefaultHttpContext();
        httpContext.Request.Path = "/api/v1/tenant1/products";

        var accessor = new Mock<IHttpContextAccessor>();
        accessor.Setup(x => x.HttpContext).Returns(httpContext);

        var resolver = new PathTenantResolver<string>(accessor.Object, segmentIndex: 2);

        // Act
        var tenantId = await resolver.ResolveTenantAsync();

        // Assert
        tenantId.Should().Be("tenant1");
    }

    [Fact]
    public async Task ResolveTenantAsync_WithSegmentIndexOutOfRange_ShouldReturnNull()
    {
        // Arrange
        var httpContext = new DefaultHttpContext();
        httpContext.Request.Path = "/tenant1";

        var accessor = new Mock<IHttpContextAccessor>();
        accessor.Setup(x => x.HttpContext).Returns(httpContext);

        var resolver = new PathTenantResolver<string>(accessor.Object, segmentIndex: 5);

        // Act
        var tenantId = await resolver.ResolveTenantAsync();

        // Assert
        tenantId.Should().BeNull();
    }

    [Fact]
    public async Task ResolveTenantAsync_WithEmptyPath_ShouldReturnNull()
    {
        // Arrange
        var httpContext = new DefaultHttpContext();
        httpContext.Request.Path = "";

        var accessor = new Mock<IHttpContextAccessor>();
        accessor.Setup(x => x.HttpContext).Returns(httpContext);

        var resolver = new PathTenantResolver<string>(accessor.Object);

        // Act
        var tenantId = await resolver.ResolveTenantAsync();

        // Assert
        tenantId.Should().BeNull();
    }

    [Fact]
    public async Task ResolveTenantAsync_WithNoHttpContext_ShouldReturnNull()
    {
        // Arrange
        var accessor = new Mock<IHttpContextAccessor>();
        accessor.Setup(x => x.HttpContext).Returns(default(HttpContext));

        var resolver = new PathTenantResolver<string>(accessor.Object);

        // Act
        var tenantId = await resolver.ResolveTenantAsync();

        // Assert
        tenantId.Should().BeNull();
    }

    [Fact]
    public async Task ResolveTenantAsync_WithPathPrefix_ShouldReturnTenantAfterPrefix()
    {
        // Arrange
        var httpContext = new DefaultHttpContext();
        httpContext.Request.Path = "/api/tenant1/products";

        var accessor = new Mock<IHttpContextAccessor>();
        accessor.Setup(x => x.HttpContext).Returns(httpContext);

        var resolver = new PathTenantResolver<string>(accessor.Object, pathPrefix: "/api");

        // Act
        var tenantId = await resolver.ResolveTenantAsync();

        // Assert
        tenantId.Should().Be("tenant1");
    }

    [Fact]
    public async Task ResolveTenantAsync_WithMultiSegmentPathPrefix_ShouldReturnTenantAfterPrefix()
    {
        // Arrange
        var httpContext = new DefaultHttpContext();
        httpContext.Request.Path = "/api/v1/tenants/tenant1/orders";

        var accessor = new Mock<IHttpContextAccessor>();
        accessor.Setup(x => x.HttpContext).Returns(httpContext);

        var resolver = new PathTenantResolver<string>(accessor.Object, pathPrefix: "/api/v1/tenants");

        // Act
        var tenantId = await resolver.ResolveTenantAsync();

        // Assert
        tenantId.Should().Be("tenant1");
    }

    [Fact]
    public async Task ResolveTenantAsync_WithPathNotMatchingPrefix_ShouldReturnNull()
    {
        // Arrange
        var httpContext = new DefaultHttpContext();
        httpContext.Request.Path = "/other/tenant1/products";

        var accessor = new Mock<IHttpContextAccessor>();
        accessor.Setup(x => x.HttpContext).Returns(httpContext);

        var resolver = new PathTenantResolver<string>(accessor.Object, pathPrefix: "/api");

        // Act
        var tenantId = await resolver.ResolveTenantAsync();

        // Assert
        tenantId.Should().BeNull();
    }

    [Fact]
    public async Task ResolveTenantAsync_WithPrefixAtEndOfPath_ShouldReturnNull()
    {
        // Arrange
        var httpContext = new DefaultHttpContext();
        httpContext.Request.Path = "/api";

        var accessor = new Mock<IHttpContextAccessor>();
        accessor.Setup(x => x.HttpContext).Returns(httpContext);

        var resolver = new PathTenantResolver<string>(accessor.Object, pathPrefix: "/api");

        // Act
        var tenantId = await resolver.ResolveTenantAsync();

        // Assert
        tenantId.Should().BeNull();
    }

    [Fact]
    public async Task ResolveTenantAsync_WithPrefixWithTrailingSlash_ShouldWork()
    {
        // Arrange
        var httpContext = new DefaultHttpContext();
        httpContext.Request.Path = "/api/tenant1/products";

        var accessor = new Mock<IHttpContextAccessor>();
        accessor.Setup(x => x.HttpContext).Returns(httpContext);

        var resolver = new PathTenantResolver<string>(accessor.Object, pathPrefix: "/api/");

        // Act
        var tenantId = await resolver.ResolveTenantAsync();

        // Assert
        tenantId.Should().Be("tenant1");
    }

    [Fact]
    public async Task ResolveTenantAsync_WithGuidTenantId_ShouldParse()
    {
        // Arrange
        var expectedGuid = Guid.NewGuid();
        var httpContext = new DefaultHttpContext();
        httpContext.Request.Path = $"/api/{expectedGuid}/products";

        var accessor = new Mock<IHttpContextAccessor>();
        accessor.Setup(x => x.HttpContext).Returns(httpContext);

        var resolver = new PathTenantResolver<Guid>(accessor.Object, segmentIndex: 1);

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
        httpContext.Request.Path = "/api/not-a-guid/products";

        var accessor = new Mock<IHttpContextAccessor>();
        accessor.Setup(x => x.HttpContext).Returns(httpContext);

        var resolver = new PathTenantResolver<Guid>(accessor.Object, segmentIndex: 1);

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
        httpContext.Request.Path = "/api/123/products";

        var accessor = new Mock<IHttpContextAccessor>();
        accessor.Setup(x => x.HttpContext).Returns(httpContext);

        var resolver = new PathTenantResolver<int>(accessor.Object, segmentIndex: 1);

        // Act
        var tenantId = await resolver.ResolveTenantAsync();

        // Assert
        tenantId.Should().Be(123);
    }

    [Fact]
    public async Task ResolveTenantAsync_WithCustomParser_ShouldUseParser()
    {
        // Arrange
        var httpContext = new DefaultHttpContext();
        httpContext.Request.Path = "/api/PREFIX_tenant1/products";

        var accessor = new Mock<IHttpContextAccessor>();
        accessor.Setup(x => x.HttpContext).Returns(httpContext);

        var resolver = new PathTenantResolver<string>(
            accessor.Object,
            segmentIndex: 1,
            parser: value => value.Replace("PREFIX_", ""));

        // Act
        var tenantId = await resolver.ResolveTenantAsync();

        // Assert
        tenantId.Should().Be("tenant1");
    }

    [Fact]
    public async Task ResolveTenantAsync_DefaultConstructor_ShouldUseFirstSegment()
    {
        // Arrange
        var httpContext = new DefaultHttpContext();
        httpContext.Request.Path = "/tenant1/api/products";

        var accessor = new Mock<IHttpContextAccessor>();
        accessor.Setup(x => x.HttpContext).Returns(httpContext);

        var resolver = new PathTenantResolver<string>(accessor.Object);

        // Act
        var tenantId = await resolver.ResolveTenantAsync();

        // Assert
        tenantId.Should().Be("tenant1");
    }

    [Fact]
    public async Task ResolveTenantAsync_WithCaseInsensitivePrefix_ShouldMatch()
    {
        // Arrange
        var httpContext = new DefaultHttpContext();
        httpContext.Request.Path = "/API/tenant1/products";

        var accessor = new Mock<IHttpContextAccessor>();
        accessor.Setup(x => x.HttpContext).Returns(httpContext);

        var resolver = new PathTenantResolver<string>(accessor.Object, pathPrefix: "/api");

        // Act
        var tenantId = await resolver.ResolveTenantAsync();

        // Assert
        tenantId.Should().Be("tenant1");
    }

    [Fact]
    public void Priority_ShouldBeDefaultValue()
    {
        // Arrange
        var accessor = new Mock<IHttpContextAccessor>();
        var resolver = new PathTenantResolver<string>(accessor.Object);

        // Assert
        resolver.Priority.Should().Be(125);
    }

    [Fact]
    public void Priority_ShouldBeConfigurable()
    {
        // Arrange
        var accessor = new Mock<IHttpContextAccessor>();
        var resolver = new PathTenantResolver<string>(accessor.Object) { Priority = 200 };

        // Assert
        resolver.Priority.Should().Be(200);
    }
}
