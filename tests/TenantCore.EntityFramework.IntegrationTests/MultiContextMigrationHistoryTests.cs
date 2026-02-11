using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using TenantCore.EntityFramework.Abstractions;
using TenantCore.EntityFramework.Configuration;
using TenantCore.EntityFramework.Context;
using TenantCore.EntityFramework.Extensions;
using TenantCore.EntityFramework.Migrations;
using TenantCore.EntityFramework.PostgreSql;
using Xunit;

namespace TenantCore.EntityFramework.IntegrationTests;

/// <summary>
/// Integration tests verifying that multiple DbContext types can coexist with
/// independent per-context migration history tables, and that each tenant schema
/// gets its own copy of each history table.
/// </summary>
[Collection("PostgreSql")]
[Trait("Category", "Integration")]
public class MultiContextMigrationHistoryTests
{
    private readonly PostgreSqlFixture _fixture;
    private const string MigrationsAssembly = "TenantCore.EntityFramework.IntegrationTests";

    public MultiContextMigrationHistoryTests(PostgreSqlFixture fixture)
    {
        _fixture = fixture;
    }

    /// <summary>
    /// Builds a service provider with two DbContext types registered with distinct
    /// per-context migration history tables.
    /// </summary>
    private ServiceProvider BuildDualContextServiceProvider(
        string migrationHistoryTable = "__MigrationMigrations",
        string testHistoryTable = "__TestMigrations")
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
            MigrationsAssembly,
            migrationHistoryTable);

        services.AddTenantDbContextPostgreSql<TestDbContext, string>(
            _fixture.ConnectionString,
            MigrationsAssembly,
            testHistoryTable);

        return services.BuildServiceProvider();
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

    private async Task<List<string>> GetTablesInSchemaAsync(string schemaName)
    {
        var options = new DbContextOptionsBuilder<MigrationTestDbContext>()
            .UseNpgsql(_fixture.ConnectionString)
            .Options;

        await using var context = new MigrationTestDbContext(options);
        await context.Database.OpenConnectionAsync();

        await using var command = context.Database.GetDbConnection().CreateCommand();
        command.CommandText = $@"
            SELECT table_name FROM information_schema.tables
            WHERE table_schema = '{schemaName}'
            AND table_type = 'BASE TABLE'
            ORDER BY table_name";

        var tables = new List<string>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            tables.Add(reader.GetString(0));
        }

        return tables;
    }

    private async Task<List<string>> GetMigrationsFromTableAsync(string schemaName, string historyTable)
    {
        var options = new DbContextOptionsBuilder<MigrationTestDbContext>()
            .UseNpgsql(_fixture.ConnectionString)
            .Options;

        await using var context = new MigrationTestDbContext(options);
        await context.Database.OpenConnectionAsync();

        await using var command = context.Database.GetDbConnection().CreateCommand();
        command.CommandText = $@"
            SELECT ""MigrationId"" FROM ""{schemaName}"".""{historyTable}""
            ORDER BY ""MigrationId""";

        var migrations = new List<string>();
        try
        {
            await using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                migrations.Add(reader.GetString(0));
            }
        }
        catch
        {
            // Table may not exist
        }

        return migrations;
    }

    private async Task DropSchemaAsync(string schemaName)
    {
        try
        {
            var options = new DbContextOptionsBuilder<MigrationTestDbContext>()
                .UseNpgsql(_fixture.ConnectionString)
                .Options;

            await using var context = new MigrationTestDbContext(options);
            await context.Database.OpenConnectionAsync();

            await using var cmd = context.Database.GetDbConnection().CreateCommand();
            cmd.CommandText = $"DROP SCHEMA IF EXISTS \"{schemaName}\" CASCADE";
            await cmd.ExecuteNonQueryAsync();
        }
        catch
        {
            // Ignore cleanup errors
        }
    }

    [Fact]
    public async Task DualContextProvision_ShouldCreateDistinctHistoryTables()
    {
        // Arrange
        await using var sp = BuildDualContextServiceProvider(
            "__ProductHistory", "__TestEntityHistory");
        using var scope = sp.CreateScope();

        var tenantId = $"dual_{Guid.NewGuid():N}"[..15];
        var schemaName = $"tenant_{tenantId}";
        var accessor = scope.ServiceProvider.GetRequiredService<ITenantContextAccessor<string>>();

        try
        {
            // Act - provision schema and run both context migrations
            var tenantContext = new TenantContext<string>(tenantId, schemaName);
            accessor.SetTenantContext(tenantContext);

            var migrationRunner = scope.ServiceProvider
                .GetRequiredService<TenantMigrationRunner<MigrationTestDbContext, string>>();
            await migrationRunner.MigrateTenantAsync(tenantId);

            var testRunner = scope.ServiceProvider
                .GetRequiredService<TenantMigrationRunner<TestDbContext, string>>();
            await testRunner.MigrateTenantAsync(tenantId);

            // Assert - both per-context history tables exist
            Assert.True(
                await TableExistsInSchemaAsync(schemaName, "__ProductHistory"),
                "MigrationTestDbContext history table '__ProductHistory' should exist");
            Assert.True(
                await TableExistsInSchemaAsync(schemaName, "__TestEntityHistory"),
                "TestDbContext history table '__TestEntityHistory' should exist");

            // Assert - default EF history table should NOT exist
            Assert.False(
                await TableExistsInSchemaAsync(schemaName, "__EFMigrationsHistory"),
                "Default __EFMigrationsHistory should NOT exist when per-context tables are configured");
        }
        finally
        {
            accessor.SetTenantContext(null);
            await DropSchemaAsync(schemaName);
        }
    }

    [Fact]
    public async Task DualContextProvision_EachHistoryTableTracksOnlyItsOwnMigrations()
    {
        // Arrange
        await using var sp = BuildDualContextServiceProvider(
            "__ProductHistory", "__TestEntityHistory");
        using var scope = sp.CreateScope();

        var tenantId = $"trk_{Guid.NewGuid():N}"[..15];
        var schemaName = $"tenant_{tenantId}";
        var accessor = scope.ServiceProvider.GetRequiredService<ITenantContextAccessor<string>>();

        try
        {
            // Act
            accessor.SetTenantContext(new TenantContext<string>(tenantId, schemaName));

            var migrationRunner = scope.ServiceProvider
                .GetRequiredService<TenantMigrationRunner<MigrationTestDbContext, string>>();
            await migrationRunner.MigrateTenantAsync(tenantId);

            var testRunner = scope.ServiceProvider
                .GetRequiredService<TenantMigrationRunner<TestDbContext, string>>();
            await testRunner.MigrateTenantAsync(tenantId);

            // Assert - each history table has its own context's migrations
            var productMigrations = await GetMigrationsFromTableAsync(schemaName, "__ProductHistory");
            var testMigrations = await GetMigrationsFromTableAsync(schemaName, "__TestEntityHistory");

            Assert.NotEmpty(productMigrations);
            Assert.NotEmpty(testMigrations);

            // The two sets of migrations should be different (different contexts)
            Assert.NotEqual(productMigrations, testMigrations);

            // MigrationTestDbContext migrations should reference MigrationTestDb
            Assert.All(productMigrations, m =>
                Assert.DoesNotContain("TestDb", m));

            // TestDbContext migrations should reference TestDb
            Assert.All(testMigrations, m =>
                Assert.DoesNotContain("MigrationTestDb", m));
        }
        finally
        {
            accessor.SetTenantContext(null);
            await DropSchemaAsync(schemaName);
        }
    }

    [Fact]
    public async Task DualContextProvision_BothContextsCreateTheirEntityTables()
    {
        // Arrange
        await using var sp = BuildDualContextServiceProvider(
            "__ProductHistory", "__TestEntityHistory");
        using var scope = sp.CreateScope();

        var tenantId = $"ent_{Guid.NewGuid():N}"[..15];
        var schemaName = $"tenant_{tenantId}";
        var accessor = scope.ServiceProvider.GetRequiredService<ITenantContextAccessor<string>>();

        try
        {
            // Act
            accessor.SetTenantContext(new TenantContext<string>(tenantId, schemaName));

            var migrationRunner = scope.ServiceProvider
                .GetRequiredService<TenantMigrationRunner<MigrationTestDbContext, string>>();
            await migrationRunner.MigrateTenantAsync(tenantId);

            var testRunner = scope.ServiceProvider
                .GetRequiredService<TenantMigrationRunner<TestDbContext, string>>();
            await testRunner.MigrateTenantAsync(tenantId);

            // Assert - all entity tables from both contexts exist
            var tables = await GetTablesInSchemaAsync(schemaName);

            // From MigrationTestDbContext
            Assert.Contains("Products", tables);
            Assert.Contains("Categories", tables);

            // From TestDbContext
            Assert.Contains("TestEntities", tables);

            // Both history tables
            Assert.Contains("__ProductHistory", tables);
            Assert.Contains("__TestEntityHistory", tables);
        }
        finally
        {
            accessor.SetTenantContext(null);
            await DropSchemaAsync(schemaName);
        }
    }

    [Fact]
    public async Task DualContextProvision_DataIsolationAcrossContextsAndTenants()
    {
        // Arrange
        await using var sp = BuildDualContextServiceProvider(
            "__ProductHistory", "__TestEntityHistory");

        var tenants = Enumerable.Range(1, 3)
            .Select(i => $"iso{i}_{Guid.NewGuid():N}"[..15])
            .ToList();

        try
        {
            // Provision all tenants with both contexts (fresh scope per tenant)
            foreach (var tenantId in tenants)
            {
                using var migrationScope = sp.CreateScope();
                var schemaName = $"tenant_{tenantId}";
                var accessor = migrationScope.ServiceProvider.GetRequiredService<ITenantContextAccessor<string>>();
                accessor.SetTenantContext(new TenantContext<string>(tenantId, schemaName));

                var migrationRunner = migrationScope.ServiceProvider
                    .GetRequiredService<TenantMigrationRunner<MigrationTestDbContext, string>>();
                await migrationRunner.MigrateTenantAsync(tenantId);

                var testRunner = migrationScope.ServiceProvider
                    .GetRequiredService<TenantMigrationRunner<TestDbContext, string>>();
                await testRunner.MigrateTenantAsync(tenantId);
            }

            // Insert data into each tenant using both contexts
            foreach (var tenantId in tenants)
            {
                using var dataScope = sp.CreateScope();
                var schemaName = $"tenant_{tenantId}";
                var accessor = dataScope.ServiceProvider.GetRequiredService<ITenantContextAccessor<string>>();
                accessor.SetTenantContext(new TenantContext<string>(tenantId, schemaName));

                var productContextFactory = dataScope.ServiceProvider
                    .GetRequiredService<IDbContextFactory<MigrationTestDbContext>>();
                await using var productCtx = await productContextFactory.CreateDbContextAsync();
                productCtx.Products.Add(new Product
                {
                    Name = $"Product_for_{tenantId}",
                    Price = 10.00m
                });
                await productCtx.SaveChangesAsync();

                var testContextFactory = dataScope.ServiceProvider
                    .GetRequiredService<IDbContextFactory<TestDbContext>>();
                await using var testCtx = await testContextFactory.CreateDbContextAsync();
                testCtx.TestEntities.Add(new TestEntity
                {
                    Name = $"Entity_for_{tenantId}"
                });
                await testCtx.SaveChangesAsync();
            }

            // Assert - each tenant sees only its own data in both contexts
            foreach (var tenantId in tenants)
            {
                using var readScope = sp.CreateScope();
                var schemaName = $"tenant_{tenantId}";
                var accessor = readScope.ServiceProvider.GetRequiredService<ITenantContextAccessor<string>>();
                accessor.SetTenantContext(new TenantContext<string>(tenantId, schemaName));

                var productContextFactory = readScope.ServiceProvider
                    .GetRequiredService<IDbContextFactory<MigrationTestDbContext>>();
                await using var productCtx = await productContextFactory.CreateDbContextAsync();
                var products = await productCtx.Products.ToListAsync();
                Assert.Single(products);
                Assert.Equal($"Product_for_{tenantId}", products[0].Name);

                var testContextFactory = readScope.ServiceProvider
                    .GetRequiredService<IDbContextFactory<TestDbContext>>();
                await using var testCtx = await testContextFactory.CreateDbContextAsync();
                var entities = await testCtx.TestEntities.ToListAsync();
                Assert.Single(entities);
                Assert.Equal($"Entity_for_{tenantId}", entities[0].Name);
            }
        }
        finally
        {
            foreach (var tenantId in tenants)
            {
                await DropSchemaAsync($"tenant_{tenantId}");
            }
        }
    }

    [Fact]
    public async Task ThreeTenants_EachTenantHasBothHistoryTablesIndependently()
    {
        // Arrange
        await using var sp = BuildDualContextServiceProvider(
            "__ProductHistory", "__TestEntityHistory");

        var tenants = Enumerable.Range(1, 3)
            .Select(i => $"mht{i}_{Guid.NewGuid():N}"[..15])
            .ToList();

        try
        {
            // Act - provision all tenants with both contexts
            // Use a fresh scope per tenant to avoid stale scoped state
            foreach (var tenantId in tenants)
            {
                using var scope = sp.CreateScope();
                var schemaName = $"tenant_{tenantId}";
                var accessor = scope.ServiceProvider.GetRequiredService<ITenantContextAccessor<string>>();
                accessor.SetTenantContext(new TenantContext<string>(tenantId, schemaName));

                var migrationRunner = scope.ServiceProvider
                    .GetRequiredService<TenantMigrationRunner<MigrationTestDbContext, string>>();
                await migrationRunner.MigrateTenantAsync(tenantId);

                var testRunner = scope.ServiceProvider
                    .GetRequiredService<TenantMigrationRunner<TestDbContext, string>>();
                await testRunner.MigrateTenantAsync(tenantId);
            }

            // Assert - each tenant has both history tables with correct migrations
            foreach (var tenantId in tenants)
            {
                var schemaName = $"tenant_{tenantId}";

                // Both history tables exist
                Assert.True(
                    await TableExistsInSchemaAsync(schemaName, "__ProductHistory"),
                    $"Tenant {tenantId}: __ProductHistory should exist");
                Assert.True(
                    await TableExistsInSchemaAsync(schemaName, "__TestEntityHistory"),
                    $"Tenant {tenantId}: __TestEntityHistory should exist");

                // No default history table
                Assert.False(
                    await TableExistsInSchemaAsync(schemaName, "__EFMigrationsHistory"),
                    $"Tenant {tenantId}: __EFMigrationsHistory should NOT exist");

                // Each table has migrations recorded
                var productMigrations = await GetMigrationsFromTableAsync(schemaName, "__ProductHistory");
                var testMigrations = await GetMigrationsFromTableAsync(schemaName, "__TestEntityHistory");

                Assert.NotEmpty(productMigrations);
                Assert.NotEmpty(testMigrations);
            }

            // Verify migration counts are consistent across all tenants
            var firstTenantSchema = $"tenant_{tenants[0]}";
            var expectedProductMigrations = await GetMigrationsFromTableAsync(firstTenantSchema, "__ProductHistory");
            var expectedTestMigrations = await GetMigrationsFromTableAsync(firstTenantSchema, "__TestEntityHistory");

            foreach (var tenantId in tenants.Skip(1))
            {
                var schemaName = $"tenant_{tenantId}";

                var productMigrations = await GetMigrationsFromTableAsync(schemaName, "__ProductHistory");
                var testMigrations = await GetMigrationsFromTableAsync(schemaName, "__TestEntityHistory");

                Assert.Equal(expectedProductMigrations, productMigrations);
                Assert.Equal(expectedTestMigrations, testMigrations);
            }
        }
        finally
        {
            foreach (var tenantId in tenants)
            {
                await DropSchemaAsync($"tenant_{tenantId}");
            }
        }
    }

    [Fact]
    public async Task DualContextMigration_IsIdempotent()
    {
        // Arrange
        await using var sp = BuildDualContextServiceProvider(
            "__ProductHistory", "__TestEntityHistory");
        using var scope = sp.CreateScope();

        var tenantId = $"idm_{Guid.NewGuid():N}"[..15];
        var schemaName = $"tenant_{tenantId}";
        var accessor = scope.ServiceProvider.GetRequiredService<ITenantContextAccessor<string>>();

        try
        {
            accessor.SetTenantContext(new TenantContext<string>(tenantId, schemaName));

            var migrationRunner = scope.ServiceProvider
                .GetRequiredService<TenantMigrationRunner<MigrationTestDbContext, string>>();
            var testRunner = scope.ServiceProvider
                .GetRequiredService<TenantMigrationRunner<TestDbContext, string>>();

            // Act - run migrations twice
            await migrationRunner.MigrateTenantAsync(tenantId);
            await testRunner.MigrateTenantAsync(tenantId);

            var productMigrationsFirst = await GetMigrationsFromTableAsync(schemaName, "__ProductHistory");
            var testMigrationsFirst = await GetMigrationsFromTableAsync(schemaName, "__TestEntityHistory");

            // Run again
            await migrationRunner.MigrateTenantAsync(tenantId);
            await testRunner.MigrateTenantAsync(tenantId);

            var productMigrationsSecond = await GetMigrationsFromTableAsync(schemaName, "__ProductHistory");
            var testMigrationsSecond = await GetMigrationsFromTableAsync(schemaName, "__TestEntityHistory");

            // Assert - no duplicate migrations
            Assert.Equal(productMigrationsFirst, productMigrationsSecond);
            Assert.Equal(testMigrationsFirst, testMigrationsSecond);
        }
        finally
        {
            accessor.SetTenantContext(null);
            await DropSchemaAsync(schemaName);
        }
    }

    [Fact]
    public async Task PerContextOptions_TakePrecedenceOverGlobalOptions()
    {
        // Arrange - set a global history table but override per-context
        var services = new ServiceCollection();

        services.AddLogging(builder => builder.AddConsole().SetMinimumLevel(LogLevel.Debug));

        services.AddTenantCore<string>(options =>
        {
            options.UseConnectionString(_fixture.ConnectionString);
            options.UseSchemaPerTenant(schema =>
            {
                schema.SchemaPrefix = "tenant_";
            });
            options.ConfigureMigrations(migrations =>
            {
                migrations.MigrationHistoryTable = "__GlobalDefault";
            });
        });

        // Register with per-context overrides (should take precedence over __GlobalDefault)
        services.AddTenantDbContextPostgreSql<MigrationTestDbContext, string>(
            _fixture.ConnectionString,
            MigrationsAssembly,
            "__PerContextProduct");

        services.AddTenantDbContextPostgreSql<TestDbContext, string>(
            _fixture.ConnectionString,
            MigrationsAssembly,
            "__PerContextTest");

        await using var sp = services.BuildServiceProvider();
        using var scope = sp.CreateScope();

        var tenantId = $"prec_{Guid.NewGuid():N}"[..15];
        var schemaName = $"tenant_{tenantId}";
        var accessor = scope.ServiceProvider.GetRequiredService<ITenantContextAccessor<string>>();

        try
        {
            accessor.SetTenantContext(new TenantContext<string>(tenantId, schemaName));

            var migrationRunner = scope.ServiceProvider
                .GetRequiredService<TenantMigrationRunner<MigrationTestDbContext, string>>();
            await migrationRunner.MigrateTenantAsync(tenantId);

            var testRunner = scope.ServiceProvider
                .GetRequiredService<TenantMigrationRunner<TestDbContext, string>>();
            await testRunner.MigrateTenantAsync(tenantId);

            // Assert - per-context tables exist, global default does NOT
            Assert.True(
                await TableExistsInSchemaAsync(schemaName, "__PerContextProduct"),
                "Per-context product history table should exist");
            Assert.True(
                await TableExistsInSchemaAsync(schemaName, "__PerContextTest"),
                "Per-context test history table should exist");
            Assert.False(
                await TableExistsInSchemaAsync(schemaName, "__GlobalDefault"),
                "Global default history table should NOT exist when per-context overrides are set");
        }
        finally
        {
            accessor.SetTenantContext(null);
            await DropSchemaAsync(schemaName);
        }
    }
}
