using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using TenantCore.EntityFramework.Abstractions;
using TenantCore.EntityFramework.ControlDb;
using TenantCore.EntityFramework.Extensions;
using TenantCore.EntityFramework.PostgreSql;
using Xunit;

namespace TenantCore.EntityFramework.IntegrationTests;

/// <summary>
/// Integration tests for control database functionality.
/// Verifies that tenants created via ProvisionTenantAsync are properly
/// tracked in the control database and returned by GetTenantsAsync.
/// </summary>
[Collection("PostgreSql")]
[Trait("Category", "Integration")]
public class ControlDatabaseTests
{
    private readonly PostgreSqlFixture _fixture;
    private const string MigrationsAssembly = "TenantCore.EntityFramework.IntegrationTests";

    public ControlDatabaseTests(PostgreSqlFixture fixture)
    {
        _fixture = fixture;
    }

    private ServiceProvider BuildServiceProviderWithControlDb()
    {
        var services = new ServiceCollection();

        services.AddLogging(builder => builder.AddConsole().SetMinimumLevel(LogLevel.Debug));

        services.AddTenantCore<string>(options =>
        {
            options.UseConnectionString(_fixture.ConnectionString);
            options.UseSchemaPerTenant(schema =>
            {
                schema.SchemaPrefix = "tenant_";
            });
        });

        // Add control database
        services.AddTenantControlDatabase(
            dbOptions => dbOptions.UseNpgsql(_fixture.ConnectionString),
            options =>
            {
                options.Schema = "tenant_control";
                options.EnableCaching = false; // Disable caching for tests
                options.ApplyMigrationsOnStartup = false;
            });

        services.AddTenantDbContextPostgreSql<TestDbContext, string>(
            _fixture.ConnectionString,
            MigrationsAssembly);

        return services.BuildServiceProvider();
    }

    private async Task EnsureControlDatabaseSchemaAsync(IServiceProvider sp)
    {
        using var scope = sp.CreateScope();
        var contextFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<ControlDbContext>>();
        await using var context = await contextFactory.CreateDbContextAsync();
        await context.Database.EnsureCreatedAsync();
    }

    private async Task CleanupTenantsAsync(ITenantManager<string> tenantManager, ITenantStore tenantStore, params string[] tenantIds)
    {
        foreach (var tenantId in tenantIds)
        {
            try
            {
                if (await tenantManager.TenantExistsAsync(tenantId))
                {
                    await tenantManager.DeleteTenantAsync(tenantId, hardDelete: true);
                }
            }
            catch
            {
                // Ignore cleanup errors for schema
            }

            try
            {
                // Also clean up control database record if it exists
                // Generate deterministic Guid from string (same logic as TenantManager)
                using var md5 = System.Security.Cryptography.MD5.Create();
                var hash = md5.ComputeHash(System.Text.Encoding.UTF8.GetBytes(tenantId));
                var guid = new Guid(hash);
                await tenantStore.DeleteTenantAsync(guid);
            }
            catch
            {
                // Ignore cleanup errors for control db
            }
        }
    }

    [Fact]
    public async Task ProvisionTenant_WithControlDatabase_ShouldCreateRecordInControlDb()
    {
        // Arrange
        await using var sp = BuildServiceProviderWithControlDb();
        await EnsureControlDatabaseSchemaAsync(sp);

        using var scope = sp.CreateScope();
        var tenantManager = scope.ServiceProvider.GetRequiredService<ITenantManager<string>>();
        var tenantStore = scope.ServiceProvider.GetRequiredService<ITenantStore>();

        var tenantId = $"ctrl_test_{Guid.NewGuid():N}"[..20];

        try
        {
            // Act
            await tenantManager.ProvisionTenantAsync(tenantId);

            // Assert - Tenant should exist in control database
            var tenants = await tenantStore.GetTenantsAsync();
            Assert.Contains(tenants, t => t.TenantSlug == tenantId);
        }
        finally
        {
            await CleanupTenantsAsync(tenantManager, tenantStore, tenantId);
        }
    }

    [Fact]
    public async Task GetTenantsAsync_WithControlDatabase_ShouldReturnProvisionedTenants()
    {
        // Arrange
        await using var sp = BuildServiceProviderWithControlDb();
        await EnsureControlDatabaseSchemaAsync(sp);

        using var scope = sp.CreateScope();
        var tenantManager = scope.ServiceProvider.GetRequiredService<ITenantManager<string>>();
        var tenantStore = scope.ServiceProvider.GetRequiredService<ITenantStore>();

        var tenant1 = $"list_t1_{Guid.NewGuid():N}"[..20];
        var tenant2 = $"list_t2_{Guid.NewGuid():N}"[..20];

        try
        {
            // Act - Create tenants using simple ProvisionTenantAsync
            await tenantManager.ProvisionTenantAsync(tenant1);
            await tenantManager.ProvisionTenantAsync(tenant2);

            // Assert - GetTenantsAsync should return both tenants
            var tenants = await tenantManager.GetTenantsAsync();
            var tenantList = tenants.ToList();

            Assert.Contains(tenant1, tenantList);
            Assert.Contains(tenant2, tenantList);
        }
        finally
        {
            await CleanupTenantsAsync(tenantManager, tenantStore, tenant1, tenant2);
        }
    }

    [Fact]
    public async Task ProvisionTenant_WithControlDatabase_ShouldSetStatusToActive()
    {
        // Arrange
        await using var sp = BuildServiceProviderWithControlDb();
        await EnsureControlDatabaseSchemaAsync(sp);

        using var scope = sp.CreateScope();
        var tenantManager = scope.ServiceProvider.GetRequiredService<ITenantManager<string>>();
        var tenantStore = scope.ServiceProvider.GetRequiredService<ITenantStore>();

        var tenantId = $"status_{Guid.NewGuid():N}"[..20];

        try
        {
            // Act
            await tenantManager.ProvisionTenantAsync(tenantId);

            // Assert - Tenant should have Active status
            var tenants = await tenantStore.GetTenantsAsync();
            var tenant = tenants.FirstOrDefault(t => t.TenantSlug == tenantId);

            Assert.NotNull(tenant);
            Assert.Equal(TenantStatus.Active, tenant.Status);
        }
        finally
        {
            await CleanupTenantsAsync(tenantManager, tenantStore, tenantId);
        }
    }

    [Fact]
    public async Task DeleteTenant_WithControlDatabase_ShouldRemoveFromControlDb()
    {
        // Arrange
        await using var sp = BuildServiceProviderWithControlDb();
        await EnsureControlDatabaseSchemaAsync(sp);

        using var scope = sp.CreateScope();
        var tenantManager = scope.ServiceProvider.GetRequiredService<ITenantManager<string>>();
        var tenantStore = scope.ServiceProvider.GetRequiredService<ITenantStore>();

        var tenantId = $"del_{Guid.NewGuid():N}"[..20];

        // Create a tenant first
        await tenantManager.ProvisionTenantAsync(tenantId);

        // Verify it exists
        var tenantsBeforeDelete = await tenantManager.GetTenantsAsync();
        Assert.Contains(tenantId, tenantsBeforeDelete);

        // Act - Delete the tenant
        await tenantManager.DeleteTenantAsync(tenantId, hardDelete: true);

        // Assert - Should no longer appear in GetTenantsAsync
        var tenantsAfterDelete = await tenantManager.GetTenantsAsync();
        Assert.DoesNotContain(tenantId, tenantsAfterDelete);
    }

    [Fact]
    public async Task ProvisionTenant_AlreadyExists_ShouldThrowTenantAlreadyExistsException()
    {
        // Arrange
        await using var sp = BuildServiceProviderWithControlDb();
        await EnsureControlDatabaseSchemaAsync(sp);

        using var scope = sp.CreateScope();
        var tenantManager = scope.ServiceProvider.GetRequiredService<ITenantManager<string>>();
        var tenantStore = scope.ServiceProvider.GetRequiredService<ITenantStore>();

        var tenantId = $"dup_{Guid.NewGuid():N}"[..20];

        try
        {
            // Create tenant first
            await tenantManager.ProvisionTenantAsync(tenantId);

            // Act & Assert - Second provision should throw
            await Assert.ThrowsAsync<TenantAlreadyExistsException>(
                () => tenantManager.ProvisionTenantAsync(tenantId));
        }
        finally
        {
            await CleanupTenantsAsync(tenantManager, tenantStore, tenantId);
        }
    }
}
