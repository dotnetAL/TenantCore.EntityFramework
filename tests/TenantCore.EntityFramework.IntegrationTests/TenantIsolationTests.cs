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
/// Integration tests verifying tenant isolation through schema-per-tenant strategy.
/// These tests ensure that:
/// 1. Multiple tenant schemas can be provisioned using EF Core migrations
/// 2. Tables are created correctly in each schema via migrations
/// 3. Data written to one tenant's schema is completely isolated from other tenants
/// </summary>
[Collection("PostgreSql")]
[Trait("Category", "Integration")]
public class TenantIsolationTests
{
    private readonly PostgreSqlFixture _fixture;
    private const string MigrationsAssembly = "TenantCore.EntityFramework.IntegrationTests";

    public TenantIsolationTests(PostgreSqlFixture fixture)
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

        services.AddTenantDbContextPostgreSql<TestDbContext, string>(
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
    /// Helper to clean up schemas directly using ISchemaManager (for archived schemas etc)
    /// </summary>
    private async Task CleanupSchemasAsync(ISchemaManager schemaManager, params string[] schemaNames)
    {
        var options = new DbContextOptionsBuilder<TestDbContext>()
            .UseNpgsql(_fixture.ConnectionString)
            .Options;

        await using var context = new TestDbContext(options);

        foreach (var schemaName in schemaNames)
        {
            try
            {
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

    [Fact]
    public async Task ProvisionMultipleTenants_ShouldCreateSeparateSchemas()
    {
        // Arrange
        await using var sp = BuildServiceProvider();
        using var scope = sp.CreateScope();
        var tenantManager = scope.ServiceProvider.GetRequiredService<ITenantManager<string>>();
        var schemaManager = scope.ServiceProvider.GetRequiredService<ISchemaManager>();

        var tenant1 = $"t1_{Guid.NewGuid():N}"[..15];
        var tenant2 = $"t2_{Guid.NewGuid():N}"[..15];

        try
        {
            // Act - Use ITenantManager.ProvisionTenantAsync which applies EF Core migrations
            await tenantManager.ProvisionTenantAsync(tenant1);
            await tenantManager.ProvisionTenantAsync(tenant2);

            // Assert - Verify schemas exist
            var options = new DbContextOptionsBuilder<TestDbContext>()
                .UseNpgsql(_fixture.ConnectionString)
                .Options;

            await using var context = new TestDbContext(options);
            var schema1Exists = await schemaManager.SchemaExistsAsync(context, $"tenant_{tenant1}");
            var schema2Exists = await schemaManager.SchemaExistsAsync(context, $"tenant_{tenant2}");

            Assert.True(schema1Exists, "tenant1 schema should exist in database");
            Assert.True(schema2Exists, "tenant2 schema should exist in database");
        }
        finally
        {
            await CleanupTenantsAsync(tenantManager, tenant1, tenant2);
        }
    }

    [Fact]
    public async Task ProvisionTenant_ShouldCreateTablesInTenantSchema()
    {
        // Arrange
        await using var sp = BuildServiceProvider();
        using var scope = sp.CreateScope();
        var tenantManager = scope.ServiceProvider.GetRequiredService<ITenantManager<string>>();
        var tenantContextAccessor = scope.ServiceProvider.GetRequiredService<ITenantContextAccessor<string>>();
        var contextFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<TestDbContext>>();

        var tenantId = $"mig_{Guid.NewGuid():N}"[..15];

        try
        {
            // Act - Use ITenantManager.ProvisionTenantAsync which applies EF Core migrations
            await tenantManager.ProvisionTenantAsync(tenantId);

            // Assert - Set tenant context and verify we can query
            var schemaName = $"tenant_{tenantId}";
            tenantContextAccessor.SetTenantContext(new TenantContext<string>(tenantId, schemaName));

            try
            {
                await using var context = await contextFactory.CreateDbContextAsync();

                // If no exception is thrown and we can query, the tables were created by migrations
                var canQuery = await context.TestEntities.AnyAsync();
                Assert.False(canQuery, "newly created tenant should have no data");
            }
            finally
            {
                tenantContextAccessor.SetTenantContext(null);
            }
        }
        finally
        {
            await CleanupTenantsAsync(tenantManager, tenantId);
        }
    }

    [Fact]
    public async Task TenantDataIsolation_DataInOneTenant_ShouldNotBeVisibleToOtherTenant()
    {
        // Arrange
        await using var sp = BuildServiceProvider();
        using var scope = sp.CreateScope();
        var tenantManager = scope.ServiceProvider.GetRequiredService<ITenantManager<string>>();
        var tenantContextAccessor = scope.ServiceProvider.GetRequiredService<ITenantContextAccessor<string>>();
        var contextFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<TestDbContext>>();

        // Use lowercase tenant IDs - the library sanitizes/lowercases tenant IDs in schema names
        var tenantA = $"ta_{Guid.NewGuid():N}"[..15];
        var tenantB = $"tb_{Guid.NewGuid():N}"[..15];

        try
        {
            // Provision both tenants using EF Core migrations
            await tenantManager.ProvisionTenantAsync(tenantA);
            await tenantManager.ProvisionTenantAsync(tenantB);

            // Act - Write data to Tenant A
            // Note: Schema names are lowercased by the library's SanitizeTenantId
            var schemaA = $"tenant_{tenantA.ToLowerInvariant()}";
            tenantContextAccessor.SetTenantContext(new TenantContext<string>(tenantA, schemaA));

            try
            {
                await using var contextA = await contextFactory.CreateDbContextAsync();
                contextA.TestEntities.Add(new TestEntity { Name = "Tenant A Data", CreatedAt = DateTime.UtcNow });
                await contextA.SaveChangesAsync();
            }
            finally
            {
                tenantContextAccessor.SetTenantContext(null);
            }

            // Act - Write data to Tenant B
            var schemaB = $"tenant_{tenantB.ToLowerInvariant()}";
            tenantContextAccessor.SetTenantContext(new TenantContext<string>(tenantB, schemaB));

            try
            {
                await using var contextB = await contextFactory.CreateDbContextAsync();
                contextB.TestEntities.Add(new TestEntity { Name = "Tenant B Data", CreatedAt = DateTime.UtcNow });
                await contextB.SaveChangesAsync();
            }
            finally
            {
                tenantContextAccessor.SetTenantContext(null);
            }

            // Assert - Tenant A should only see its own data
            tenantContextAccessor.SetTenantContext(new TenantContext<string>(tenantA, schemaA));
            try
            {
                await using var readContextA = await contextFactory.CreateDbContextAsync();
                var tenantAData = await readContextA.TestEntities.ToListAsync();

                Assert.Single(tenantAData);
                Assert.Contains(tenantAData, e => e.Name == "Tenant A Data");
                Assert.DoesNotContain(tenantAData, e => e.Name == "Tenant B Data");
            }
            finally
            {
                tenantContextAccessor.SetTenantContext(null);
            }

            // Assert - Tenant B should only see its own data
            tenantContextAccessor.SetTenantContext(new TenantContext<string>(tenantB, schemaB));
            try
            {
                await using var readContextB = await contextFactory.CreateDbContextAsync();
                var tenantBData = await readContextB.TestEntities.ToListAsync();

                Assert.Single(tenantBData);
                Assert.Contains(tenantBData, e => e.Name == "Tenant B Data");
                Assert.DoesNotContain(tenantBData, e => e.Name == "Tenant A Data");
            }
            finally
            {
                tenantContextAccessor.SetTenantContext(null);
            }
        }
        finally
        {
            await CleanupTenantsAsync(tenantManager, tenantA, tenantB);
        }
    }

    [Fact]
    public async Task MultipleTenantsProvisioning_ShouldCreateIsolatedSchemasWithTables()
    {
        // Arrange
        await using var sp = BuildServiceProvider();
        using var scope = sp.CreateScope();
        var tenantManager = scope.ServiceProvider.GetRequiredService<ITenantManager<string>>();
        var schemaManager = scope.ServiceProvider.GetRequiredService<ISchemaManager>();
        var tenantContextAccessor = scope.ServiceProvider.GetRequiredService<ITenantContextAccessor<string>>();
        var contextFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<TestDbContext>>();

        var tenants = Enumerable.Range(1, 3)
            .Select(i => $"m{i}_{Guid.NewGuid():N}"[..15])
            .ToList();

        try
        {
            // Act - Provision all tenants using EF Core migrations
            foreach (var tenant in tenants)
            {
                await tenantManager.ProvisionTenantAsync(tenant);
            }

            // Assert - All schemas should exist with queryable tables
            var options = new DbContextOptionsBuilder<TestDbContext>()
                .UseNpgsql(_fixture.ConnectionString)
                .Options;

            await using var context = new TestDbContext(options);

            foreach (var tenant in tenants)
            {
                var schemaName = $"tenant_{tenant}";
                var schemaExists = await schemaManager.SchemaExistsAsync(context, schemaName);
                Assert.True(schemaExists, $"schema for tenant {tenant} should exist");

                // Verify we can query the tables in each schema
                tenantContextAccessor.SetTenantContext(new TenantContext<string>(tenant, schemaName));
                try
                {
                    await using var tenantContext = await contextFactory.CreateDbContextAsync();
                    var count = await tenantContext.TestEntities.CountAsync();
                    Assert.Equal(0, count);
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
    public async Task ConcurrentTenantProvisioning_ShouldCreateIsolatedSchemas()
    {
        // Arrange
        await using var sp = BuildServiceProvider();
        var tenants = Enumerable.Range(1, 5)
            .Select(i => $"c{i}_{Guid.NewGuid():N}"[..15])
            .ToList();

        try
        {
            // Act - Provision tenants concurrently using EF Core migrations
            var tasks = tenants.Select(async tenantId =>
            {
                using var scope = sp.CreateScope();
                var tenantManager = scope.ServiceProvider.GetRequiredService<ITenantManager<string>>();
                await tenantManager.ProvisionTenantAsync(tenantId);
            });

            await Task.WhenAll(tasks);

            // Assert - All tenants should have schemas
            using var assertScope = sp.CreateScope();
            var verifySchemaManager = assertScope.ServiceProvider.GetRequiredService<ISchemaManager>();

            var options = new DbContextOptionsBuilder<TestDbContext>()
                .UseNpgsql(_fixture.ConnectionString)
                .Options;

            await using var context = new TestDbContext(options);

            foreach (var tenant in tenants)
            {
                var schemaName = $"tenant_{tenant}";
                var exists = await verifySchemaManager.SchemaExistsAsync(context, schemaName);
                Assert.True(exists, $"tenant {tenant} schema should exist after concurrent provisioning");
            }
        }
        finally
        {
            using var cleanupScope = sp.CreateScope();
            var cleanupTenantManager = cleanupScope.ServiceProvider.GetRequiredService<ITenantManager<string>>();
            await CleanupTenantsAsync(cleanupTenantManager, tenants.ToArray());
        }
    }

    [Fact]
    public async Task TenantDeletion_ShouldRemoveSchemaAndData()
    {
        // Arrange
        await using var sp = BuildServiceProvider();
        using var scope = sp.CreateScope();
        var tenantManager = scope.ServiceProvider.GetRequiredService<ITenantManager<string>>();
        var schemaManager = scope.ServiceProvider.GetRequiredService<ISchemaManager>();
        var tenantContextAccessor = scope.ServiceProvider.GetRequiredService<ITenantContextAccessor<string>>();
        var contextFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<TestDbContext>>();

        var tenantId = $"del_{Guid.NewGuid():N}"[..15];
        var schemaName = $"tenant_{tenantId}";

        // Provision and add data using EF Core migrations
        await tenantManager.ProvisionTenantAsync(tenantId);

        tenantContextAccessor.SetTenantContext(new TenantContext<string>(tenantId, schemaName));
        try
        {
            await using var context = await contextFactory.CreateDbContextAsync();
            context.TestEntities.Add(new TestEntity { Name = "Test Data" });
            await context.SaveChangesAsync();
        }
        finally
        {
            tenantContextAccessor.SetTenantContext(null);
        }

        // Act - Delete the tenant using ITenantManager
        await tenantManager.DeleteTenantAsync(tenantId, hardDelete: true);

        // Assert - Schema should no longer exist
        var options = new DbContextOptionsBuilder<TestDbContext>()
            .UseNpgsql(_fixture.ConnectionString)
            .Options;

        await using var adminContext = new TestDbContext(options);
        var exists = await schemaManager.SchemaExistsAsync(adminContext, schemaName);
        Assert.False(exists, "schema should not exist after deletion");
    }

    [Fact]
    public async Task TenantArchiveAndRestore_ShouldPreserveData()
    {
        // Arrange
        await using var sp = BuildServiceProvider();
        using var scope = sp.CreateScope();
        var tenantManager = scope.ServiceProvider.GetRequiredService<ITenantManager<string>>();
        var schemaManager = scope.ServiceProvider.GetRequiredService<ISchemaManager>();
        var tenantContextAccessor = scope.ServiceProvider.GetRequiredService<ITenantContextAccessor<string>>();
        var contextFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<TestDbContext>>();

        var tenantId = $"arc_{Guid.NewGuid():N}"[..15];
        var schemaName = $"tenant_{tenantId}";
        var archivedSchemaName = $"archived_{schemaName}";

        try
        {
            // Provision and add data using EF Core migrations
            await tenantManager.ProvisionTenantAsync(tenantId);

            tenantContextAccessor.SetTenantContext(new TenantContext<string>(tenantId, schemaName));
            try
            {
                await using var context = await contextFactory.CreateDbContextAsync();
                context.TestEntities.Add(new TestEntity { Name = "Archived Data" });
                await context.SaveChangesAsync();
            }
            finally
            {
                tenantContextAccessor.SetTenantContext(null);
            }

            // Act - Archive the tenant using ITenantManager
            await tenantManager.ArchiveTenantAsync(tenantId);

            // Assert - Original schema should not exist
            var options = new DbContextOptionsBuilder<TestDbContext>()
                .UseNpgsql(_fixture.ConnectionString)
                .Options;

            await using var adminContext = new TestDbContext(options);
            var existsAfterArchive = await schemaManager.SchemaExistsAsync(adminContext, schemaName);
            Assert.False(existsAfterArchive, "original schema should not exist after archive");

            var archivedExists = await schemaManager.SchemaExistsAsync(adminContext, archivedSchemaName);
            Assert.True(archivedExists, "archived schema should exist");

            // Act - Restore the tenant using ITenantManager
            await tenantManager.RestoreTenantAsync(tenantId);

            // Assert - Original schema should exist again and data should be preserved
            var existsAfterRestore = await schemaManager.SchemaExistsAsync(adminContext, schemaName);
            Assert.True(existsAfterRestore, "schema should exist after restore");

            tenantContextAccessor.SetTenantContext(new TenantContext<string>(tenantId, schemaName));
            try
            {
                await using var context = await contextFactory.CreateDbContextAsync();
                var data = await context.TestEntities.ToListAsync();
                Assert.Single(data);
                Assert.Equal("Archived Data", data[0].Name);
            }
            finally
            {
                tenantContextAccessor.SetTenantContext(null);
            }
        }
        finally
        {
            await CleanupTenantsAsync(tenantManager, tenantId);
            await CleanupSchemasAsync(schemaManager, archivedSchemaName);
        }
    }

    [Fact]
    public async Task CrossTenantQueries_ShouldNotLeak()
    {
        // Arrange
        await using var sp = BuildServiceProvider();
        using var scope = sp.CreateScope();
        var tenantManager = scope.ServiceProvider.GetRequiredService<ITenantManager<string>>();
        var tenantContextAccessor = scope.ServiceProvider.GetRequiredService<ITenantContextAccessor<string>>();
        var contextFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<TestDbContext>>();

        var tenant1 = $"x1_{Guid.NewGuid():N}"[..15];
        var tenant2 = $"x2_{Guid.NewGuid():N}"[..15];

        try
        {
            // Provision using EF Core migrations
            await tenantManager.ProvisionTenantAsync(tenant1);
            await tenantManager.ProvisionTenantAsync(tenant2);

            // Add distinct data to each tenant
            var schema1 = $"tenant_{tenant1}";
            var schema2 = $"tenant_{tenant2}";

            tenantContextAccessor.SetTenantContext(new TenantContext<string>(tenant1, schema1));
            try
            {
                await using var ctx1 = await contextFactory.CreateDbContextAsync();
                for (int i = 0; i < 10; i++)
                {
                    ctx1.TestEntities.Add(new TestEntity { Name = $"Tenant1_Entity_{i}" });
                }
                await ctx1.SaveChangesAsync();
            }
            finally
            {
                tenantContextAccessor.SetTenantContext(null);
            }

            tenantContextAccessor.SetTenantContext(new TenantContext<string>(tenant2, schema2));
            try
            {
                await using var ctx2 = await contextFactory.CreateDbContextAsync();
                for (int i = 0; i < 5; i++)
                {
                    ctx2.TestEntities.Add(new TestEntity { Name = $"Tenant2_Entity_{i}" });
                }
                await ctx2.SaveChangesAsync();
            }
            finally
            {
                tenantContextAccessor.SetTenantContext(null);
            }

            // Assert - Each tenant should only see their own data count
            tenantContextAccessor.SetTenantContext(new TenantContext<string>(tenant1, schema1));
            try
            {
                await using var readCtx1 = await contextFactory.CreateDbContextAsync();
                var count1 = await readCtx1.TestEntities.CountAsync();
                Assert.Equal(10, count1);

                var names1 = await readCtx1.TestEntities.Select(e => e.Name).ToListAsync();
                Assert.All(names1, n => Assert.StartsWith("Tenant1_", n));
            }
            finally
            {
                tenantContextAccessor.SetTenantContext(null);
            }

            tenantContextAccessor.SetTenantContext(new TenantContext<string>(tenant2, schema2));
            try
            {
                await using var readCtx2 = await contextFactory.CreateDbContextAsync();
                var count2 = await readCtx2.TestEntities.CountAsync();
                Assert.Equal(5, count2);

                var names2 = await readCtx2.TestEntities.Select(e => e.Name).ToListAsync();
                Assert.All(names2, n => Assert.StartsWith("Tenant2_", n));
            }
            finally
            {
                tenantContextAccessor.SetTenantContext(null);
            }
        }
        finally
        {
            await CleanupTenantsAsync(tenantManager, tenant1, tenant2);
        }
    }

    [Fact]
    public async Task GetSchemas_ShouldReturnAllProvisionedTenantSchemas()
    {
        // Arrange
        await using var sp = BuildServiceProvider();
        using var scope = sp.CreateScope();
        var tenantManager = scope.ServiceProvider.GetRequiredService<ITenantManager<string>>();
        var schemaManager = scope.ServiceProvider.GetRequiredService<ISchemaManager>();

        var uniquePrefix = Guid.NewGuid().ToString("N")[..6];
        var tenants = new[] { $"{uniquePrefix}_a", $"{uniquePrefix}_b", $"{uniquePrefix}_c" };

        try
        {
            // Provision using EF Core migrations
            foreach (var tenant in tenants)
            {
                await tenantManager.ProvisionTenantAsync(tenant);
            }

            // Act
            var options = new DbContextOptionsBuilder<TestDbContext>()
                .UseNpgsql(_fixture.ConnectionString)
                .Options;

            await using var context = new TestDbContext(options);
            var allSchemas = await schemaManager.GetSchemasAsync(context, "tenant_");
            var schemaList = allSchemas.ToList();

            // Assert
            foreach (var tenant in tenants)
            {
                Assert.Contains($"tenant_{tenant}", schemaList);
            }
        }
        finally
        {
            await CleanupTenantsAsync(tenantManager, tenants);
        }
    }

    [Fact]
    public async Task SwitchingTenantContext_ShouldQueryCorrectSchema()
    {
        // Arrange
        await using var sp = BuildServiceProvider();
        using var scope = sp.CreateScope();
        var tenantManager = scope.ServiceProvider.GetRequiredService<ITenantManager<string>>();
        var tenantContextAccessor = scope.ServiceProvider.GetRequiredService<ITenantContextAccessor<string>>();
        var contextFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<TestDbContext>>();

        var tenant1 = $"sw1_{Guid.NewGuid():N}"[..15];
        var tenant2 = $"sw2_{Guid.NewGuid():N}"[..15];

        try
        {
            // Provision using EF Core migrations
            await tenantManager.ProvisionTenantAsync(tenant1);
            await tenantManager.ProvisionTenantAsync(tenant2);

            var schema1 = $"tenant_{tenant1}";
            var schema2 = $"tenant_{tenant2}";

            // Add data to tenant1
            tenantContextAccessor.SetTenantContext(new TenantContext<string>(tenant1, schema1));
            try
            {
                await using var ctx = await contextFactory.CreateDbContextAsync();
                ctx.TestEntities.Add(new TestEntity { Name = "Tenant1_Unique_Data" });
                await ctx.SaveChangesAsync();
            }
            finally
            {
                tenantContextAccessor.SetTenantContext(null);
            }

            // Add data to tenant2
            tenantContextAccessor.SetTenantContext(new TenantContext<string>(tenant2, schema2));
            try
            {
                await using var ctx = await contextFactory.CreateDbContextAsync();
                ctx.TestEntities.Add(new TestEntity { Name = "Tenant2_Unique_Data" });
                await ctx.SaveChangesAsync();
            }
            finally
            {
                tenantContextAccessor.SetTenantContext(null);
            }

            // Act & Assert - Switch between tenants multiple times and verify correct data
            for (int i = 0; i < 3; i++)
            {
                // Query tenant1
                tenantContextAccessor.SetTenantContext(new TenantContext<string>(tenant1, schema1));
                try
                {
                    await using var ctx1 = await contextFactory.CreateDbContextAsync();
                    var data1 = await ctx1.TestEntities.FirstOrDefaultAsync();
                    Assert.NotNull(data1);
                    Assert.Equal("Tenant1_Unique_Data", data1.Name);
                }
                finally
                {
                    tenantContextAccessor.SetTenantContext(null);
                }

                // Query tenant2
                tenantContextAccessor.SetTenantContext(new TenantContext<string>(tenant2, schema2));
                try
                {
                    await using var ctx2 = await contextFactory.CreateDbContextAsync();
                    var data2 = await ctx2.TestEntities.FirstOrDefaultAsync();
                    Assert.NotNull(data2);
                    Assert.Equal("Tenant2_Unique_Data", data2.Name);
                }
                finally
                {
                    tenantContextAccessor.SetTenantContext(null);
                }
            }
        }
        finally
        {
            await CleanupTenantsAsync(tenantManager, tenant1, tenant2);
        }
    }

    [Fact]
    public async Task FiveTenants_WithFiveUniqueRecordsEach_ShouldMaintainCompleteIsolation()
    {
        // Arrange
        await using var sp = BuildServiceProvider();
        using var scope = sp.CreateScope();
        var tenantManager = scope.ServiceProvider.GetRequiredService<ITenantManager<string>>();
        var tenantContextAccessor = scope.ServiceProvider.GetRequiredService<ITenantContextAccessor<string>>();
        var contextFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<TestDbContext>>();

        // Create 5 tenants with unique identifiers
        var tenants = Enumerable.Range(1, 5)
            .Select(i => $"iso{i}_{Guid.NewGuid():N}"[..15])
            .ToList();

        // Track expected data per tenant for verification
        var expectedDataPerTenant = new Dictionary<string, List<string>>();

        try
        {
            // Provision all 5 tenants using EF Core migrations
            foreach (var tenant in tenants)
            {
                await tenantManager.ProvisionTenantAsync(tenant);
                expectedDataPerTenant[tenant] = new List<string>();
            }

            // Add 5 unique records to each tenant
            foreach (var tenant in tenants)
            {
                var schemaName = $"tenant_{tenant}";
                tenantContextAccessor.SetTenantContext(new TenantContext<string>(tenant, schemaName));

                try
                {
                    await using var context = await contextFactory.CreateDbContextAsync();

                    for (int i = 1; i <= 5; i++)
                    {
                        var recordName = $"{tenant}_Record_{i}_{Guid.NewGuid():N}"[..30];
                        context.TestEntities.Add(new TestEntity
                        {
                            Name = recordName,
                            CreatedAt = DateTime.UtcNow
                        });
                        expectedDataPerTenant[tenant].Add(recordName);
                    }

                    await context.SaveChangesAsync();
                }
                finally
                {
                    tenantContextAccessor.SetTenantContext(null);
                }
            }

            // VERIFY: Each tenant has exactly their 5 unique records
            foreach (var tenant in tenants)
            {
                var schemaName = $"tenant_{tenant}";
                tenantContextAccessor.SetTenantContext(new TenantContext<string>(tenant, schemaName));

                try
                {
                    await using var context = await contextFactory.CreateDbContextAsync();
                    var records = await context.TestEntities.ToListAsync();

                    // Should have exactly 5 records
                    Assert.Equal(5, records.Count);

                    // All records should belong to this tenant
                    var recordNames = records.Select(r => r.Name).ToList();
                    foreach (var expectedName in expectedDataPerTenant[tenant])
                    {
                        Assert.Contains(expectedName, recordNames);
                    }

                    // No records from other tenants should be present
                    foreach (var otherTenant in tenants.Where(t => t != tenant))
                    {
                        foreach (var otherTenantRecord in expectedDataPerTenant[otherTenant])
                        {
                            Assert.DoesNotContain(otherTenantRecord, recordNames);
                        }
                    }
                }
                finally
                {
                    tenantContextAccessor.SetTenantContext(null);
                }
            }

            // VERIFY: Cross-tenant queries return null/empty for records that exist in other tenants
            // Pick a specific record from tenant 1 and try to find it in all other tenants
            var tenant1 = tenants[0];
            var tenant1SpecificRecord = expectedDataPerTenant[tenant1].First();

            for (int i = 1; i < tenants.Count; i++)
            {
                var otherTenant = tenants[i];
                var otherSchemaName = $"tenant_{otherTenant}";
                tenantContextAccessor.SetTenantContext(new TenantContext<string>(otherTenant, otherSchemaName));

                try
                {
                    await using var context = await contextFactory.CreateDbContextAsync();

                    // Try to find tenant1's specific record in this tenant's context
                    var foundRecord = await context.TestEntities
                        .FirstOrDefaultAsync(e => e.Name == tenant1SpecificRecord);

                    Assert.Null(foundRecord);

                    // Also verify by name pattern - no records starting with tenant1's prefix
                    var leakedRecords = await context.TestEntities
                        .Where(e => e.Name.StartsWith(tenant1))
                        .ToListAsync();

                    Assert.Empty(leakedRecords);
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

    /// <summary>
    /// CRITICAL TEST: Verifies tenant isolation when connections are reused from the pool.
    /// This test specifically targets the connection pooling bug where search_path from a
    /// previous tenant persisted on pooled connections.
    ///
    /// The bug occurs because PostgreSQL connections are pooled, and when a connection is
    /// returned to the pool and then reused by a different tenant's request, the connection
    /// is already open (ConnectionOpened doesn't fire again), so the search_path was never
    /// updated to the new tenant's schema.
    ///
    /// This test rapidly alternates between tenants using the SAME DbContext factory to
    /// maximize the chance of connection pool reuse, which would expose the bug.
    /// </summary>
    [Fact]
    public async Task ConnectionPoolReuse_ShouldMaintainTenantIsolation()
    {
        // Arrange
        await using var sp = BuildServiceProvider();
        using var scope = sp.CreateScope();
        var tenantManager = scope.ServiceProvider.GetRequiredService<ITenantManager<string>>();
        var tenantContextAccessor = scope.ServiceProvider.GetRequiredService<ITenantContextAccessor<string>>();
        var contextFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<TestDbContext>>();

        var tenant1 = $"pool1_{Guid.NewGuid():N}"[..15];
        var tenant2 = $"pool2_{Guid.NewGuid():N}"[..15];

        try
        {
            // Provision both tenants
            await tenantManager.ProvisionTenantAsync(tenant1);
            await tenantManager.ProvisionTenantAsync(tenant2);

            var schema1 = $"tenant_{tenant1}";
            var schema2 = $"tenant_{tenant2}";

            // Add unique data to each tenant
            tenantContextAccessor.SetTenantContext(new TenantContext<string>(tenant1, schema1));
            try
            {
                await using var ctx = await contextFactory.CreateDbContextAsync();
                ctx.TestEntities.Add(new TestEntity { Name = "TENANT1_UNIQUE_DATA" });
                await ctx.SaveChangesAsync();
            }
            finally
            {
                tenantContextAccessor.SetTenantContext(null);
            }

            tenantContextAccessor.SetTenantContext(new TenantContext<string>(tenant2, schema2));
            try
            {
                await using var ctx = await contextFactory.CreateDbContextAsync();
                ctx.TestEntities.Add(new TestEntity { Name = "TENANT2_UNIQUE_DATA" });
                await ctx.SaveChangesAsync();
            }
            finally
            {
                tenantContextAccessor.SetTenantContext(null);
            }

            // Act & Assert - Rapidly alternate between tenants many times
            // This maximizes the chance of connection pool reuse
            var errors = new List<string>();

            for (int iteration = 0; iteration < 50; iteration++)
            {
                // Query tenant1
                tenantContextAccessor.SetTenantContext(new TenantContext<string>(tenant1, schema1));
                try
                {
                    await using var ctx = await contextFactory.CreateDbContextAsync();
                    var data = await ctx.TestEntities.ToListAsync();

                    if (data.Count != 1)
                    {
                        errors.Add($"Iteration {iteration}: Tenant1 expected 1 record, got {data.Count}");
                    }
                    else if (data[0].Name != "TENANT1_UNIQUE_DATA")
                    {
                        errors.Add($"Iteration {iteration}: Tenant1 got wrong data: {data[0].Name}");
                    }
                }
                finally
                {
                    tenantContextAccessor.SetTenantContext(null);
                }

                // Query tenant2 immediately after (maximizes pool reuse chance)
                tenantContextAccessor.SetTenantContext(new TenantContext<string>(tenant2, schema2));
                try
                {
                    await using var ctx = await contextFactory.CreateDbContextAsync();
                    var data = await ctx.TestEntities.ToListAsync();

                    if (data.Count != 1)
                    {
                        errors.Add($"Iteration {iteration}: Tenant2 expected 1 record, got {data.Count}");
                    }
                    else if (data[0].Name != "TENANT2_UNIQUE_DATA")
                    {
                        errors.Add($"Iteration {iteration}: Tenant2 got wrong data: {data[0].Name}");
                    }
                }
                finally
                {
                    tenantContextAccessor.SetTenantContext(null);
                }
            }

            // If any errors occurred, the connection pool was leaking tenant data
            Assert.Empty(errors);
        }
        finally
        {
            await CleanupTenantsAsync(tenantManager, tenant1, tenant2);
        }
    }

    /// <summary>
    /// Tests that parallel requests from different tenants maintain isolation.
    /// This simulates a real web server handling concurrent requests for different tenants.
    /// </summary>
    [Fact]
    public async Task ParallelTenantRequests_ShouldMaintainIsolation()
    {
        // Arrange
        await using var sp = BuildServiceProvider();

        var tenant1 = $"par1_{Guid.NewGuid():N}"[..15];
        var tenant2 = $"par2_{Guid.NewGuid():N}"[..15];
        var tenant3 = $"par3_{Guid.NewGuid():N}"[..15];

        try
        {
            // Provision all tenants
            using (var scope = sp.CreateScope())
            {
                var tenantManager = scope.ServiceProvider.GetRequiredService<ITenantManager<string>>();
                await tenantManager.ProvisionTenantAsync(tenant1);
                await tenantManager.ProvisionTenantAsync(tenant2);
                await tenantManager.ProvisionTenantAsync(tenant3);
            }

            // Add data to each tenant with unique identifiers
            var tenants = new[] { tenant1, tenant2, tenant3 };
            foreach (var tenant in tenants)
            {
                using var scope = sp.CreateScope();
                var tenantContextAccessor = scope.ServiceProvider.GetRequiredService<ITenantContextAccessor<string>>();
                var contextFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<TestDbContext>>();

                var schema = $"tenant_{tenant}";
                tenantContextAccessor.SetTenantContext(new TenantContext<string>(tenant, schema));
                try
                {
                    await using var ctx = await contextFactory.CreateDbContextAsync();
                    for (int i = 0; i < 5; i++)
                    {
                        ctx.TestEntities.Add(new TestEntity { Name = $"{tenant}_record_{i}" });
                    }
                    await ctx.SaveChangesAsync();
                }
                finally
                {
                    tenantContextAccessor.SetTenantContext(null);
                }
            }

            // Act - Query all tenants in parallel, simulating concurrent web requests
            var tasks = tenants.Select(async tenant =>
            {
                var errors = new List<string>();

                // Each "request" uses its own scope (like ASP.NET Core does)
                using var scope = sp.CreateScope();
                var tenantContextAccessor = scope.ServiceProvider.GetRequiredService<ITenantContextAccessor<string>>();
                var contextFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<TestDbContext>>();

                var schema = $"tenant_{tenant}";
                tenantContextAccessor.SetTenantContext(new TenantContext<string>(tenant, schema));
                try
                {
                    await using var ctx = await contextFactory.CreateDbContextAsync();
                    var data = await ctx.TestEntities.ToListAsync();

                    // Verify count
                    if (data.Count != 5)
                    {
                        errors.Add($"Tenant {tenant} expected 5 records, got {data.Count}");
                    }

                    // Verify all records belong to this tenant
                    foreach (var record in data)
                    {
                        if (!record.Name.StartsWith(tenant))
                        {
                            errors.Add($"Tenant {tenant} got record from another tenant: {record.Name}");
                        }
                    }
                }
                finally
                {
                    tenantContextAccessor.SetTenantContext(null);
                }

                return errors;
            });

            var allErrors = await Task.WhenAll(tasks);
            var flatErrors = allErrors.SelectMany(e => e).ToList();

            // Assert
            Assert.Empty(flatErrors);
        }
        finally
        {
            using var cleanupScope = sp.CreateScope();
            var cleanupTenantManager = cleanupScope.ServiceProvider.GetRequiredService<ITenantManager<string>>();
            await CleanupTenantsAsync(cleanupTenantManager, tenant1, tenant2, tenant3);
        }
    }

    /// <summary>
    /// Tests that interleaved read/write operations across tenants maintain isolation.
    /// This catches bugs where write operations might affect the wrong tenant's data.
    /// </summary>
    [Fact]
    public async Task InterleavedReadWriteOperations_ShouldMaintainIsolation()
    {
        // Arrange
        await using var sp = BuildServiceProvider();
        using var scope = sp.CreateScope();
        var tenantManager = scope.ServiceProvider.GetRequiredService<ITenantManager<string>>();
        var tenantContextAccessor = scope.ServiceProvider.GetRequiredService<ITenantContextAccessor<string>>();
        var contextFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<TestDbContext>>();

        var tenant1 = $"rw1_{Guid.NewGuid():N}"[..15];
        var tenant2 = $"rw2_{Guid.NewGuid():N}"[..15];

        try
        {
            // Provision both tenants
            await tenantManager.ProvisionTenantAsync(tenant1);
            await tenantManager.ProvisionTenantAsync(tenant2);

            var schema1 = $"tenant_{tenant1}";
            var schema2 = $"tenant_{tenant2}";

            // Interleaved operations: write to tenant1, write to tenant2, read both
            for (int i = 0; i < 10; i++)
            {
                // Write to tenant1
                tenantContextAccessor.SetTenantContext(new TenantContext<string>(tenant1, schema1));
                try
                {
                    await using var ctx = await contextFactory.CreateDbContextAsync();
                    ctx.TestEntities.Add(new TestEntity { Name = $"T1_Batch{i}" });
                    await ctx.SaveChangesAsync();
                }
                finally
                {
                    tenantContextAccessor.SetTenantContext(null);
                }

                // Write to tenant2
                tenantContextAccessor.SetTenantContext(new TenantContext<string>(tenant2, schema2));
                try
                {
                    await using var ctx = await contextFactory.CreateDbContextAsync();
                    ctx.TestEntities.Add(new TestEntity { Name = $"T2_Batch{i}" });
                    await ctx.SaveChangesAsync();
                }
                finally
                {
                    tenantContextAccessor.SetTenantContext(null);
                }

                // Verify tenant1 only sees its data
                tenantContextAccessor.SetTenantContext(new TenantContext<string>(tenant1, schema1));
                try
                {
                    await using var ctx = await contextFactory.CreateDbContextAsync();
                    var count = await ctx.TestEntities.CountAsync();
                    Assert.Equal(i + 1, count);

                    var hasWrongData = await ctx.TestEntities.AnyAsync(e => e.Name.StartsWith("T2_"));
                    Assert.False(hasWrongData, $"Tenant1 should not see Tenant2 data at iteration {i}");
                }
                finally
                {
                    tenantContextAccessor.SetTenantContext(null);
                }

                // Verify tenant2 only sees its data
                tenantContextAccessor.SetTenantContext(new TenantContext<string>(tenant2, schema2));
                try
                {
                    await using var ctx = await contextFactory.CreateDbContextAsync();
                    var count = await ctx.TestEntities.CountAsync();
                    Assert.Equal(i + 1, count);

                    var hasWrongData = await ctx.TestEntities.AnyAsync(e => e.Name.StartsWith("T1_"));
                    Assert.False(hasWrongData, $"Tenant2 should not see Tenant1 data at iteration {i}");
                }
                finally
                {
                    tenantContextAccessor.SetTenantContext(null);
                }
            }
        }
        finally
        {
            await CleanupTenantsAsync(tenantManager, tenant1, tenant2);
        }
    }

    /// <summary>
    /// Tests that UPDATE and DELETE operations affect only the correct tenant's data.
    /// </summary>
    [Fact]
    public async Task UpdateDeleteOperations_ShouldOnlyAffectCurrentTenant()
    {
        // Arrange
        await using var sp = BuildServiceProvider();
        using var scope = sp.CreateScope();
        var tenantManager = scope.ServiceProvider.GetRequiredService<ITenantManager<string>>();
        var tenantContextAccessor = scope.ServiceProvider.GetRequiredService<ITenantContextAccessor<string>>();
        var contextFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<TestDbContext>>();

        var tenant1 = $"ud1_{Guid.NewGuid():N}"[..15];
        var tenant2 = $"ud2_{Guid.NewGuid():N}"[..15];

        try
        {
            // Provision both tenants
            await tenantManager.ProvisionTenantAsync(tenant1);
            await tenantManager.ProvisionTenantAsync(tenant2);

            var schema1 = $"tenant_{tenant1}";
            var schema2 = $"tenant_{tenant2}";

            // Add identical records to both tenants
            foreach (var (tenant, schema) in new[] { (tenant1, schema1), (tenant2, schema2) })
            {
                tenantContextAccessor.SetTenantContext(new TenantContext<string>(tenant, schema));
                try
                {
                    await using var ctx = await contextFactory.CreateDbContextAsync();
                    ctx.TestEntities.Add(new TestEntity { Name = "SharedName" });
                    ctx.TestEntities.Add(new TestEntity { Name = "ToDelete" });
                    ctx.TestEntities.Add(new TestEntity { Name = "ToUpdate" });
                    await ctx.SaveChangesAsync();
                }
                finally
                {
                    tenantContextAccessor.SetTenantContext(null);
                }
            }

            // Update "ToUpdate" in tenant1 only
            tenantContextAccessor.SetTenantContext(new TenantContext<string>(tenant1, schema1));
            try
            {
                await using var ctx = await contextFactory.CreateDbContextAsync();
                var entity = await ctx.TestEntities.FirstAsync(e => e.Name == "ToUpdate");
                entity.Name = "Updated_By_Tenant1";
                await ctx.SaveChangesAsync();
            }
            finally
            {
                tenantContextAccessor.SetTenantContext(null);
            }

            // Delete "ToDelete" in tenant1 only
            tenantContextAccessor.SetTenantContext(new TenantContext<string>(tenant1, schema1));
            try
            {
                await using var ctx = await contextFactory.CreateDbContextAsync();
                var entity = await ctx.TestEntities.FirstAsync(e => e.Name == "ToDelete");
                ctx.TestEntities.Remove(entity);
                await ctx.SaveChangesAsync();
            }
            finally
            {
                tenantContextAccessor.SetTenantContext(null);
            }

            // Verify tenant1 state
            tenantContextAccessor.SetTenantContext(new TenantContext<string>(tenant1, schema1));
            try
            {
                await using var ctx = await contextFactory.CreateDbContextAsync();
                var data = await ctx.TestEntities.OrderBy(e => e.Name).ToListAsync();

                Assert.Equal(2, data.Count);
                Assert.Contains(data, e => e.Name == "SharedName");
                Assert.Contains(data, e => e.Name == "Updated_By_Tenant1");
                Assert.DoesNotContain(data, e => e.Name == "ToDelete");
                Assert.DoesNotContain(data, e => e.Name == "ToUpdate");
            }
            finally
            {
                tenantContextAccessor.SetTenantContext(null);
            }

            // Verify tenant2 state is UNCHANGED
            tenantContextAccessor.SetTenantContext(new TenantContext<string>(tenant2, schema2));
            try
            {
                await using var ctx = await contextFactory.CreateDbContextAsync();
                var data = await ctx.TestEntities.OrderBy(e => e.Name).ToListAsync();

                Assert.Equal(3, data.Count);
                Assert.Contains(data, e => e.Name == "SharedName");
                Assert.Contains(data, e => e.Name == "ToDelete"); // Still exists!
                Assert.Contains(data, e => e.Name == "ToUpdate"); // Still original!
                Assert.DoesNotContain(data, e => e.Name == "Updated_By_Tenant1");
            }
            finally
            {
                tenantContextAccessor.SetTenantContext(null);
            }
        }
        finally
        {
            await CleanupTenantsAsync(tenantManager, tenant1, tenant2);
        }
    }

    /// <summary>
    /// Tests rapid context switching to catch any race conditions or state leakage.
    /// Uses a high iteration count with minimal delay between operations.
    /// </summary>
    [Fact]
    public async Task RapidContextSwitching_ShouldNeverLeakData()
    {
        // Arrange
        await using var sp = BuildServiceProvider();
        using var scope = sp.CreateScope();
        var tenantManager = scope.ServiceProvider.GetRequiredService<ITenantManager<string>>();
        var tenantContextAccessor = scope.ServiceProvider.GetRequiredService<ITenantContextAccessor<string>>();
        var contextFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<TestDbContext>>();

        var tenants = Enumerable.Range(1, 3)
            .Select(i => $"rapid{i}_{Guid.NewGuid():N}"[..15])
            .ToList();

        try
        {
            // Provision all tenants
            foreach (var tenant in tenants)
            {
                await tenantManager.ProvisionTenantAsync(tenant);
            }

            // Add unique data to each tenant
            foreach (var tenant in tenants)
            {
                var schema = $"tenant_{tenant}";
                tenantContextAccessor.SetTenantContext(new TenantContext<string>(tenant, schema));
                try
                {
                    await using var ctx = await contextFactory.CreateDbContextAsync();
                    ctx.TestEntities.Add(new TestEntity { Name = $"MARKER_{tenant}" });
                    await ctx.SaveChangesAsync();
                }
                finally
                {
                    tenantContextAccessor.SetTenantContext(null);
                }
            }

            // Rapid switching test - 100 iterations
            var random = new Random(42); // Fixed seed for reproducibility
            var errors = new List<string>();

            for (int i = 0; i < 100; i++)
            {
                // Pick a random tenant
                var tenant = tenants[random.Next(tenants.Count)];
                var schema = $"tenant_{tenant}";

                tenantContextAccessor.SetTenantContext(new TenantContext<string>(tenant, schema));
                try
                {
                    await using var ctx = await contextFactory.CreateDbContextAsync();
                    var data = await ctx.TestEntities.ToListAsync();

                    // Should only have 1 record with this tenant's marker
                    if (data.Count != 1)
                    {
                        errors.Add($"Iteration {i}: Tenant {tenant} expected 1 record, got {data.Count}");
                    }
                    else if (data[0].Name != $"MARKER_{tenant}")
                    {
                        errors.Add($"Iteration {i}: Tenant {tenant} got wrong marker: {data[0].Name}");
                    }
                }
                finally
                {
                    tenantContextAccessor.SetTenantContext(null);
                }
            }

            Assert.Empty(errors);
        }
        finally
        {
            await CleanupTenantsAsync(tenantManager, tenants.ToArray());
        }
    }

    /// <summary>
    /// CRITICAL TEST: Verifies that querying without a tenant context does not leak data
    /// from a previously used tenant on the same pooled connection.
    ///
    /// This tests the scenario where:
    /// 1. Request A sets tenant context, queries tenant_a schema
    /// 2. Request B has NO tenant context (e.g., excluded path, or resolution failed with ReturnNull)
    /// 3. Request B should NOT see tenant_a's data even though connection might be reused
    /// </summary>
    [Fact]
    public async Task QueryWithoutTenantContext_ShouldNotLeakPreviousTenantData()
    {
        // Arrange
        await using var sp = BuildServiceProvider();
        using var scope = sp.CreateScope();
        var tenantManager = scope.ServiceProvider.GetRequiredService<ITenantManager<string>>();
        var tenantContextAccessor = scope.ServiceProvider.GetRequiredService<ITenantContextAccessor<string>>();
        var contextFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<TestDbContext>>();

        var tenant1 = $"leak1_{Guid.NewGuid():N}"[..15];

        try
        {
            // Provision tenant and add data
            await tenantManager.ProvisionTenantAsync(tenant1);

            var schema1 = $"tenant_{tenant1}";
            tenantContextAccessor.SetTenantContext(new TenantContext<string>(tenant1, schema1));
            try
            {
                await using var ctx = await contextFactory.CreateDbContextAsync();
                ctx.TestEntities.Add(new TestEntity { Name = "SECRET_TENANT_DATA" });
                await ctx.SaveChangesAsync();
            }
            finally
            {
                tenantContextAccessor.SetTenantContext(null);
            }

            // Verify tenant data exists when context is set
            tenantContextAccessor.SetTenantContext(new TenantContext<string>(tenant1, schema1));
            try
            {
                await using var ctx = await contextFactory.CreateDbContextAsync();
                var count = await ctx.TestEntities.CountAsync();
                Assert.Equal(1, count);
            }
            finally
            {
                tenantContextAccessor.SetTenantContext(null);
            }

            // Act - Query WITHOUT tenant context (simulates request without tenant header)
            // The connection might be reused from the pool with tenant1's search_path
            // but with our fix, search_path should be reset to 'public'
            tenantContextAccessor.SetTenantContext(null);

            // Query multiple times to increase chance of connection pool reuse
            for (int i = 0; i < 10; i++)
            {
                await using var ctx = await contextFactory.CreateDbContextAsync();

                // This query should hit 'public' schema, not tenant schema
                // The table doesn't exist in public schema, so this should either:
                // - Return 0 records (if table exists in public)
                // - Throw an exception (if table doesn't exist in public)
                // Either way, it should NOT return "SECRET_TENANT_DATA"
                try
                {
                    var data = await ctx.TestEntities.ToListAsync();
                    // If we get here, table exists in public - verify no tenant data leaked
                    Assert.DoesNotContain(data, e => e.Name == "SECRET_TENANT_DATA");
                }
                catch (Npgsql.PostgresException ex) when (ex.SqlState == "42P01") // relation does not exist
                {
                    // Expected - table doesn't exist in public schema
                    // This is fine - the important thing is we didn't leak tenant data
                }
            }
        }
        finally
        {
            await CleanupTenantsAsync(tenantManager, tenant1);
        }
    }

    /// <summary>
    /// Tests the scenario where tenant context switches from tenant A to null to tenant B.
    /// This should never allow tenant B to see tenant A's data.
    /// </summary>
    [Fact]
    public async Task TenantContextSwitchThroughNull_ShouldMaintainIsolation()
    {
        // Arrange
        await using var sp = BuildServiceProvider();
        using var scope = sp.CreateScope();
        var tenantManager = scope.ServiceProvider.GetRequiredService<ITenantManager<string>>();
        var tenantContextAccessor = scope.ServiceProvider.GetRequiredService<ITenantContextAccessor<string>>();
        var contextFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<TestDbContext>>();

        var tenant1 = $"null1_{Guid.NewGuid():N}"[..15];
        var tenant2 = $"null2_{Guid.NewGuid():N}"[..15];

        try
        {
            // Provision both tenants
            await tenantManager.ProvisionTenantAsync(tenant1);
            await tenantManager.ProvisionTenantAsync(tenant2);

            var schema1 = $"tenant_{tenant1}";
            var schema2 = $"tenant_{tenant2}";

            // Add data to tenant1
            tenantContextAccessor.SetTenantContext(new TenantContext<string>(tenant1, schema1));
            try
            {
                await using var ctx = await contextFactory.CreateDbContextAsync();
                ctx.TestEntities.Add(new TestEntity { Name = "TENANT1_SECRET" });
                await ctx.SaveChangesAsync();
            }
            finally
            {
                tenantContextAccessor.SetTenantContext(null);
            }

            // Add data to tenant2
            tenantContextAccessor.SetTenantContext(new TenantContext<string>(tenant2, schema2));
            try
            {
                await using var ctx = await contextFactory.CreateDbContextAsync();
                ctx.TestEntities.Add(new TestEntity { Name = "TENANT2_SECRET" });
                await ctx.SaveChangesAsync();
            }
            finally
            {
                tenantContextAccessor.SetTenantContext(null);
            }

            // Act & Assert - Switch through null context pattern multiple times
            for (int i = 0; i < 20; i++)
            {
                // Query tenant1
                tenantContextAccessor.SetTenantContext(new TenantContext<string>(tenant1, schema1));
                try
                {
                    await using var ctx = await contextFactory.CreateDbContextAsync();
                    var data = await ctx.TestEntities.SingleAsync();
                    Assert.Equal("TENANT1_SECRET", data.Name);
                }
                finally
                {
                    tenantContextAccessor.SetTenantContext(null);
                }

                // Clear context (simulates request without tenant)
                tenantContextAccessor.SetTenantContext(null);

                // Query tenant2 - should NOT see tenant1 data
                tenantContextAccessor.SetTenantContext(new TenantContext<string>(tenant2, schema2));
                try
                {
                    await using var ctx = await contextFactory.CreateDbContextAsync();
                    var data = await ctx.TestEntities.SingleAsync();
                    Assert.Equal("TENANT2_SECRET", data.Name);
                }
                finally
                {
                    tenantContextAccessor.SetTenantContext(null);
                }
            }
        }
        finally
        {
            await CleanupTenantsAsync(tenantManager, tenant1, tenant2);
        }
    }

    /// <summary>
    /// COMPREHENSIVE END-TO-END TEST: Provisions 10 tenants, adds data to only 3 of them,
    /// and verifies complete isolation - only those 3 tenants should see data.
    ///
    /// This test validates the entire tenant lifecycle:
    /// 1. Schema creation via ProvisionTenantAsync
    /// 2. Table creation via migrations (not EnsureCreatedAsync in public schema)
    /// 3. Data insertion respects tenant context
    /// 4. Data queries respect tenant context
    /// 5. Empty tenants return no data (not data from other tenants)
    /// </summary>
    [Fact]
    public async Task TenTenants_DataInThree_OthersShouldBeEmpty()
    {
        // Arrange
        await using var sp = BuildServiceProvider();
        using var scope = sp.CreateScope();
        var tenantManager = scope.ServiceProvider.GetRequiredService<ITenantManager<string>>();
        var tenantContextAccessor = scope.ServiceProvider.GetRequiredService<ITenantContextAccessor<string>>();
        var contextFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<TestDbContext>>();

        // Create 10 unique tenant IDs
        var testRunId = Guid.NewGuid().ToString("N")[..8];
        var tenants = Enumerable.Range(1, 10)
            .Select(i => $"t{i}_{testRunId}")
            .ToList();

        // Define which tenants will have data (5, 6, 7 - using 0-based index: 4, 5, 6)
        var tenantsWithData = new[] { tenants[4], tenants[5], tenants[6] }; // tenants 5, 6, 7
        var tenantsWithoutData = tenants.Except(tenantsWithData).ToList();  // tenants 1, 2, 3, 4, 8, 9, 10

        // Track expected data per tenant
        var expectedData = new Dictionary<string, List<string>>();

        try
        {
            // ============================================================
            // STEP 1: Provision all 10 tenants
            // ============================================================
            foreach (var tenant in tenants)
            {
                await tenantManager.ProvisionTenantAsync(tenant);
                expectedData[tenant] = new List<string>();
            }

            // Verify all 10 tenants were created
            var allTenants = (await tenantManager.GetTenantsAsync()).ToList();
            foreach (var tenant in tenants)
            {
                Assert.Contains(tenant, allTenants);
            }

            // ============================================================
            // STEP 2: Add items ONLY to tenants 5, 6, and 7
            // ============================================================

            // Add 3 items to tenant 5 (index 4)
            var tenant5 = tenants[4];
            var schema5 = $"tenant_{tenant5}";
            tenantContextAccessor.SetTenantContext(new TenantContext<string>(tenant5, schema5));
            try
            {
                await using var ctx = await contextFactory.CreateDbContextAsync();
                for (int i = 1; i <= 3; i++)
                {
                    var name = $"Tenant5_Product_{i}";
                    ctx.TestEntities.Add(new TestEntity { Name = name, CreatedAt = DateTime.UtcNow });
                    expectedData[tenant5].Add(name);
                }
                await ctx.SaveChangesAsync();
            }
            finally
            {
                tenantContextAccessor.SetTenantContext(null);
            }

            // Add 5 items to tenant 6 (index 5)
            var tenant6 = tenants[5];
            var schema6 = $"tenant_{tenant6}";
            tenantContextAccessor.SetTenantContext(new TenantContext<string>(tenant6, schema6));
            try
            {
                await using var ctx = await contextFactory.CreateDbContextAsync();
                for (int i = 1; i <= 5; i++)
                {
                    var name = $"Tenant6_Product_{i}";
                    ctx.TestEntities.Add(new TestEntity { Name = name, CreatedAt = DateTime.UtcNow });
                    expectedData[tenant6].Add(name);
                }
                await ctx.SaveChangesAsync();
            }
            finally
            {
                tenantContextAccessor.SetTenantContext(null);
            }

            // Add 2 items to tenant 7 (index 6)
            var tenant7 = tenants[6];
            var schema7 = $"tenant_{tenant7}";
            tenantContextAccessor.SetTenantContext(new TenantContext<string>(tenant7, schema7));
            try
            {
                await using var ctx = await contextFactory.CreateDbContextAsync();
                for (int i = 1; i <= 2; i++)
                {
                    var name = $"Tenant7_Product_{i}";
                    ctx.TestEntities.Add(new TestEntity { Name = name, CreatedAt = DateTime.UtcNow });
                    expectedData[tenant7].Add(name);
                }
                await ctx.SaveChangesAsync();
            }
            finally
            {
                tenantContextAccessor.SetTenantContext(null);
            }

            // ============================================================
            // STEP 3: Verify tenants 5, 6, 7 have their data
            // ============================================================

            // Verify tenant 5 has exactly 3 items
            tenantContextAccessor.SetTenantContext(new TenantContext<string>(tenant5, schema5));
            try
            {
                await using var ctx = await contextFactory.CreateDbContextAsync();
                var data = await ctx.TestEntities.ToListAsync();

                Assert.Equal(3, data.Count);
                foreach (var expectedName in expectedData[tenant5])
                {
                    Assert.Contains(data, e => e.Name == expectedName);
                }
                // Verify no data from other tenants
                Assert.All(data, e => Assert.StartsWith("Tenant5_", e.Name));
            }
            finally
            {
                tenantContextAccessor.SetTenantContext(null);
            }

            // Verify tenant 6 has exactly 5 items
            tenantContextAccessor.SetTenantContext(new TenantContext<string>(tenant6, schema6));
            try
            {
                await using var ctx = await contextFactory.CreateDbContextAsync();
                var data = await ctx.TestEntities.ToListAsync();

                Assert.Equal(5, data.Count);
                foreach (var expectedName in expectedData[tenant6])
                {
                    Assert.Contains(data, e => e.Name == expectedName);
                }
                // Verify no data from other tenants
                Assert.All(data, e => Assert.StartsWith("Tenant6_", e.Name));
            }
            finally
            {
                tenantContextAccessor.SetTenantContext(null);
            }

            // Verify tenant 7 has exactly 2 items
            tenantContextAccessor.SetTenantContext(new TenantContext<string>(tenant7, schema7));
            try
            {
                await using var ctx = await contextFactory.CreateDbContextAsync();
                var data = await ctx.TestEntities.ToListAsync();

                Assert.Equal(2, data.Count);
                foreach (var expectedName in expectedData[tenant7])
                {
                    Assert.Contains(data, e => e.Name == expectedName);
                }
                // Verify no data from other tenants
                Assert.All(data, e => Assert.StartsWith("Tenant7_", e.Name));
            }
            finally
            {
                tenantContextAccessor.SetTenantContext(null);
            }

            // ============================================================
            // STEP 4: Verify all OTHER tenants (1,2,3,4,8,9,10) are EMPTY
            // ============================================================

            foreach (var tenant in tenantsWithoutData)
            {
                var schema = $"tenant_{tenant}";
                tenantContextAccessor.SetTenantContext(new TenantContext<string>(tenant, schema));
                try
                {
                    await using var ctx = await contextFactory.CreateDbContextAsync();
                    var data = await ctx.TestEntities.ToListAsync();

                    // This tenant should have NO data
                    Assert.Empty(data);
                }
                finally
                {
                    tenantContextAccessor.SetTenantContext(null);
                }
            }

            // ============================================================
            // STEP 5: Cross-verification - ensure no data leakage
            // ============================================================

            // Query each tenant and ensure they don't see other tenants' data
            foreach (var tenant in tenants)
            {
                var schema = $"tenant_{tenant}";
                tenantContextAccessor.SetTenantContext(new TenantContext<string>(tenant, schema));
                try
                {
                    await using var ctx = await contextFactory.CreateDbContextAsync();
                    var data = await ctx.TestEntities.ToListAsync();

                    // Check that this tenant doesn't have any data from OTHER tenants
                    foreach (var otherTenant in tenants.Where(t => t != tenant))
                    {
                        var otherTenantPrefix = otherTenant == tenant5 ? "Tenant5_" :
                                                otherTenant == tenant6 ? "Tenant6_" :
                                                otherTenant == tenant7 ? "Tenant7_" : null;

                        if (otherTenantPrefix != null)
                        {
                            Assert.DoesNotContain(data, e => e.Name.StartsWith(otherTenantPrefix));
                        }
                    }
                }
                finally
                {
                    tenantContextAccessor.SetTenantContext(null);
                }
            }
        }
        finally
        {
            // Cleanup all 10 tenants
            await CleanupTenantsAsync(tenantManager, tenants.ToArray());
        }
    }

    /// <summary>
    /// Tests that schemas are actually created in the database and tables exist in tenant schemas,
    /// NOT in the public schema. This directly tests the EnsureCreatedAsync fix.
    /// </summary>
    [Fact]
    public async Task TenantProvisioning_TablesShouldBeInTenantSchema_NotPublicSchema()
    {
        // Arrange
        await using var sp = BuildServiceProvider();
        using var scope = sp.CreateScope();
        var tenantManager = scope.ServiceProvider.GetRequiredService<ITenantManager<string>>();
        var schemaManager = scope.ServiceProvider.GetRequiredService<ISchemaManager>();

        var tenant = $"schematest_{Guid.NewGuid():N}"[..15];
        var schemaName = $"tenant_{tenant}";

        try
        {
            // Act - Provision the tenant
            await tenantManager.ProvisionTenantAsync(tenant);

            // Assert - Query the database directly to verify table locations
            var options = new DbContextOptionsBuilder<TestDbContext>()
                .UseNpgsql(_fixture.ConnectionString)
                .Options;

            await using var context = new TestDbContext(options);
            var connection = context.Database.GetDbConnection();
            await connection.OpenAsync();

            // Check that TestEntities table exists in TENANT schema
            await using var cmd1 = connection.CreateCommand();
            cmd1.CommandText = $@"
                SELECT COUNT(*) FROM information_schema.tables
                WHERE table_schema = '{schemaName}'
                AND table_name = 'TestEntities'";
            var tableInTenantSchema = Convert.ToInt32(await cmd1.ExecuteScalarAsync());
            Assert.Equal(1, tableInTenantSchema);

            // Check that TestEntities table does NOT exist in PUBLIC schema
            // (This would fail if EnsureCreatedAsync bug was present)
            await using var cmd2 = connection.CreateCommand();
            cmd2.CommandText = @"
                SELECT COUNT(*) FROM information_schema.tables
                WHERE table_schema = 'public'
                AND table_name = 'TestEntities'";
            var tableInPublicSchema = Convert.ToInt32(await cmd2.ExecuteScalarAsync());
            Assert.Equal(0, tableInPublicSchema);

            // Check that __EFMigrationsHistory exists in TENANT schema
            await using var cmd3 = connection.CreateCommand();
            cmd3.CommandText = $@"
                SELECT COUNT(*) FROM information_schema.tables
                WHERE table_schema = '{schemaName}'
                AND table_name = '__EFMigrationsHistory'";
            var migrationsInTenantSchema = Convert.ToInt32(await cmd3.ExecuteScalarAsync());
            Assert.Equal(1, migrationsInTenantSchema);
        }
        finally
        {
            await CleanupTenantsAsync(tenantManager, tenant);
        }
    }
}
