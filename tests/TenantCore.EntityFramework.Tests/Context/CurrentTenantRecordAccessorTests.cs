using FluentAssertions;
using Moq;
using TenantCore.EntityFramework.Abstractions;
using TenantCore.EntityFramework.Context;
using TenantCore.EntityFramework.ControlDb;
using Xunit;

namespace TenantCore.EntityFramework.Tests.Context;

public class CurrentTenantRecordAccessorTests
{
    private readonly Mock<ITenantContextAccessor<string>> _contextAccessor;
    private readonly Mock<ITenantStore> _tenantStore;

    private static readonly TenantRecord AcmeRecord = new(
        Guid.NewGuid(), "acme", TenantStatus.Active, "tenant_acme",
        null, null, null, DateTime.UtcNow, DateTime.UtcNow);

    private static readonly TenantRecord ContosRecord = new(
        Guid.NewGuid(), "contoso", TenantStatus.Active, "tenant_contoso",
        null, null, null, DateTime.UtcNow, DateTime.UtcNow);

    public CurrentTenantRecordAccessorTests()
    {
        _contextAccessor = new Mock<ITenantContextAccessor<string>>();
        _tenantStore = new Mock<ITenantStore>();
    }

    private CurrentTenantRecordAccessor<string> CreateAccessor(ITenantStore? tenantStore = null)
    {
        return new CurrentTenantRecordAccessor<string>(
            _contextAccessor.Object,
            tenantStore);
    }

    [Fact]
    public async Task GetCurrentTenantRecordAsync_NoTenantStore_ReturnsNull()
    {
        // Arrange
        _contextAccessor
            .Setup(x => x.TenantContext)
            .Returns(new TenantContext<string>("acme"));

        var accessor = CreateAccessor(tenantStore: null);

        // Act
        var result = await accessor.GetCurrentTenantRecordAsync();

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public async Task GetCurrentTenantRecordAsync_NoTenantContext_ReturnsNull()
    {
        // Arrange
        _contextAccessor
            .Setup(x => x.TenantContext)
            .Returns((TenantContext<string>?)null);

        var accessor = CreateAccessor(_tenantStore.Object);

        // Act
        var result = await accessor.GetCurrentTenantRecordAsync();

        // Assert
        result.Should().BeNull();
        _tenantStore.Verify(
            x => x.GetTenantBySlugAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task GetCurrentTenantRecordAsync_InvalidTenantContext_ReturnsNull()
    {
        // Arrange - default(string) is null, which makes IsValid false
        _contextAccessor
            .Setup(x => x.TenantContext)
            .Returns(new TenantContext<string>(default!));

        var accessor = CreateAccessor(_tenantStore.Object);

        // Act
        var result = await accessor.GetCurrentTenantRecordAsync();

        // Assert
        result.Should().BeNull();
        _tenantStore.Verify(
            x => x.GetTenantBySlugAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task GetCurrentTenantRecordAsync_HappyPath_ReturnsTenantRecord()
    {
        // Arrange
        _contextAccessor
            .Setup(x => x.TenantContext)
            .Returns(new TenantContext<string>("acme"));

        _tenantStore
            .Setup(x => x.GetTenantBySlugAsync("acme", It.IsAny<CancellationToken>()))
            .ReturnsAsync(AcmeRecord);

        var accessor = CreateAccessor(_tenantStore.Object);

        // Act
        var result = await accessor.GetCurrentTenantRecordAsync();

        // Assert
        result.Should().BeSameAs(AcmeRecord);
    }

    [Fact]
    public async Task GetCurrentTenantRecordAsync_TenantNotFoundInStore_ReturnsNull()
    {
        // Arrange
        _contextAccessor
            .Setup(x => x.TenantContext)
            .Returns(new TenantContext<string>("unknown"));

        _tenantStore
            .Setup(x => x.GetTenantBySlugAsync("unknown", It.IsAny<CancellationToken>()))
            .ReturnsAsync((TenantRecord?)null);

        var accessor = CreateAccessor(_tenantStore.Object);

        // Act
        var result = await accessor.GetCurrentTenantRecordAsync();

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public async Task GetCurrentTenantRecordAsync_MultipleCalls_CachesResult()
    {
        // Arrange
        _contextAccessor
            .Setup(x => x.TenantContext)
            .Returns(new TenantContext<string>("acme"));

        _tenantStore
            .Setup(x => x.GetTenantBySlugAsync("acme", It.IsAny<CancellationToken>()))
            .ReturnsAsync(AcmeRecord);

        var accessor = CreateAccessor(_tenantStore.Object);

        // Act
        var result1 = await accessor.GetCurrentTenantRecordAsync();
        var result2 = await accessor.GetCurrentTenantRecordAsync();
        var result3 = await accessor.GetCurrentTenantRecordAsync();

        // Assert
        result1.Should().BeSameAs(AcmeRecord);
        result2.Should().BeSameAs(AcmeRecord);
        result3.Should().BeSameAs(AcmeRecord);

        _tenantStore.Verify(
            x => x.GetTenantBySlugAsync("acme", It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task GetCurrentTenantRecordAsync_MultipleCalls_NullResult_CachesNull()
    {
        // Arrange
        _contextAccessor
            .Setup(x => x.TenantContext)
            .Returns(new TenantContext<string>("unknown"));

        _tenantStore
            .Setup(x => x.GetTenantBySlugAsync("unknown", It.IsAny<CancellationToken>()))
            .ReturnsAsync((TenantRecord?)null);

        var accessor = CreateAccessor(_tenantStore.Object);

        // Act
        var result1 = await accessor.GetCurrentTenantRecordAsync();
        var result2 = await accessor.GetCurrentTenantRecordAsync();

        // Assert
        result1.Should().BeNull();
        result2.Should().BeNull();

        _tenantStore.Verify(
            x => x.GetTenantBySlugAsync("unknown", It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task GetCurrentTenantRecordAsync_TenantChanges_RefetchesFromStore()
    {
        // Arrange - start with "acme"
        _contextAccessor
            .Setup(x => x.TenantContext)
            .Returns(new TenantContext<string>("acme"));

        _tenantStore
            .Setup(x => x.GetTenantBySlugAsync("acme", It.IsAny<CancellationToken>()))
            .ReturnsAsync(AcmeRecord);

        _tenantStore
            .Setup(x => x.GetTenantBySlugAsync("contoso", It.IsAny<CancellationToken>()))
            .ReturnsAsync(ContosRecord);

        var accessor = CreateAccessor(_tenantStore.Object);

        // Act - fetch for acme
        var result1 = await accessor.GetCurrentTenantRecordAsync();

        // Simulate TenantScope switch
        _contextAccessor
            .Setup(x => x.TenantContext)
            .Returns(new TenantContext<string>("contoso"));

        var result2 = await accessor.GetCurrentTenantRecordAsync();

        // Assert
        result1.Should().BeSameAs(AcmeRecord);
        result2.Should().BeSameAs(ContosRecord);

        _tenantStore.Verify(
            x => x.GetTenantBySlugAsync("acme", It.IsAny<CancellationToken>()),
            Times.Once);
        _tenantStore.Verify(
            x => x.GetTenantBySlugAsync("contoso", It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task GetCurrentTenantRecordAsync_StoreThrows_PropagatesException()
    {
        // Arrange
        _contextAccessor
            .Setup(x => x.TenantContext)
            .Returns(new TenantContext<string>("acme"));

        _tenantStore
            .Setup(x => x.GetTenantBySlugAsync("acme", It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Database connection failed"));

        var accessor = CreateAccessor(_tenantStore.Object);

        // Act
        var act = () => accessor.GetCurrentTenantRecordAsync();

        // Assert
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("Database connection failed");
    }
}
