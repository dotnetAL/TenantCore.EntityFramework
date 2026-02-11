using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using TenantCore.EntityFramework.Abstractions;
using TenantCore.EntityFramework.Context;
using TenantCore.EntityFramework.Extensions;
using TenantCore.EntityFramework.PostgreSql;
using Xunit;

namespace TenantCore.EntityFramework.IntegrationTests;

[Collection("PostgreSql")]
[Trait("Category", "Integration")]
public class TenantContextAccessorExtensionsIntegrationTests
{
    private readonly PostgreSqlFixture _fixture;
    private const string MigrationsAssembly = "TenantCore.EntityFramework.IntegrationTests";

    public TenantContextAccessorExtensionsIntegrationTests(PostgreSqlFixture fixture)
    {
        _fixture = fixture;
    }

    private ServiceProvider BuildServiceProvider(string? customHistoryTable = null)
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
            if (customHistoryTable != null)
            {
                options.ConfigureMigrations(migrations =>
                {
                    migrations.MigrationHistoryTable = customHistoryTable;
                });
            }
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

    private async Task<bool> TableExistsInSchemaAsync(string schemaName, string tableName)
    {
        var options = new DbContextOptionsBuilder<MigrationTestDbContext>()
            .UseNpgsql(_fixture.ConnectionString)
            .Options;

        await using var context = new MigrationTestDbContext(options);
        await context.Database.OpenConnectionAsync();

        await using var command = context.Database.GetDbConnection().CreateCommand();
        command.CommandText = $@"
            SELECT COUNT(*) FROM information_schema.tables
            WHERE table_schema = '{schemaName}'
            AND table_name = '{tableName}'";

        var result = await command.ExecuteScalarAsync();
        return Convert.ToInt64(result) > 0;
    }

    [Fact]
    public async Task GetTenantDbContextAsync_WithExistingTenant_ShouldReturnWorkingContext()
    {
        // Arrange
        await using var sp = BuildServiceProvider();
        using var scope = sp.CreateScope();
        var tenantManager = scope.ServiceProvider.GetRequiredService<ITenantManager<string>>();
        var accessor = scope.ServiceProvider.GetRequiredService<ITenantContextAccessor<string>>();
        var scopedSp = scope.ServiceProvider;

        var tenantId = $"ext_{Guid.NewGuid():N}"[..15];

        try
        {
            await tenantManager.ProvisionTenantAsync(tenantId);

            // Act - get context via extension method
            var schemaName = $"tenant_{tenantId}";
            accessor.SetTenantContext(new TenantContext<string>(tenantId, schemaName));
            await using var context = await accessor.GetTenantDbContextAsync<MigrationTestDbContext, string>(scopedSp);

            // Write data
            var product = new Product { Name = "ExtensionTest", Price = 42.00m };
            context.Products.Add(product);
            await context.SaveChangesAsync();

            // Read data back
            var saved = await context.Products.FirstOrDefaultAsync(p => p.Name == "ExtensionTest");

            // Assert
            Assert.NotNull(saved);
            Assert.Equal(42.00m, saved.Price);
        }
        finally
        {
            accessor.SetTenantContext(null);
            await CleanupTenantsAsync(tenantManager, tenantId);
        }
    }

    [Fact]
    public async Task MigrateTenantAsync_ForNewTenant_ShouldCreateSchemaAndApplyMigrations()
    {
        // Arrange
        await using var sp = BuildServiceProvider();
        using var scope = sp.CreateScope();
        var accessor = scope.ServiceProvider.GetRequiredService<ITenantContextAccessor<string>>();
        var tenantManager = scope.ServiceProvider.GetRequiredService<ITenantManager<string>>();
        var scopedSp = scope.ServiceProvider;

        var tenantId = $"migex_{Guid.NewGuid():N}"[..15];
        var schemaName = $"tenant_{tenantId}";

        try
        {
            // Act - migrate using extension method
            await accessor.MigrateTenantAsync<MigrationTestDbContext, string>(tenantId, scopedSp);

            // Assert - schema and tables should exist
            var productsExists = await TableExistsInSchemaAsync(schemaName, "Products");
            var categoriesExists = await TableExistsInSchemaAsync(schemaName, "Categories");
            var historyExists = await TableExistsInSchemaAsync(schemaName, "__EFMigrationsHistory");

            Assert.True(productsExists, "Products table should exist in tenant schema");
            Assert.True(categoriesExists, "Categories table should exist in tenant schema");
            Assert.True(historyExists, "Migrations history table should exist in tenant schema");

            // Assert - tenant context should be restored
            Assert.Null(accessor.TenantContext);
        }
        finally
        {
            // Clean up by dropping the schema directly since we didn't use tenantManager to provision
            try
            {
                var options = new DbContextOptionsBuilder<MigrationTestDbContext>()
                    .UseNpgsql(_fixture.ConnectionString)
                    .Options;
                await using var cleanupContext = new MigrationTestDbContext(options);
                await cleanupContext.Database.OpenConnectionAsync();
                await using var cmd = cleanupContext.Database.GetDbConnection().CreateCommand();
                cmd.CommandText = $"DROP SCHEMA IF EXISTS \"{schemaName}\" CASCADE";
                await cmd.ExecuteNonQueryAsync();
            }
            catch
            {
                // Ignore cleanup errors
            }
        }
    }

    [Fact]
    public async Task MigrateTenantAsync_ForExistingTenant_ShouldBeIdempotent()
    {
        // Arrange
        await using var sp = BuildServiceProvider();
        using var scope = sp.CreateScope();
        var accessor = scope.ServiceProvider.GetRequiredService<ITenantContextAccessor<string>>();
        var tenantManager = scope.ServiceProvider.GetRequiredService<ITenantManager<string>>();
        var scopedSp = scope.ServiceProvider;

        var tenantId = $"idem_{Guid.NewGuid():N}"[..15];

        try
        {
            // Provision first
            await tenantManager.ProvisionTenantAsync(tenantId);

            // Act - migrate again (should be idempotent)
            await accessor.MigrateTenantAsync<MigrationTestDbContext, string>(tenantId, scopedSp);

            // Assert - should complete without errors and context should be restored
            Assert.Null(accessor.TenantContext);

            // Verify tables still exist
            var schemaName = $"tenant_{tenantId}";
            var productsExists = await TableExistsInSchemaAsync(schemaName, "Products");
            Assert.True(productsExists);
        }
        finally
        {
            await CleanupTenantsAsync(tenantManager, tenantId);
        }
    }

    [Fact]
    public async Task MigrateTenantAsync_WithCustomHistoryTable_ShouldUseConfiguredName()
    {
        // Arrange
        await using var sp = BuildServiceProvider(customHistoryTable: "__CustomExtMigrations");
        using var scope = sp.CreateScope();
        var accessor = scope.ServiceProvider.GetRequiredService<ITenantContextAccessor<string>>();
        var scopedSp = scope.ServiceProvider;

        var tenantId = $"custx_{Guid.NewGuid():N}"[..15];
        var schemaName = $"tenant_{tenantId}";

        try
        {
            // Act
            await accessor.MigrateTenantAsync<MigrationTestDbContext, string>(tenantId, scopedSp);

            // Assert
            var customTableExists = await TableExistsInSchemaAsync(schemaName, "__CustomExtMigrations");
            Assert.True(customTableExists, "Custom migration history table should exist");
        }
        finally
        {
            try
            {
                var options = new DbContextOptionsBuilder<MigrationTestDbContext>()
                    .UseNpgsql(_fixture.ConnectionString)
                    .Options;
                await using var cleanupContext = new MigrationTestDbContext(options);
                await cleanupContext.Database.OpenConnectionAsync();
                await using var cmd = cleanupContext.Database.GetDbConnection().CreateCommand();
                cmd.CommandText = $"DROP SCHEMA IF EXISTS \"{schemaName}\" CASCADE";
                await cmd.ExecuteNonQueryAsync();
            }
            catch
            {
                // Ignore cleanup errors
            }
        }
    }
}
