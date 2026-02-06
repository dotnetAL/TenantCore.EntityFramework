using FluentAssertions;
using Microsoft.Extensions.Caching.Memory;
using Moq;
using TenantCore.EntityFramework.Configuration;
using TenantCore.EntityFramework.ControlDb;
using Xunit;

namespace TenantCore.EntityFramework.Tests.ControlDb;

public class CachedTenantStoreTests
{
    private readonly Mock<ITenantStore> _innerStoreMock;
    private readonly IMemoryCache _cache;
    private readonly ControlDbOptions _options;
    private readonly CachedTenantStore _cachedStore;

    public CachedTenantStoreTests()
    {
        _innerStoreMock = new Mock<ITenantStore>();
        _cache = new MemoryCache(new MemoryCacheOptions());
        _options = new ControlDbOptions
        {
            EnableCaching = true,
            CacheDuration = TimeSpan.FromMinutes(5)
        };
        _cachedStore = new CachedTenantStore(_innerStoreMock.Object, _cache, _options);
    }

    [Fact]
    public async Task GetTenantAsync_FirstCall_ShouldCallInnerStore()
    {
        // Arrange
        var tenantId = Guid.NewGuid();
        var tenant = CreateTenantRecord(tenantId, "test-tenant");
        _innerStoreMock.Setup(s => s.GetTenantAsync(tenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(tenant);

        // Act
        var result = await _cachedStore.GetTenantAsync(tenantId);

        // Assert
        result.Should().Be(tenant);
        _innerStoreMock.Verify(s => s.GetTenantAsync(tenantId, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GetTenantAsync_SecondCall_ShouldReturnCachedResult()
    {
        // Arrange
        var tenantId = Guid.NewGuid();
        var tenant = CreateTenantRecord(tenantId, "test-tenant");
        _innerStoreMock.Setup(s => s.GetTenantAsync(tenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(tenant);

        // Act
        await _cachedStore.GetTenantAsync(tenantId);
        var result = await _cachedStore.GetTenantAsync(tenantId);

        // Assert
        result.Should().Be(tenant);
        _innerStoreMock.Verify(s => s.GetTenantAsync(tenantId, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GetTenantBySlugAsync_FirstCall_ShouldCallInnerStore()
    {
        // Arrange
        var tenant = CreateTenantRecord(Guid.NewGuid(), "test-tenant");
        _innerStoreMock.Setup(s => s.GetTenantBySlugAsync("test-tenant", It.IsAny<CancellationToken>()))
            .ReturnsAsync(tenant);

        // Act
        var result = await _cachedStore.GetTenantBySlugAsync("test-tenant");

        // Assert
        result.Should().Be(tenant);
        _innerStoreMock.Verify(s => s.GetTenantBySlugAsync("test-tenant", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GetTenantBySlugAsync_SecondCall_ShouldReturnCachedResult()
    {
        // Arrange
        var tenant = CreateTenantRecord(Guid.NewGuid(), "test-tenant");
        _innerStoreMock.Setup(s => s.GetTenantBySlugAsync("test-tenant", It.IsAny<CancellationToken>()))
            .ReturnsAsync(tenant);

        // Act
        await _cachedStore.GetTenantBySlugAsync("test-tenant");
        var result = await _cachedStore.GetTenantBySlugAsync("test-tenant");

        // Assert
        result.Should().Be(tenant);
        _innerStoreMock.Verify(s => s.GetTenantBySlugAsync("test-tenant", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GetTenantsAsync_FirstCall_ShouldCallInnerStore()
    {
        // Arrange
        var tenants = new List<TenantRecord>
        {
            CreateTenantRecord(Guid.NewGuid(), "tenant-1"),
            CreateTenantRecord(Guid.NewGuid(), "tenant-2")
        };
        _innerStoreMock.Setup(s => s.GetTenantsAsync(null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(tenants);

        // Act
        var result = await _cachedStore.GetTenantsAsync();

        // Assert
        result.Should().BeEquivalentTo(tenants);
        _innerStoreMock.Verify(s => s.GetTenantsAsync(null, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GetTenantsAsync_SecondCall_ShouldReturnCachedResult()
    {
        // Arrange
        var tenants = new List<TenantRecord>
        {
            CreateTenantRecord(Guid.NewGuid(), "tenant-1")
        };
        _innerStoreMock.Setup(s => s.GetTenantsAsync(null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(tenants);

        // Act
        await _cachedStore.GetTenantsAsync();
        var result = await _cachedStore.GetTenantsAsync();

        // Assert
        result.Should().BeEquivalentTo(tenants);
        _innerStoreMock.Verify(s => s.GetTenantsAsync(null, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task CreateTenantAsync_ShouldInvalidateCache()
    {
        // Arrange
        var tenantId = Guid.NewGuid();
        var request = new CreateTenantRequest("new-tenant", "tenant_new");
        var tenant = CreateTenantRecord(tenantId, "new-tenant");

        _innerStoreMock.Setup(s => s.CreateTenantAsync(tenantId, request, It.IsAny<CancellationToken>()))
            .ReturnsAsync(tenant);

        // Pre-populate cache
        var existingTenants = new List<TenantRecord>();
        _innerStoreMock.Setup(s => s.GetTenantsAsync(null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(existingTenants);
        await _cachedStore.GetTenantsAsync();

        // Act
        await _cachedStore.CreateTenantAsync(tenantId, request);

        // Setup for post-create call
        var updatedTenants = new List<TenantRecord> { tenant };
        _innerStoreMock.Setup(s => s.GetTenantsAsync(null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(updatedTenants);

        // Verify cache is invalidated by calling GetTenantsAsync again
        var result = await _cachedStore.GetTenantsAsync();

        // Assert - inner store should be called again after create
        _innerStoreMock.Verify(s => s.GetTenantsAsync(null, It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    [Fact]
    public async Task UpdateStatusAsync_ShouldInvalidateCache()
    {
        // Arrange
        var tenantId = Guid.NewGuid();
        var tenant = CreateTenantRecord(tenantId, "test-tenant");
        var updatedTenant = CreateTenantRecord(tenantId, "test-tenant", TenantStatus.Suspended);

        _innerStoreMock.Setup(s => s.GetTenantAsync(tenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(tenant);
        _innerStoreMock.Setup(s => s.UpdateStatusAsync(tenantId, TenantStatus.Suspended, It.IsAny<CancellationToken>()))
            .ReturnsAsync(updatedTenant);

        // Pre-populate cache
        await _cachedStore.GetTenantAsync(tenantId);

        // Act
        await _cachedStore.UpdateStatusAsync(tenantId, TenantStatus.Suspended);

        // Setup for post-update call
        _innerStoreMock.Setup(s => s.GetTenantAsync(tenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(updatedTenant);

        var result = await _cachedStore.GetTenantAsync(tenantId);

        // Assert - should return updated tenant (cached after update)
        result!.Status.Should().Be(TenantStatus.Suspended);
    }

    [Fact]
    public async Task DeleteTenantAsync_ShouldInvalidateCache()
    {
        // Arrange
        var tenantId = Guid.NewGuid();
        var tenant = CreateTenantRecord(tenantId, "test-tenant");

        _innerStoreMock.Setup(s => s.GetTenantAsync(tenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(tenant);
        _innerStoreMock.Setup(s => s.DeleteTenantAsync(tenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        // Pre-populate cache
        await _cachedStore.GetTenantAsync(tenantId);

        // Act
        await _cachedStore.DeleteTenantAsync(tenantId);

        // Setup for post-delete call
        _innerStoreMock.Setup(s => s.GetTenantAsync(tenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((TenantRecord?)null);

        var result = await _cachedStore.GetTenantAsync(tenantId);

        // Assert
        result.Should().BeNull();
        _innerStoreMock.Verify(s => s.GetTenantAsync(tenantId, It.IsAny<CancellationToken>()), Times.Exactly(3));
    }

    [Fact]
    public async Task GetTenantPasswordAsync_ShouldNeverCache()
    {
        // Arrange
        var tenantId = Guid.NewGuid();
        _innerStoreMock.Setup(s => s.GetTenantPasswordAsync(tenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync("secret-password");

        // Act
        await _cachedStore.GetTenantPasswordAsync(tenantId);
        await _cachedStore.GetTenantPasswordAsync(tenantId);

        // Assert - should call inner store each time
        _innerStoreMock.Verify(s => s.GetTenantPasswordAsync(tenantId, It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    [Fact]
    public async Task GetTenantByApiKeyAsync_ShouldNeverCache()
    {
        // Arrange
        var tenant = CreateTenantRecord(Guid.NewGuid(), "api-tenant");
        var apiKey = "test-api-key";
        _innerStoreMock.Setup(s => s.GetTenantByApiKeyAsync(apiKey, It.IsAny<CancellationToken>()))
            .ReturnsAsync(tenant);

        // Act
        await _cachedStore.GetTenantByApiKeyAsync(apiKey);
        var result = await _cachedStore.GetTenantByApiKeyAsync(apiKey);

        // Assert - API key verification should never be cached for security reasons
        result.Should().Be(tenant);
        _innerStoreMock.Verify(s => s.GetTenantByApiKeyAsync(apiKey, It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    private static TenantRecord CreateTenantRecord(
        Guid tenantId,
        string slug,
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
