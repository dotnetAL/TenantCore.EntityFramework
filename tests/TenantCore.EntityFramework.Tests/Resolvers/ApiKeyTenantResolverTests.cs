using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Moq;
using TenantCore.EntityFramework.ControlDb;
using TenantCore.EntityFramework.Resolvers;
using Xunit;

namespace TenantCore.EntityFramework.Tests.Resolvers;

public class ApiKeyTenantResolverTests
{
    [Fact]
    public async Task ResolveTenantAsync_WithValidApiKey_ShouldReturnTenantId()
    {
        // Arrange
        var tenantId = Guid.NewGuid();
        var apiKey = "valid-api-key-123";
        var apiKeyHash = ApiKeyHasher.ComputeHash(apiKey);

        var tenant = CreateTenantRecord(tenantId, "test-tenant", apiKeyHash);

        var tenantStoreMock = new Mock<ITenantStore>();
        tenantStoreMock.Setup(s => s.GetTenantByApiKeyHashAsync(apiKeyHash, It.IsAny<CancellationToken>()))
            .ReturnsAsync(tenant);

        var httpContext = new DefaultHttpContext();
        httpContext.Request.Headers["X-Api-Key"] = apiKey;

        var accessor = new Mock<IHttpContextAccessor>();
        accessor.Setup(x => x.HttpContext).Returns(httpContext);

        var resolver = new ApiKeyTenantResolver<Guid>(accessor.Object, tenantStoreMock.Object);

        // Act
        var result = await resolver.ResolveTenantAsync();

        // Assert
        result.Should().Be(tenantId);
    }

    [Fact]
    public async Task ResolveTenantAsync_WithInactiveStatus_ShouldReturnDefault()
    {
        // Arrange
        var tenantId = Guid.NewGuid();
        var apiKey = "valid-api-key-123";
        var apiKeyHash = ApiKeyHasher.ComputeHash(apiKey);

        var tenant = CreateTenantRecord(tenantId, "test-tenant", apiKeyHash, TenantStatus.Suspended);

        var tenantStoreMock = new Mock<ITenantStore>();
        tenantStoreMock.Setup(s => s.GetTenantByApiKeyHashAsync(apiKeyHash, It.IsAny<CancellationToken>()))
            .ReturnsAsync(tenant);

        var httpContext = new DefaultHttpContext();
        httpContext.Request.Headers["X-Api-Key"] = apiKey;

        var accessor = new Mock<IHttpContextAccessor>();
        accessor.Setup(x => x.HttpContext).Returns(httpContext);

        var resolver = new ApiKeyTenantResolver<Guid>(accessor.Object, tenantStoreMock.Object);

        // Act
        var result = await resolver.ResolveTenantAsync();

        // Assert
        result.Should().Be(Guid.Empty);
    }

    [Fact]
    public async Task ResolveTenantAsync_WithMissingHeader_ShouldReturnDefault()
    {
        // Arrange
        var tenantStoreMock = new Mock<ITenantStore>();

        var httpContext = new DefaultHttpContext();

        var accessor = new Mock<IHttpContextAccessor>();
        accessor.Setup(x => x.HttpContext).Returns(httpContext);

        var resolver = new ApiKeyTenantResolver<Guid>(accessor.Object, tenantStoreMock.Object);

        // Act
        var result = await resolver.ResolveTenantAsync();

        // Assert
        result.Should().Be(Guid.Empty);
        tenantStoreMock.Verify(s => s.GetTenantByApiKeyHashAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ResolveTenantAsync_WithEmptyHeader_ShouldReturnDefault()
    {
        // Arrange
        var tenantStoreMock = new Mock<ITenantStore>();

        var httpContext = new DefaultHttpContext();
        httpContext.Request.Headers["X-Api-Key"] = "";

        var accessor = new Mock<IHttpContextAccessor>();
        accessor.Setup(x => x.HttpContext).Returns(httpContext);

        var resolver = new ApiKeyTenantResolver<Guid>(accessor.Object, tenantStoreMock.Object);

        // Act
        var result = await resolver.ResolveTenantAsync();

        // Assert
        result.Should().Be(Guid.Empty);
    }

    [Fact]
    public async Task ResolveTenantAsync_WithUnknownApiKey_ShouldReturnDefault()
    {
        // Arrange
        var apiKey = "unknown-api-key";
        var apiKeyHash = ApiKeyHasher.ComputeHash(apiKey);

        var tenantStoreMock = new Mock<ITenantStore>();
        tenantStoreMock.Setup(s => s.GetTenantByApiKeyHashAsync(apiKeyHash, It.IsAny<CancellationToken>()))
            .ReturnsAsync((TenantRecord?)null);

        var httpContext = new DefaultHttpContext();
        httpContext.Request.Headers["X-Api-Key"] = apiKey;

        var accessor = new Mock<IHttpContextAccessor>();
        accessor.Setup(x => x.HttpContext).Returns(httpContext);

        var resolver = new ApiKeyTenantResolver<Guid>(accessor.Object, tenantStoreMock.Object);

        // Act
        var result = await resolver.ResolveTenantAsync();

        // Assert
        result.Should().Be(Guid.Empty);
    }

    [Fact]
    public async Task ResolveTenantAsync_WithNoTenantStore_ShouldReturnDefault()
    {
        // Arrange
        var httpContext = new DefaultHttpContext();
        httpContext.Request.Headers["X-Api-Key"] = "some-api-key";

        var accessor = new Mock<IHttpContextAccessor>();
        accessor.Setup(x => x.HttpContext).Returns(httpContext);

        var resolver = new ApiKeyTenantResolver<Guid>(accessor.Object, null);

        // Act
        var result = await resolver.ResolveTenantAsync();

        // Assert
        result.Should().Be(Guid.Empty);
    }

    [Fact]
    public async Task ResolveTenantAsync_WithNoHttpContext_ShouldReturnDefault()
    {
        // Arrange
        var tenantStoreMock = new Mock<ITenantStore>();

        var accessor = new Mock<IHttpContextAccessor>();
        accessor.Setup(x => x.HttpContext).Returns(default(HttpContext));

        var resolver = new ApiKeyTenantResolver<Guid>(accessor.Object, tenantStoreMock.Object);

        // Act
        var result = await resolver.ResolveTenantAsync();

        // Assert
        result.Should().Be(Guid.Empty);
    }

    [Fact]
    public async Task ResolveTenantAsync_WithCustomHeaderName_ShouldUseCustomHeader()
    {
        // Arrange
        var tenantId = Guid.NewGuid();
        var apiKey = "custom-api-key";
        var apiKeyHash = ApiKeyHasher.ComputeHash(apiKey);

        var tenant = CreateTenantRecord(tenantId, "test-tenant", apiKeyHash);

        var tenantStoreMock = new Mock<ITenantStore>();
        tenantStoreMock.Setup(s => s.GetTenantByApiKeyHashAsync(apiKeyHash, It.IsAny<CancellationToken>()))
            .ReturnsAsync(tenant);

        var httpContext = new DefaultHttpContext();
        httpContext.Request.Headers["Custom-Api-Key-Header"] = apiKey;

        var accessor = new Mock<IHttpContextAccessor>();
        accessor.Setup(x => x.HttpContext).Returns(httpContext);

        var resolver = new ApiKeyTenantResolver<Guid>(accessor.Object, tenantStoreMock.Object, "Custom-Api-Key-Header");

        // Act
        var result = await resolver.ResolveTenantAsync();

        // Assert
        result.Should().Be(tenantId);
    }

    [Fact]
    public async Task ResolveTenantAsync_WithStringTKey_ShouldReturnStringTenantId()
    {
        // Arrange
        var tenantId = Guid.NewGuid();
        var apiKey = "valid-api-key";
        var apiKeyHash = ApiKeyHasher.ComputeHash(apiKey);

        var tenant = CreateTenantRecord(tenantId, "test-tenant", apiKeyHash);

        var tenantStoreMock = new Mock<ITenantStore>();
        tenantStoreMock.Setup(s => s.GetTenantByApiKeyHashAsync(apiKeyHash, It.IsAny<CancellationToken>()))
            .ReturnsAsync(tenant);

        var httpContext = new DefaultHttpContext();
        httpContext.Request.Headers["X-Api-Key"] = apiKey;

        var accessor = new Mock<IHttpContextAccessor>();
        accessor.Setup(x => x.HttpContext).Returns(httpContext);

        var resolver = new ApiKeyTenantResolver<string>(accessor.Object, tenantStoreMock.Object);

        // Act
        var result = await resolver.ResolveTenantAsync();

        // Assert
        result.Should().Be(tenantId.ToString());
    }

    [Fact]
    public void Priority_ShouldBeDefault175()
    {
        // Arrange
        var accessor = new Mock<IHttpContextAccessor>();
        var resolver = new ApiKeyTenantResolver<Guid>(accessor.Object, null);

        // Assert
        resolver.Priority.Should().Be(175);
    }

    [Fact]
    public void Priority_ShouldBeConfigurable()
    {
        // Arrange
        var accessor = new Mock<IHttpContextAccessor>();
        var resolver = new ApiKeyTenantResolver<Guid>(accessor.Object, null) { Priority = 200 };

        // Assert
        resolver.Priority.Should().Be(200);
    }

    private static TenantRecord CreateTenantRecord(
        Guid tenantId,
        string slug,
        string? apiKeyHash = null,
        TenantStatus status = TenantStatus.Active)
    {
        var now = DateTime.UtcNow;
        return new TenantRecord(
            tenantId,
            slug,
            status,
            $"tenant_{slug.Replace("-", "_")}",
            null, null, null,
            now, now);
    }
}
