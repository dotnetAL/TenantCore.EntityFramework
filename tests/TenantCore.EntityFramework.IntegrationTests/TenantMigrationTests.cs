using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using TenantCore.EntityFramework.Abstractions;
using TenantCore.EntityFramework.Context;
using TenantCore.EntityFramework.Extensions;
using TenantCore.EntityFramework.PostgreSql;
using Xunit;

namespace TenantCore.EntityFramework.IntegrationTests;

/// <summary>
/// Integration tests verifying that EF Core migrations are correctly applied to multiple tenant schemas.
/// These tests ensure that:
/// 1. Multiple tenant schemas can be provisioned with proper EF Core migrations
/// 2. The same migration history is tracked independently per schema
/// 3. Each tenant's schema has the correct structure from migrations
/// </summary>
[Collection("PostgreSql")]
[Trait("Category", "Integration")]
public class TenantMigrationTests
{
    private readonly PostgreSqlFixture _fixture;
    private const string MigrationsAssembly = "TenantCore.EntityFramework.IntegrationTests";

    public TenantMigrationTests(PostgreSqlFixture fixture)
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
        });

        services.AddTenantDbContextPostgreSql<MigrationTestDbContext, string>(
            _fixture.ConnectionString,
            MigrationsAssembly);

        return services.BuildServiceProvider();
    }

    /// <summary>
    /// Helper to clean up schemas after a test using ITenantManager
    /// </summary>
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

    /// <summary>
    /// Gets the list of tables in a tenant's schema.
    /// </summary>
    private async Task<List<string>> GetTablesInSchemaAsync(string tenantId)
    {
        var schemaName = $"tenant_{tenantId}";

        var options = new DbContextOptionsBuilder<MigrationTestDbContext>()
            .UseNpgsql(_fixture.ConnectionString)
            .Options;

        await using var context = new MigrationTestDbContext(options);

        await context.Database.OpenConnectionAsync();
        using var command = context.Database.GetDbConnection().CreateCommand();
        command.CommandText = $@"
            SELECT table_name FROM information_schema.tables
            WHERE table_schema = '{schemaName}'
            AND table_type = 'BASE TABLE'
            ORDER BY table_name";

        var tables = new List<string>();
        using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            tables.Add(reader.GetString(0));
        }

        return tables;
    }

    /// <summary>
    /// Checks if the __EFMigrationsHistory table exists in a tenant's schema.
    /// </summary>
    private async Task<bool> MigrationsHistoryExistsAsync(string tenantId)
    {
        var schemaName = $"tenant_{tenantId}";

        var options = new DbContextOptionsBuilder<MigrationTestDbContext>()
            .UseNpgsql(_fixture.ConnectionString)
            .Options;

        await using var context = new MigrationTestDbContext(options);

        await context.Database.OpenConnectionAsync();
        using var command = context.Database.GetDbConnection().CreateCommand();
        command.CommandText = $@"
            SELECT COUNT(*) FROM information_schema.tables
            WHERE table_schema = '{schemaName}'
            AND table_name = '__EFMigrationsHistory'";

        var result = await command.ExecuteScalarAsync();
        return Convert.ToInt64(result) > 0;
    }

    /// <summary>
    /// Gets the applied migrations from a tenant's __EFMigrationsHistory table.
    /// </summary>
    private async Task<List<string>> GetAppliedMigrationsAsync(string tenantId)
    {
        var schemaName = $"tenant_{tenantId}";

        var options = new DbContextOptionsBuilder<MigrationTestDbContext>()
            .UseNpgsql(_fixture.ConnectionString)
            .Options;

        await using var context = new MigrationTestDbContext(options);

        await context.Database.OpenConnectionAsync();
        using var command = context.Database.GetDbConnection().CreateCommand();
        command.CommandText = $@"
            SELECT ""MigrationId"" FROM ""{schemaName}"".""__EFMigrationsHistory""
            ORDER BY ""MigrationId""";

        var migrations = new List<string>();
        try
        {
            using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                migrations.Add(reader.GetString(0));
            }
        }
        catch
        {
            // Table may not exist yet
        }

        return migrations;
    }

    [Fact]
    public async Task ProvisionThreeTenants_ApplyMigrations_EachTenantShouldHaveSameSchema()
    {
        // Arrange
        await using var sp = BuildServiceProvider();
        using var scope = sp.CreateScope();
        var tenantManager = scope.ServiceProvider.GetRequiredService<ITenantManager<string>>();
        var tenantContextAccessor = scope.ServiceProvider.GetRequiredService<ITenantContextAccessor<string>>();
        var contextFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<MigrationTestDbContext>>();

        // Create 3 tenants
        var tenants = Enumerable.Range(1, 3)
            .Select(i => $"mig{i}_{Guid.NewGuid():N}"[..15])
            .ToList();

        try
        {
            // Act - Provision all 3 tenants using ITenantManager (applies EF Core migrations)
            foreach (var tenant in tenants)
            {
                await tenantManager.ProvisionTenantAsync(tenant);
            }

            // Assert - Each tenant should have the same tables from migrations
            foreach (var tenant in tenants)
            {
                var tables = await GetTablesInSchemaAsync(tenant);

                Assert.Contains("Products", tables);
                Assert.Contains("Categories", tables);
                Assert.Contains("__EFMigrationsHistory", tables);
            }

            // Verify each tenant can independently store and retrieve data
            foreach (var tenant in tenants)
            {
                var schemaName = $"tenant_{tenant}";
                tenantContextAccessor.SetTenantContext(new TenantContext<string>(tenant, schemaName));

                try
                {
                    await using var context = await contextFactory.CreateDbContextAsync();

                    // Add a category and product
                    var category = new Category { Name = $"Category for {tenant}" };
                    context.Categories.Add(category);
                    await context.SaveChangesAsync();

                    var product = new Product
                    {
                        Name = $"Product for {tenant}",
                        Price = 99.99m,
                        CategoryId = category.Id
                    };
                    context.Products.Add(product);
                    await context.SaveChangesAsync();

                    // Verify data was saved
                    var savedProduct = await context.Products
                        .Include(p => p.Category)
                        .FirstOrDefaultAsync();

                    Assert.NotNull(savedProduct);
                    Assert.Equal($"Product for {tenant}", savedProduct.Name);
                    Assert.NotNull(savedProduct.Category);
                    Assert.Equal($"Category for {tenant}", savedProduct.Category.Name);
                }
                finally
                {
                    tenantContextAccessor.SetTenantContext(null);
                }
            }
        }
        finally
        {
            await CleanupTenantsAsync(tenantManager, tenants.ToArray());
        }
    }

    [Fact]
    public async Task ProvisionTenant_ShouldCreateMigrationsHistoryInTenantSchema()
    {
        // Arrange
        await using var sp = BuildServiceProvider();
        using var scope = sp.CreateScope();
        var tenantManager = scope.ServiceProvider.GetRequiredService<ITenantManager<string>>();

        var tenantId = $"hist_{Guid.NewGuid():N}"[..15];

        try
        {
            // Act - Provision tenant using EF Core migrations
            await tenantManager.ProvisionTenantAsync(tenantId);

            // Assert - __EFMigrationsHistory should exist in tenant's schema
            var historyExists = await MigrationsHistoryExistsAsync(tenantId);
            Assert.True(historyExists, "Migrations history table should exist in tenant schema");

            // Verify migrations were recorded
            var appliedMigrations = await GetAppliedMigrationsAsync(tenantId);
            Assert.NotEmpty(appliedMigrations);
            Assert.Contains(appliedMigrations, m => m.Contains("Initial"));
        }
        finally
        {
            await CleanupTenantsAsync(tenantManager, tenantId);
        }
    }

    [Fact]
    public async Task ProvisionThreeTenants_EachTenantShouldHaveIndependentMigrationHistory()
    {
        // Arrange
        await using var sp = BuildServiceProvider();
        using var scope = sp.CreateScope();
        var tenantManager = scope.ServiceProvider.GetRequiredService<ITenantManager<string>>();

        var tenants = Enumerable.Range(1, 3)
            .Select(i => $"ind{i}_{Guid.NewGuid():N}"[..15])
            .ToList();

        try
        {
            // Act - Provision all tenants
            foreach (var tenant in tenants)
            {
                await tenantManager.ProvisionTenantAsync(tenant);
            }

            // Assert - Each tenant should have its own migrations history
            foreach (var tenant in tenants)
            {
                var historyExists = await MigrationsHistoryExistsAsync(tenant);
                Assert.True(historyExists, $"Tenant {tenant} should have its own migrations history");

                var appliedMigrations = await GetAppliedMigrationsAsync(tenant);
                Assert.NotEmpty(appliedMigrations);
            }

            // Verify migrations are tracked independently (no cross-contamination)
            var migrations1 = await GetAppliedMigrationsAsync(tenants[0]);
            var migrations2 = await GetAppliedMigrationsAsync(tenants[1]);
            var migrations3 = await GetAppliedMigrationsAsync(tenants[2]);

            // All should have the same migrations applied
            Assert.Equal(migrations1.Count, migrations2.Count);
            Assert.Equal(migrations2.Count, migrations3.Count);
        }
        finally
        {
            await CleanupTenantsAsync(tenantManager, tenants.ToArray());
        }
    }

    [Fact]
    public async Task MigrateTenant_WhenAlreadyMigrated_ShouldBeIdempotent()
    {
        // Arrange
        await using var sp = BuildServiceProvider();
        using var scope = sp.CreateScope();
        var tenantManager = scope.ServiceProvider.GetRequiredService<ITenantManager<string>>();

        var tenantId = $"idem_{Guid.NewGuid():N}"[..15];

        try
        {
            // Act - Provision tenant (applies migrations)
            await tenantManager.ProvisionTenantAsync(tenantId);

            // Get applied migrations count
            var migrationsBeforeSecondRun = await GetAppliedMigrationsAsync(tenantId);

            // Run migrations again
            await tenantManager.MigrateTenantAsync(tenantId);

            // Assert - Should be the same number of migrations (no duplicates)
            var migrationsAfterSecondRun = await GetAppliedMigrationsAsync(tenantId);
            Assert.Equal(migrationsBeforeSecondRun.Count, migrationsAfterSecondRun.Count);
        }
        finally
        {
            await CleanupTenantsAsync(tenantManager, tenantId);
        }
    }

    [Fact]
    public async Task MigrateAllTenants_ShouldApplyMigrationsToAllTenants()
    {
        // Arrange
        await using var sp = BuildServiceProvider();
        using var scope = sp.CreateScope();
        var tenantManager = scope.ServiceProvider.GetRequiredService<ITenantManager<string>>();

        var tenants = Enumerable.Range(1, 3)
            .Select(i => $"all{i}_{Guid.NewGuid():N}"[..15])
            .ToList();

        try
        {
            // Provision all tenants
            foreach (var tenant in tenants)
            {
                await tenantManager.ProvisionTenantAsync(tenant);
            }

            // Act - Migrate all tenants (should be idempotent since already migrated)
            await tenantManager.MigrateAllTenantsAsync();

            // Assert - All tenants should have migrations applied
            foreach (var tenant in tenants)
            {
                var appliedMigrations = await GetAppliedMigrationsAsync(tenant);
                Assert.NotEmpty(appliedMigrations);
            }
        }
        finally
        {
            await CleanupTenantsAsync(tenantManager, tenants.ToArray());
        }
    }

    [Fact]
    public async Task ProvisionThreeTenants_DataIsolationBetweenSchemas()
    {
        // Arrange
        await using var sp = BuildServiceProvider();
        using var scope = sp.CreateScope();
        var tenantManager = scope.ServiceProvider.GetRequiredService<ITenantManager<string>>();
        var tenantContextAccessor = scope.ServiceProvider.GetRequiredService<ITenantContextAccessor<string>>();
        var contextFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<MigrationTestDbContext>>();

        var tenants = Enumerable.Range(1, 3)
            .Select(i => $"iso{i}_{Guid.NewGuid():N}"[..15])
            .ToList();

        try
        {
            // Provision all tenants using EF Core migrations
            foreach (var tenant in tenants)
            {
                await tenantManager.ProvisionTenantAsync(tenant);
            }

            // Add unique data to each tenant
            foreach (var tenant in tenants)
            {
                var schemaName = $"tenant_{tenant}";
                tenantContextAccessor.SetTenantContext(new TenantContext<string>(tenant, schemaName));

                try
                {
                    await using var context = await contextFactory.CreateDbContextAsync();

                    var product = new Product
                    {
                        Name = $"Product_for_{tenant}",
                        Price = 50.00m
                    };
                    context.Products.Add(product);
                    await context.SaveChangesAsync();
                }
                finally
                {
                    tenantContextAccessor.SetTenantContext(null);
                }
            }

            // Assert - Each tenant should only see its own data
            foreach (var tenant in tenants)
            {
                var schemaName = $"tenant_{tenant}";
                tenantContextAccessor.SetTenantContext(new TenantContext<string>(tenant, schemaName));

                try
                {
                    await using var context = await contextFactory.CreateDbContextAsync();
                    var products = await context.Products.ToListAsync();

                    Assert.Single(products);
                    Assert.Equal($"Product_for_{tenant}", products[0].Name);
                }
                finally
                {
                    tenantContextAccessor.SetTenantContext(null);
                }
            }
        }
        finally
        {
            await CleanupTenantsAsync(tenantManager, tenants.ToArray());
        }
    }

    [Fact]
    public async Task TenantExists_ShouldReturnTrueForProvisionedTenant()
    {
        // Arrange
        await using var sp = BuildServiceProvider();
        using var scope = sp.CreateScope();
        var tenantManager = scope.ServiceProvider.GetRequiredService<ITenantManager<string>>();

        var tenantId = $"exists_{Guid.NewGuid():N}"[..15];

        try
        {
            // Assert - Should not exist before provisioning
            var existsBefore = await tenantManager.TenantExistsAsync(tenantId);
            Assert.False(existsBefore, "Tenant should not exist before provisioning");

            // Act
            await tenantManager.ProvisionTenantAsync(tenantId);

            // Assert - Should exist after provisioning
            var existsAfter = await tenantManager.TenantExistsAsync(tenantId);
            Assert.True(existsAfter, "Tenant should exist after provisioning");
        }
        finally
        {
            await CleanupTenantsAsync(tenantManager, tenantId);
        }
    }

    [Fact]
    public async Task GetTenants_ShouldReturnAllProvisionedTenants()
    {
        // Arrange
        await using var sp = BuildServiceProvider();
        using var scope = sp.CreateScope();
        var tenantManager = scope.ServiceProvider.GetRequiredService<ITenantManager<string>>();

        var uniquePrefix = Guid.NewGuid().ToString("N")[..6];
        var tenants = Enumerable.Range(1, 3)
            .Select(i => $"{uniquePrefix}_{i}")
            .ToList();

        try
        {
            // Provision all tenants
            foreach (var tenant in tenants)
            {
                await tenantManager.ProvisionTenantAsync(tenant);
            }

            // Act
            var allTenants = await tenantManager.GetTenantsAsync();
            var tenantList = allTenants.ToList();

            // Assert - All provisioned tenants should be returned
            // GetTenantsAsync returns tenant IDs (without the schema prefix)
            foreach (var tenant in tenants)
            {
                Assert.Contains(tenant, tenantList);
            }
        }
        finally
        {
            await CleanupTenantsAsync(tenantManager, tenants.ToArray());
        }
    }
}
