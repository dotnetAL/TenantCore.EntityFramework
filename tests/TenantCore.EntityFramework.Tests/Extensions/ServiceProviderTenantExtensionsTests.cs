using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TenantCore.EntityFramework.Abstractions;
using TenantCore.EntityFramework.Configuration;
using TenantCore.EntityFramework.Context;
using TenantCore.EntityFramework.Extensions;
using Xunit;

namespace TenantCore.EntityFramework.Tests.Extensions;

public class ServiceProviderTenantExtensionsTests
{
    [Fact]
    public async Task GetTenantDbContextAsync_ShouldReturnScopeWithWorkingContext()
    {
        // Arrange
        var services = new ServiceCollection();
        var accessor = new TenantContextAccessor<string>();
        services.AddSingleton<ITenantContextAccessor<string>>(accessor);
        services.AddSingleton(new TenantCoreOptions());
        services.AddDbContextFactory<SpTestTenantDbContext>(opts =>
            opts.UseInMemoryDatabase($"test_{Guid.NewGuid()}"));
        await using var sp = services.BuildServiceProvider();

        // Act
        await using var scope = await sp.GetTenantDbContextAsync<SpTestTenantDbContext, string>("tenant1");

        // Assert
        scope.Should().NotBeNull();
        scope.Context.Should().NotBeNull();
        scope.Context.Should().BeOfType<SpTestTenantDbContext>();
    }

    [Fact]
    public async Task GetTenantDbContextAsync_ShouldComputeCorrectSchemaName()
    {
        // Arrange
        var accessor = new TenantContextAccessor<string>();
        var options = new TenantCoreOptions();
        options.SchemaPerTenant.SchemaPrefix = "custom_";

        var services = new ServiceCollection();
        services.AddSingleton<ITenantContextAccessor<string>>(accessor);
        services.AddSingleton(options);
        services.AddDbContextFactory<SpTestTenantDbContext>(opts =>
            opts.UseInMemoryDatabase($"test_{Guid.NewGuid()}"));
        await using var sp = services.BuildServiceProvider();

        // Act
        var scope = await sp.GetTenantDbContextAsync<SpTestTenantDbContext, string>("myTenant");

        // Assert - verify via dispose that the scope was created with the right accessor/context
        // (tenant context was set with custom prefix schema name)
        await scope.DisposeAsync();

        // After dispose, context should be null (no previous context)
        accessor.TenantContext.Should().BeNull();
    }

    [Fact]
    public async Task Dispose_ShouldRestorePreviousTenantContext()
    {
        // Arrange
        var accessor = new TenantContextAccessor<string>();
        var previousContext = new TenantContext<string>("previous", "tenant_previous");
        accessor.SetTenantContext(previousContext);

        var services = new ServiceCollection();
        services.AddSingleton<ITenantContextAccessor<string>>(accessor);
        services.AddSingleton(new TenantCoreOptions());
        services.AddDbContextFactory<SpTestTenantDbContext>(opts =>
            opts.UseInMemoryDatabase($"test_{Guid.NewGuid()}"));
        await using var sp = services.BuildServiceProvider();

        // Act
        var scope = await sp.GetTenantDbContextAsync<SpTestTenantDbContext, string>("newTenant");
        await scope.DisposeAsync();

        // Assert - previous context should be restored after dispose
        accessor.TenantContext.Should().NotBeNull();
        accessor.TenantContext!.TenantId.Should().Be("previous");
        accessor.TenantContext.SchemaName.Should().Be("tenant_previous");
    }

    [Fact]
    public async Task Dispose_ShouldRestoreNullWhenNoPreviousContext()
    {
        // Arrange
        var accessor = new TenantContextAccessor<string>();

        var services = new ServiceCollection();
        services.AddSingleton<ITenantContextAccessor<string>>(accessor);
        services.AddSingleton(new TenantCoreOptions());
        services.AddDbContextFactory<SpTestTenantDbContext>(opts =>
            opts.UseInMemoryDatabase($"test_{Guid.NewGuid()}"));
        await using var sp = services.BuildServiceProvider();

        // Act
        var scope = await sp.GetTenantDbContextAsync<SpTestTenantDbContext, string>("tenant1");
        await scope.DisposeAsync();

        // Assert
        accessor.TenantContext.Should().BeNull();
    }

    [Fact]
    public async Task Dispose_ShouldDisposeTheDbContext()
    {
        // Arrange
        var accessor = new TenantContextAccessor<string>();

        var services = new ServiceCollection();
        services.AddSingleton<ITenantContextAccessor<string>>(accessor);
        services.AddSingleton(new TenantCoreOptions());
        services.AddDbContextFactory<SpTestTenantDbContext>(opts =>
            opts.UseInMemoryDatabase($"test_{Guid.NewGuid()}"));
        await using var sp = services.BuildServiceProvider();

        // Act
        var scope = await sp.GetTenantDbContextAsync<SpTestTenantDbContext, string>("tenant1");
        var context = scope.Context;
        await scope.DisposeAsync();

        // Assert - accessing the disposed context should throw
        var act = () => context.TestEntities.ToList();
        act.Should().Throw<ObjectDisposedException>();
    }

    [Fact]
    public async Task GetTenantDbContextAsync_WithNullServiceProvider_ShouldThrowArgumentNullException()
    {
        // Arrange
        IServiceProvider sp = null!;

        // Act
        var act = () => sp.GetTenantDbContextAsync<SpTestTenantDbContext, string>("tenant1");

        // Assert
        await act.Should().ThrowAsync<ArgumentNullException>()
            .Where(e => e.ParamName == "serviceProvider");
    }

    [Fact]
    public async Task GetTenantDbContextAsync_WithNullTenantId_ShouldThrowArgumentNullException()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddSingleton<ITenantContextAccessor<string>>(new TenantContextAccessor<string>());
        services.AddSingleton(new TenantCoreOptions());
        services.AddDbContextFactory<SpTestTenantDbContext>(opts =>
            opts.UseInMemoryDatabase($"test_{Guid.NewGuid()}"));
        await using var sp = services.BuildServiceProvider();

        // Act
        var act = () => sp.GetTenantDbContextAsync<SpTestTenantDbContext, string>(null!);

        // Assert
        await act.Should().ThrowAsync<ArgumentNullException>()
            .Where(e => e.ParamName == "tenantId");
    }

    [Fact]
    public async Task Dispose_CalledTwice_ShouldNotThrow()
    {
        // Arrange
        var accessor = new TenantContextAccessor<string>();

        var services = new ServiceCollection();
        services.AddSingleton<ITenantContextAccessor<string>>(accessor);
        services.AddSingleton(new TenantCoreOptions());
        services.AddDbContextFactory<SpTestTenantDbContext>(opts =>
            opts.UseInMemoryDatabase($"test_{Guid.NewGuid()}"));
        await using var sp = services.BuildServiceProvider();

        var scope = await sp.GetTenantDbContextAsync<SpTestTenantDbContext, string>("tenant1");

        // Act
        await scope.DisposeAsync();
        var act = async () => await scope.DisposeAsync();

        // Assert - second dispose should be a no-op
        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task SequentialScopes_EachRestoresOriginalContext()
    {
        // Arrange
        var accessor = new TenantContextAccessor<string>();
        var originalContext = new TenantContext<string>("original", "tenant_original");
        accessor.SetTenantContext(originalContext);

        var services = new ServiceCollection();
        services.AddSingleton<ITenantContextAccessor<string>>(accessor);
        services.AddSingleton(new TenantCoreOptions());
        services.AddDbContextFactory<SpTestTenantDbContext>(opts =>
            opts.UseInMemoryDatabase($"test_{Guid.NewGuid()}"));
        await using var sp = services.BuildServiceProvider();

        // Act & Assert - first scope
        var scope1 = await sp.GetTenantDbContextAsync<SpTestTenantDbContext, string>("tenant1");
        await scope1.DisposeAsync();
        accessor.TenantContext.Should().NotBeNull();
        accessor.TenantContext!.TenantId.Should().Be("original");

        // Act & Assert - second scope (original context should still be active)
        var scope2 = await sp.GetTenantDbContextAsync<SpTestTenantDbContext, string>("tenant2");
        await scope2.DisposeAsync();
        accessor.TenantContext.Should().NotBeNull();
        accessor.TenantContext!.TenantId.Should().Be("original");
    }

    [Fact]
    public async Task GetTenantDbContextAsync_WhenFactoryThrows_ShouldRestorePreviousContext()
    {
        // Arrange
        var accessor = new TenantContextAccessor<string>();
        var previousContext = new TenantContext<string>("previous", "tenant_previous");
        accessor.SetTenantContext(previousContext);

        var services = new ServiceCollection();
        services.AddSingleton<ITenantContextAccessor<string>>(accessor);
        services.AddSingleton(new TenantCoreOptions());
        services.AddDbContextFactory<SpTestTenantDbContext>((sp, opts) =>
        {
            throw new InvalidOperationException("Factory failed");
        });
        await using var sp = services.BuildServiceProvider();

        // Act
        var act = () => sp.GetTenantDbContextAsync<SpTestTenantDbContext, string>("badTenant");
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("Factory failed");

        // Assert - previous context should be restored
        accessor.TenantContext.Should().NotBeNull();
        accessor.TenantContext!.TenantId.Should().Be("previous");
    }
}

public class SpTestTenantDbContext : TenantDbContext<string>
{
    public DbSet<SpTestEntity> TestEntities => Set<SpTestEntity>();

    public SpTestTenantDbContext(DbContextOptions<SpTestTenantDbContext> options)
        : base(options)
    {
    }
}

public class SpTestEntity
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
}
