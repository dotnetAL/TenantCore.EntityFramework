using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TenantCore.EntityFramework.Abstractions;
using TenantCore.EntityFramework.Configuration;
using TenantCore.EntityFramework.Context;
using TenantCore.EntityFramework.Extensions;
using Xunit;

namespace TenantCore.EntityFramework.Tests.Extensions;

public class TenantContextAccessorExtensionsTests
{
    [Fact]
    public async Task GetTenantDbContextAsync_WithNoTenantContext_ShouldThrow()
    {
        // Arrange
        var accessor = new TenantContextAccessor<string>();
        var sp = new ServiceCollection().BuildServiceProvider();

        // Act
        var act = () => accessor.GetTenantDbContextAsync<TestTenantDbContext, string>(sp);

        // Assert
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*No tenant context is set*");
    }

    [Fact]
    public async Task GetTenantDbContextAsync_WithTenantContext_ShouldReturnDbContext()
    {
        // Arrange
        var accessor = new TenantContextAccessor<string>();
        accessor.SetTenantContext(new TenantContext<string>("tenant1", "tenant_tenant1"));

        var services = new ServiceCollection();
        services.AddDbContextFactory<TestTenantDbContext>(opts =>
            opts.UseInMemoryDatabase($"test_{Guid.NewGuid()}"));
        var sp = services.BuildServiceProvider();

        // Act
        var result = await accessor.GetTenantDbContextAsync<TestTenantDbContext, string>(sp);

        // Assert
        result.Should().NotBeNull();
    }

    [Fact]
    public async Task GetTenantDbContextAsync_WithTenantId_ShouldSetContextAndReturnDbContext()
    {
        // Arrange
        var accessor = new TenantContextAccessor<string>();
        var options = new TenantCoreOptions();
        TenantContext<string>? capturedContext = null;

        var dbName = $"test_{Guid.NewGuid()}";
        var services = new ServiceCollection();
        services.AddSingleton(options);
        services.AddSingleton<ITenantContextAccessor<string>>(accessor);
        // Capture the tenant context inside the factory callback (runs synchronously during CreateDbContextAsync)
        services.AddDbContextFactory<TestTenantDbContext>((sp, opts) =>
        {
            capturedContext = accessor.TenantContext;
            opts.UseInMemoryDatabase(dbName);
        });
        await using var sp = services.BuildServiceProvider();

        // Act
        await using var result = await accessor.GetTenantDbContextAsync<TestTenantDbContext, string>("myTenant", sp);

        // Assert
        result.Should().NotBeNull();
        // Verify the context was set when the factory ran (during CreateDbContextAsync)
        capturedContext.Should().NotBeNull();
        capturedContext!.TenantId.Should().Be("myTenant");
        capturedContext.SchemaName.Should().Be("tenant_mytenant");
    }

    [Fact]
    public async Task MigrateTenantAsync_WithNullServiceProvider_ShouldThrow()
    {
        // Arrange
        var accessor = new TenantContextAccessor<string>();

        // Act
        var act = () => accessor.MigrateTenantAsync<TestTenantDbContext, string>("tenant1", null!);

        // Assert
        await act.Should().ThrowAsync<ArgumentNullException>();
    }

}

public class TestTenantDbContext : TenantDbContext<string>
{
    public TestTenantDbContext(DbContextOptions<TestTenantDbContext> options)
        : base(options)
    {
    }
}
