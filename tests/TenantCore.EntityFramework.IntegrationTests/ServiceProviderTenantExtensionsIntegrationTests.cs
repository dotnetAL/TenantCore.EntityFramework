using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using TenantCore.EntityFramework.Abstractions;
using TenantCore.EntityFramework.Extensions;
using TenantCore.EntityFramework.PostgreSql;
using Xunit;

namespace TenantCore.EntityFramework.IntegrationTests;

[Collection("PostgreSql")]
[Trait("Category", "Integration")]
public class ServiceProviderTenantExtensionsIntegrationTests
{
    private readonly PostgreSqlFixture _fixture;
    private const string MigrationsAssembly = "TenantCore.EntityFramework.IntegrationTests";

    public ServiceProviderTenantExtensionsIntegrationTests(PostgreSqlFixture fixture)
    {
        _fixture = fixture;
    }

    private ServiceProvider BuildServiceProvider()
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

        services.AddTenantDbContextPostgreSql<MigrationTestDbContext, string>(
            _fixture.ConnectionString,
            MigrationsAssembly);

        return services.BuildServiceProvider();
    }

    private async Task CleanupTenantsAsync(ITenantManager<string> tenantManager, params string[] tenantIds)
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
                // Ignore cleanup errors
            }
        }
    }

    [Fact]
    public async Task GetTenantDbContextAsync_WithProvisionedTenant_ShouldWriteAndReadData()
    {
        // Arrange
        await using var sp = BuildServiceProvider();
        using var scope = sp.CreateScope();
        var tenantManager = scope.ServiceProvider.GetRequiredService<ITenantManager<string>>();

        var tenantId = $"spx_{Guid.NewGuid():N}"[..15];

        try
        {
            await tenantManager.ProvisionTenantAsync(tenantId);

            // Act - get context via the new IServiceProvider extension
            await using var tenantScope = await scope.ServiceProvider
                .GetTenantDbContextAsync<MigrationTestDbContext, string>(tenantId);

            var product = new Product { Name = "ScopedExtTest", Price = 99.99m };
            tenantScope.Context.Products.Add(product);
            await tenantScope.Context.SaveChangesAsync();

            var saved = await tenantScope.Context.Products
                .FirstOrDefaultAsync(p => p.Name == "ScopedExtTest");

            // Assert
            Assert.NotNull(saved);
            Assert.Equal(99.99m, saved.Price);
        }
        finally
        {
            await CleanupTenantsAsync(tenantManager, tenantId);
        }
    }

    [Fact]
    public async Task GetTenantDbContextAsync_MultipleTenants_ShouldMaintainIsolation()
    {
        // Arrange
        await using var sp = BuildServiceProvider();
        using var scope = sp.CreateScope();
        var tenantManager = scope.ServiceProvider.GetRequiredService<ITenantManager<string>>();

        var tenantA = $"spa_{Guid.NewGuid():N}"[..15];
        var tenantB = $"spb_{Guid.NewGuid():N}"[..15];

        try
        {
            await tenantManager.ProvisionTenantAsync(tenantA);
            await tenantManager.ProvisionTenantAsync(tenantB);

            // Write to tenant A
            {
                await using var tenantScope = await scope.ServiceProvider
                    .GetTenantDbContextAsync<MigrationTestDbContext, string>(tenantA);
                tenantScope.Context.Products.Add(new Product { Name = "TenantA_Product", Price = 10m });
                await tenantScope.Context.SaveChangesAsync();
            }

            // Write to tenant B
            {
                await using var tenantScope = await scope.ServiceProvider
                    .GetTenantDbContextAsync<MigrationTestDbContext, string>(tenantB);
                tenantScope.Context.Products.Add(new Product { Name = "TenantB_Product", Price = 20m });
                await tenantScope.Context.SaveChangesAsync();
            }

            // Read tenant A - should only see its data
            {
                await using var tenantScope = await scope.ServiceProvider
                    .GetTenantDbContextAsync<MigrationTestDbContext, string>(tenantA);
                var products = await tenantScope.Context.Products.ToListAsync();

                Assert.Single(products);
                Assert.Equal("TenantA_Product", products[0].Name);
            }

            // Read tenant B - should only see its data
            {
                await using var tenantScope = await scope.ServiceProvider
                    .GetTenantDbContextAsync<MigrationTestDbContext, string>(tenantB);
                var products = await tenantScope.Context.Products.ToListAsync();

                Assert.Single(products);
                Assert.Equal("TenantB_Product", products[0].Name);
            }
        }
        finally
        {
            await CleanupTenantsAsync(tenantManager, tenantA, tenantB);
        }
    }
}
