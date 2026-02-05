using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using TenantCore.EntityFramework.Abstractions;
using TenantCore.EntityFramework.Context;
using TenantCore.EntityFramework.Extensions;
using TenantCore.EntityFramework.PostgreSql;
using Xunit;

namespace TenantCore.EntityFramework.IntegrationTests;

/// <summary>
/// Integration tests verifying that migrations are correctly applied to multiple tenant schemas.
/// These tests ensure that:
/// 1. Multiple tenant schemas can be provisioned
/// 2. The same table structure (migrations) is applied to each schema
/// 3. Each tenant's schema is independent and has the correct structure
/// </summary>
[Collection("PostgreSql")]
[Trait("Category", "Integration")]
public class TenantMigrationTests
{
    private readonly PostgreSqlFixture _fixture;

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

        services.AddTenantDbContextPostgreSql<MigrationTestDbContext, string>(_fixture.ConnectionString);

        return services.BuildServiceProvider();
    }

    /// <summary>
    /// Helper to clean up schemas after a test
    /// </summary>
    private async Task CleanupSchemasAsync(ISchemaManager schemaManager, params string[] tenantIds)
    {
        var options = new DbContextOptionsBuilder<MigrationTestDbContext>()
            .UseNpgsql(_fixture.ConnectionString)
            .Options;

        await using var context = new MigrationTestDbContext(options);

        foreach (var tenantId in tenantIds)
        {
            try
            {
                var schemaName = $"tenant_{tenantId}";
                if (await schemaManager.SchemaExistsAsync(context, schemaName))
                {
                    await schemaManager.DropSchemaAsync(context, schemaName, cascade: true);
                }
            }
            catch
            {
                // Ignore cleanup errors
            }
        }
    }

    /// <summary>
    /// Provisions a tenant schema and applies the database structure using EF Core's CreateTables.
    /// This simulates what migrations would do - creating the schema structure in each tenant.
    /// </summary>
    private async Task ProvisionTenantWithMigrationsAsync(
        ISchemaManager schemaManager,
        ITenantContextAccessor<string> tenantContextAccessor,
        IDbContextFactory<MigrationTestDbContext> contextFactory,
        string tenantId)
    {
        var schemaName = $"tenant_{tenantId}";

        // Create the schema first
        var adminOptions = new DbContextOptionsBuilder<MigrationTestDbContext>()
            .UseNpgsql(_fixture.ConnectionString)
            .Options;

        await using var adminContext = new MigrationTestDbContext(adminOptions);
        await schemaManager.CreateSchemaAsync(adminContext, schemaName);

        // Set tenant context and create tables (simulating migration application)
        tenantContextAccessor.SetTenantContext(new TenantContext<string>(tenantId, schemaName));
        try
        {
            await using var context = await contextFactory.CreateDbContextAsync();
            var databaseCreator = context.GetService<IRelationalDatabaseCreator>();
            await databaseCreator.CreateTablesAsync();
        }
        finally
        {
            tenantContextAccessor.SetTenantContext(null);
        }
    }

    /// <summary>
    /// Adds an additional column to a tenant's schema to simulate a migration update.
    /// </summary>
    private async Task ApplyAdditionalMigrationAsync(
        ISchemaManager schemaManager,
        string tenantId,
        string columnName,
        string columnType)
    {
        var schemaName = $"tenant_{tenantId}";

        var options = new DbContextOptionsBuilder<MigrationTestDbContext>()
            .UseNpgsql(_fixture.ConnectionString)
            .Options;

        await using var context = new MigrationTestDbContext(options);

        // Add a new column to Products table (simulating a migration)
        var sql = $@"ALTER TABLE ""{schemaName}"".""Products"" ADD COLUMN IF NOT EXISTS ""{columnName}"" {columnType}";
#pragma warning disable EF1002
        await context.Database.ExecuteSqlRawAsync(sql);
#pragma warning restore EF1002
    }

    /// <summary>
    /// Checks if a column exists in a tenant's schema.
    /// </summary>
    private async Task<bool> ColumnExistsAsync(string tenantId, string tableName, string columnName)
    {
        var schemaName = $"tenant_{tenantId}";

        var options = new DbContextOptionsBuilder<MigrationTestDbContext>()
            .UseNpgsql(_fixture.ConnectionString)
            .Options;

        await using var context = new MigrationTestDbContext(options);

        var sql = $@"
            SELECT COUNT(*) FROM information_schema.columns
            WHERE table_schema = '{schemaName}'
            AND table_name = '{tableName}'
            AND column_name = '{columnName}'";

#pragma warning disable EF1002
        var result = await context.Database.ExecuteSqlRawAsync(sql);
#pragma warning restore EF1002

        // Use a proper query to check
        using var command = context.Database.GetDbConnection().CreateCommand();
        command.CommandText = sql;
        await context.Database.OpenConnectionAsync();
        var count = await command.ExecuteScalarAsync();
        return Convert.ToInt32(count) > 0;
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

    [Fact]
    public async Task ProvisionThreeTenants_ApplyMigrations_EachTenantShouldHaveSameSchema()
    {
        // Arrange
        await using var sp = BuildServiceProvider();
        using var scope = sp.CreateScope();
        var schemaManager = scope.ServiceProvider.GetRequiredService<ISchemaManager>();
        var tenantContextAccessor = scope.ServiceProvider.GetRequiredService<ITenantContextAccessor<string>>();
        var contextFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<MigrationTestDbContext>>();

        // Create 3 tenants
        var tenants = Enumerable.Range(1, 3)
            .Select(i => $"mig{i}_{Guid.NewGuid():N}"[..15])
            .ToList();

        try
        {
            // Act - Provision all 3 tenants with migrations (initial schema)
            foreach (var tenant in tenants)
            {
                await ProvisionTenantWithMigrationsAsync(schemaManager, tenantContextAccessor, contextFactory, tenant);
            }

            // Assert - Each tenant should have the same tables
            foreach (var tenant in tenants)
            {
                var tables = await GetTablesInSchemaAsync(tenant);

                Assert.Contains("Products", tables);
                Assert.Contains("Categories", tables);
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
            await CleanupSchemasAsync(schemaManager, tenants.ToArray());
        }
    }

    [Fact]
    public async Task ProvisionThreeTenants_ApplyNewMigration_AllTenantsShouldHaveNewColumn()
    {
        // Arrange
        await using var sp = BuildServiceProvider();
        using var scope = sp.CreateScope();
        var schemaManager = scope.ServiceProvider.GetRequiredService<ISchemaManager>();
        var tenantContextAccessor = scope.ServiceProvider.GetRequiredService<ITenantContextAccessor<string>>();
        var contextFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<MigrationTestDbContext>>();

        // Create 3 tenants
        var tenants = Enumerable.Range(1, 3)
            .Select(i => $"newmig{i}_{Guid.NewGuid():N}"[..15])
            .ToList();

        var newColumnName = "StockQuantity";

        try
        {
            // Step 1: Provision all 3 tenants with initial migrations
            foreach (var tenant in tenants)
            {
                await ProvisionTenantWithMigrationsAsync(schemaManager, tenantContextAccessor, contextFactory, tenant);
            }

            // Verify the new column does NOT exist yet
            foreach (var tenant in tenants)
            {
                var columnExists = await ColumnExistsAsync(tenant, "Products", newColumnName);
                Assert.False(columnExists, $"tenant {tenant} should NOT have {newColumnName} column before migration");
            }

            // Step 2: Apply a new "migration" to all tenants (add StockQuantity column)
            foreach (var tenant in tenants)
            {
                await ApplyAdditionalMigrationAsync(schemaManager, tenant, newColumnName, "INTEGER DEFAULT 0");
            }

            // Assert - Each tenant should now have the new column
            foreach (var tenant in tenants)
            {
                var columnExists = await ColumnExistsAsync(tenant, "Products", newColumnName);
                Assert.True(columnExists, $"tenant {tenant} should have {newColumnName} column after migration");
            }

            // Verify we can use the new column in each tenant
            foreach (var tenant in tenants)
            {
                var schemaName = $"tenant_{tenant}";
                tenantContextAccessor.SetTenantContext(new TenantContext<string>(tenant, schemaName));

                try
                {
                    await using var context = await contextFactory.CreateDbContextAsync();

                    // Insert data using raw SQL to include the new column
                    var insertSql = $@"
                        INSERT INTO ""{schemaName}"".""Products"" (""Name"", ""Price"", ""CreatedAt"", ""{newColumnName}"")
                        VALUES ('Test Product', 10.00, NOW(), 100)";
#pragma warning disable EF1002
                    await context.Database.ExecuteSqlRawAsync(insertSql);
#pragma warning restore EF1002

                    // Query to verify the new column has data
                    var selectSql = $@"SELECT ""{newColumnName}"" FROM ""{schemaName}"".""Products"" LIMIT 1";
                    await context.Database.OpenConnectionAsync();
                    using var command = context.Database.GetDbConnection().CreateCommand();
                    command.CommandText = selectSql;
                    var stockValue = await command.ExecuteScalarAsync();

                    Assert.Equal(100, Convert.ToInt32(stockValue));
                }
                finally
                {
                    tenantContextAccessor.SetTenantContext(null);
                }
            }
        }
        finally
        {
            await CleanupSchemasAsync(schemaManager, tenants.ToArray());
        }
    }

    [Fact]
    public async Task ProvisionThreeTenants_MigrationsAppliedIndependently_ShouldNotAffectOtherTenants()
    {
        // Arrange
        await using var sp = BuildServiceProvider();
        using var scope = sp.CreateScope();
        var schemaManager = scope.ServiceProvider.GetRequiredService<ISchemaManager>();
        var tenantContextAccessor = scope.ServiceProvider.GetRequiredService<ITenantContextAccessor<string>>();
        var contextFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<MigrationTestDbContext>>();

        // Create 3 tenants
        var tenant1 = $"ind1_{Guid.NewGuid():N}"[..15];
        var tenant2 = $"ind2_{Guid.NewGuid():N}"[..15];
        var tenant3 = $"ind3_{Guid.NewGuid():N}"[..15];
        var tenants = new[] { tenant1, tenant2, tenant3 };

        var newColumnName = "SpecialField";

        try
        {
            // Provision all 3 tenants with initial migrations
            foreach (var tenant in tenants)
            {
                await ProvisionTenantWithMigrationsAsync(schemaManager, tenantContextAccessor, contextFactory, tenant);
            }

            // Apply the new migration ONLY to tenant1 and tenant2 (not tenant3)
            await ApplyAdditionalMigrationAsync(schemaManager, tenant1, newColumnName, "VARCHAR(100)");
            await ApplyAdditionalMigrationAsync(schemaManager, tenant2, newColumnName, "VARCHAR(100)");

            // Assert - tenant1 and tenant2 should have the new column
            var tenant1HasColumn = await ColumnExistsAsync(tenant1, "Products", newColumnName);
            var tenant2HasColumn = await ColumnExistsAsync(tenant2, "Products", newColumnName);
            var tenant3HasColumn = await ColumnExistsAsync(tenant3, "Products", newColumnName);

            Assert.True(tenant1HasColumn, "tenant1 should have the new column");
            Assert.True(tenant2HasColumn, "tenant2 should have the new column");
            Assert.False(tenant3HasColumn, "tenant3 should NOT have the new column (migration not applied)");

            // Verify tenant3 can still operate normally without the new column
            var schemaName3 = $"tenant_{tenant3}";
            tenantContextAccessor.SetTenantContext(new TenantContext<string>(tenant3, schemaName3));

            try
            {
                await using var context = await contextFactory.CreateDbContextAsync();

                // This should work fine - tenant3 has the base schema
                var product = new Product { Name = "Tenant3 Product", Price = 50.00m };
                context.Products.Add(product);
                await context.SaveChangesAsync();

                var savedProduct = await context.Products.FirstOrDefaultAsync();
                Assert.NotNull(savedProduct);
                Assert.Equal("Tenant3 Product", savedProduct.Name);
            }
            finally
            {
                tenantContextAccessor.SetTenantContext(null);
            }
        }
        finally
        {
            await CleanupSchemasAsync(schemaManager, tenants);
        }
    }

    [Fact]
    public async Task ProvisionThreeTenants_ApplyMultipleMigrations_AllMigrationsShouldBeApplied()
    {
        // Arrange
        await using var sp = BuildServiceProvider();
        using var scope = sp.CreateScope();
        var schemaManager = scope.ServiceProvider.GetRequiredService<ISchemaManager>();
        var tenantContextAccessor = scope.ServiceProvider.GetRequiredService<ITenantContextAccessor<string>>();
        var contextFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<MigrationTestDbContext>>();

        // Create 3 tenants
        var tenants = Enumerable.Range(1, 3)
            .Select(i => $"multi{i}_{Guid.NewGuid():N}"[..15])
            .ToList();

        // Multiple migrations to apply
        var migrations = new[]
        {
            ("Migration1_StockQty", "StockQuantity", "INTEGER DEFAULT 0"),
            ("Migration2_Weight", "Weight", "DECIMAL(10,2)"),
            ("Migration3_IsActive", "IsActive", "BOOLEAN DEFAULT TRUE")
        };

        try
        {
            // Provision all 3 tenants with initial schema
            foreach (var tenant in tenants)
            {
                await ProvisionTenantWithMigrationsAsync(schemaManager, tenantContextAccessor, contextFactory, tenant);
            }

            // Apply all migrations to all tenants
            foreach (var (migrationName, columnName, columnType) in migrations)
            {
                foreach (var tenant in tenants)
                {
                    await ApplyAdditionalMigrationAsync(schemaManager, tenant, columnName, columnType);
                }
            }

            // Assert - Each tenant should have all new columns
            foreach (var tenant in tenants)
            {
                foreach (var (_, columnName, _) in migrations)
                {
                    var hasColumn = await ColumnExistsAsync(tenant, "Products", columnName);
                    Assert.True(hasColumn, $"tenant {tenant} should have column {columnName}");
                }

                // Verify we can query with all columns
                var schemaName = $"tenant_{tenant}";

                var options = new DbContextOptionsBuilder<MigrationTestDbContext>()
                    .UseNpgsql(_fixture.ConnectionString)
                    .Options;

                await using var context = new MigrationTestDbContext(options);
                await context.Database.OpenConnectionAsync();

                using var command = context.Database.GetDbConnection().CreateCommand();
                command.CommandText = $@"
                    INSERT INTO ""{schemaName}"".""Products""
                    (""Name"", ""Price"", ""CreatedAt"", ""StockQuantity"", ""Weight"", ""IsActive"")
                    VALUES ('Full Product', 25.00, NOW(), 50, 1.5, true)
                    RETURNING ""Id"", ""StockQuantity"", ""Weight"", ""IsActive""";

                using var reader = await command.ExecuteReaderAsync();
                await reader.ReadAsync();

                var id = reader.GetInt32(0);
                var stockQty = reader.GetInt32(1);
                var weight = reader.GetDecimal(2);
                var isActive = reader.GetBoolean(3);

                Assert.True(id > 0);
                Assert.Equal(50, stockQty);
                Assert.Equal(1.5m, weight);
                Assert.True(isActive);
            }
        }
        finally
        {
            await CleanupSchemasAsync(schemaManager, tenants.ToArray());
        }
    }
}
