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
public class MigrationHistoryTableTests
{
    private readonly PostgreSqlFixture _fixture;
    private const string MigrationsAssembly = "TenantCore.EntityFramework.IntegrationTests";

    public MigrationHistoryTableTests(PostgreSqlFixture fixture)
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
    public async Task ProvisionTenant_WithCustomHistoryTable_ShouldCreateCustomTable()
    {
        // Arrange
        await using var sp = BuildServiceProvider(customHistoryTable: "__CustomMigrations");
        using var scope = sp.CreateScope();
        var tenantManager = scope.ServiceProvider.GetRequiredService<ITenantManager<string>>();

        var tenantId = $"cust_{Guid.NewGuid():N}"[..15];

        try
        {
            // Act
            await tenantManager.ProvisionTenantAsync(tenantId);

            // Assert
            var schemaName = $"tenant_{tenantId}";
            var customTableExists = await TableExistsInSchemaAsync(schemaName, "__CustomMigrations");
            var defaultTableExists = await TableExistsInSchemaAsync(schemaName, "__EFMigrationsHistory");

            Assert.True(customTableExists, "Custom migration history table should exist in tenant schema");
            Assert.False(defaultTableExists, "Default __EFMigrationsHistory should NOT exist when custom table is configured");
        }
        finally
        {
            await CleanupTenantsAsync(tenantManager, tenantId);
        }
    }

    [Fact]
    public async Task ProvisionTenant_WithDefaultHistoryTable_ShouldUseEFDefault()
    {
        // Arrange
        await using var sp = BuildServiceProvider();
        using var scope = sp.CreateScope();
        var tenantManager = scope.ServiceProvider.GetRequiredService<ITenantManager<string>>();

        var tenantId = $"dflt_{Guid.NewGuid():N}"[..15];

        try
        {
            // Act
            await tenantManager.ProvisionTenantAsync(tenantId);

            // Assert
            var schemaName = $"tenant_{tenantId}";
            var defaultTableExists = await TableExistsInSchemaAsync(schemaName, "__EFMigrationsHistory");

            Assert.True(defaultTableExists, "__EFMigrationsHistory should exist with default configuration");
        }
        finally
        {
            await CleanupTenantsAsync(tenantManager, tenantId);
        }
    }

    [Fact]
    public async Task MigrateAllTenants_WithCustomHistoryTable_ShouldTrackInCustomTable()
    {
        // Arrange
        await using var sp = BuildServiceProvider(customHistoryTable: "__CustomMigrations");
        using var scope = sp.CreateScope();
        var tenantManager = scope.ServiceProvider.GetRequiredService<ITenantManager<string>>();

        var tenants = Enumerable.Range(1, 3)
            .Select(i => $"cmig{i}_{Guid.NewGuid():N}"[..15])
            .ToList();

        try
        {
            // Act - provision all 3 tenants
            foreach (var tenant in tenants)
            {
                await tenantManager.ProvisionTenantAsync(tenant);
            }

            // Assert - each tenant should have custom history table
            foreach (var tenant in tenants)
            {
                var schemaName = $"tenant_{tenant}";
                var customTableExists = await TableExistsInSchemaAsync(schemaName, "__CustomMigrations");
                Assert.True(customTableExists, $"Tenant {tenant} should have __CustomMigrations table");
            }
        }
        finally
        {
            await CleanupTenantsAsync(tenantManager, tenants.ToArray());
        }
    }
}
