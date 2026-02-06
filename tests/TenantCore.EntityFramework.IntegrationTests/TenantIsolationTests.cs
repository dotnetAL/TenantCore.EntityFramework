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
