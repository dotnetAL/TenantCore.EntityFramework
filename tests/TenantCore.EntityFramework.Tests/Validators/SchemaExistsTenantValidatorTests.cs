using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using TenantCore.EntityFramework.Abstractions;
using TenantCore.EntityFramework.Configuration;
using TenantCore.EntityFramework.Context;
using TenantCore.EntityFramework.ControlDb;
using TenantCore.EntityFramework.Validators;
using Xunit;

namespace TenantCore.EntityFramework.Tests.Validators;

public class SchemaExistsTenantValidatorTests
{
    private readonly Mock<ISchemaManager> _schemaManager;
    private readonly TenantCoreOptions _options;
    private readonly Mock<ILogger<SchemaExistsTenantValidator<TestDbContext, string>>> _logger;

    public SchemaExistsTenantValidatorTests()
    {
        _schemaManager = new Mock<ISchemaManager>();
        _options = new TenantCoreOptions();
        _options.SchemaPerTenant.SchemaPrefix = "tenant_";
        _logger = new Mock<ILogger<SchemaExistsTenantValidator<TestDbContext, string>>>();
    }

    private SchemaExistsTenantValidator<TestDbContext, string> CreateValidator(
        ITenantStore? tenantStore = null)
    {
        var services = new ServiceCollection();

        // Register a minimal DbContextFactory for TestDbContext
        services.AddDbContextFactory<TestDbContext>(opts =>
            opts.UseInMemoryDatabase(Guid.NewGuid().ToString()));

        var serviceProvider = services.BuildServiceProvider();

        return new SchemaExistsTenantValidator<TestDbContext, string>(
            serviceProvider,
            _schemaManager.Object,
            _options,
            _logger.Object,
            tenantStore);
    }

    [Fact]
    public async Task ValidateTenantAsync_SchemaExists_NoControlDb_ReturnsTrue()
    {
        // Arrange
        _schemaManager
            .Setup(x => x.SchemaExistsAsync(It.IsAny<DbContext>(), "tenant_acme", It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var validator = CreateValidator();

        // Act
        var result = await validator.ValidateTenantAsync("acme");

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public async Task ValidateTenantAsync_SchemaDoesNotExist_ReturnsFalse()
    {
        // Arrange
        _schemaManager
            .Setup(x => x.SchemaExistsAsync(It.IsAny<DbContext>(), "tenant_unknown", It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var validator = CreateValidator();

        // Act
        var result = await validator.ValidateTenantAsync("unknown");

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public async Task ValidateTenantAsync_SchemaExists_ControlDbTenantActive_ReturnsTrue()
    {
        // Arrange
        _schemaManager
            .Setup(x => x.SchemaExistsAsync(It.IsAny<DbContext>(), "tenant_acme", It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var tenantStore = new Mock<ITenantStore>();
        tenantStore
            .Setup(x => x.GetTenantBySlugAsync("acme", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TenantRecord(
                Guid.NewGuid(), "acme", TenantStatus.Active, "tenant_acme",
                null, null, null, DateTime.UtcNow, DateTime.UtcNow));

        var validator = CreateValidator(tenantStore.Object);

        // Act
        var result = await validator.ValidateTenantAsync("acme");

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public async Task ValidateTenantAsync_SchemaExists_ControlDbTenantSuspended_ReturnsFalse()
    {
        // Arrange
        _schemaManager
            .Setup(x => x.SchemaExistsAsync(It.IsAny<DbContext>(), "tenant_acme", It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var tenantStore = new Mock<ITenantStore>();
        tenantStore
            .Setup(x => x.GetTenantBySlugAsync("acme", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TenantRecord(
                Guid.NewGuid(), "acme", TenantStatus.Suspended, "tenant_acme",
                null, null, null, DateTime.UtcNow, DateTime.UtcNow));

        var validator = CreateValidator(tenantStore.Object);

        // Act
        var result = await validator.ValidateTenantAsync("acme");

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public async Task ValidateTenantAsync_SchemaExists_ControlDbTenantNotFound_ReturnsFalse()
    {
        // Arrange
        _schemaManager
            .Setup(x => x.SchemaExistsAsync(It.IsAny<DbContext>(), "tenant_acme", It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var tenantStore = new Mock<ITenantStore>();
        tenantStore
            .Setup(x => x.GetTenantBySlugAsync("acme", It.IsAny<CancellationToken>()))
            .ReturnsAsync((TenantRecord?)null);

        var validator = CreateValidator(tenantStore.Object);

        // Act
        var result = await validator.ValidateTenantAsync("acme");

        // Assert
        result.Should().BeFalse();
    }

    /// <summary>
    /// Minimal DbContext for testing purposes.
    /// </summary>
    public class TestDbContext : TenantDbContext<string>
    {
        public TestDbContext(DbContextOptions<TestDbContext> options)
            : base(options)
        {
        }
    }
}
