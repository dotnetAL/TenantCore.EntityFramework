using FluentAssertions;
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
/// 1. Multiple tenant schemas can be provisioned
/// 2. Tables are created correctly in each schema
/// 3. Data written to one tenant's schema is completely isolated from other tenants
/// </summary>
[Collection("PostgreSql")]
[Trait("Category", "Integration")]
public class TenantIsolationTests
{
    private readonly PostgreSqlFixture _fixture;

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

        services.AddTenantDbContextPostgreSql<TestDbContext, string>(_fixture.ConnectionString);

        return services.BuildServiceProvider();
    }

    /// <summary>
    /// Helper to clean up schemas after a test
    /// </summary>
    private async Task CleanupSchemasAsync(ISchemaManager schemaManager, params string[] tenantIds)
    {
        var options = new DbContextOptionsBuilder<TestDbContext>()
            .UseNpgsql(_fixture.ConnectionString)
            .Options;

        await using var context = new TestDbContext(options);

        foreach (var tenantId in tenantIds)
        {
            try
            {
                var schemaName = $"tenant_{tenantId}";
                if (await schemaManager.SchemaExistsAsync(context, schemaName))
                {
                    await schemaManager.DropSchemaAsync(context, schemaName, cascade: true);
                }

                // Also try to clean up archived schemas
                var archivedSchemaName = $"archived_tenant_{tenantId}";
                if (await schemaManager.SchemaExistsAsync(context, archivedSchemaName))
                {
                    await schemaManager.DropSchemaAsync(context, archivedSchemaName, cascade: true);
                }
            }
            catch
            {
                // Ignore cleanup errors
            }
        }
    }

    /// <summary>
    /// Helper method to provision a tenant and create tables in its schema.
    /// Creates a fresh DbContext with the correct schema to avoid EF model caching issues.
    /// </summary>
    private async Task ProvisionTenantWithTablesAsync(
        ISchemaManager schemaManager,
        string tenantId)
    {
        var schemaName = $"tenant_{tenantId}";

        // Create the schema first
        var adminOptions = new DbContextOptionsBuilder<TestDbContext>()
            .UseNpgsql(_fixture.ConnectionString)
            .Options;

        await using var adminContext = new TestDbContext(adminOptions);
        await schemaManager.CreateSchemaAsync(adminContext, schemaName);

        // Create tables using raw SQL to avoid EF model caching issues
        // The model cache means HasDefaultSchema is only called once per options instance
        var createTableSql = $@"
            CREATE TABLE IF NOT EXISTS ""{schemaName}"".""TestEntities"" (
                ""Id"" SERIAL PRIMARY KEY,
                ""Name"" VARCHAR(200) NOT NULL,
                ""CreatedAt"" TIMESTAMP WITH TIME ZONE NOT NULL DEFAULT NOW()
            )";
#pragma warning disable EF1002 // Schema name is validated, not user input
        await adminContext.Database.ExecuteSqlRawAsync(createTableSql);
#pragma warning restore EF1002
    }

    [Fact]
    public async Task ProvisionMultipleTenants_ShouldCreateSeparateSchemas()
    {
        // Arrange
        await using var sp = BuildServiceProvider();
        using var scope = sp.CreateScope();
        var schemaManager = scope.ServiceProvider.GetRequiredService<ISchemaManager>();

        var tenant1 = $"t1_{Guid.NewGuid():N}"[..15];
        var tenant2 = $"t2_{Guid.NewGuid():N}"[..15];

        try
        {
            // Act
            await ProvisionTenantWithTablesAsync(schemaManager, tenant1);
            await ProvisionTenantWithTablesAsync(schemaManager, tenant2);

            // Assert - Verify schemas exist
            var options = new DbContextOptionsBuilder<TestDbContext>()
                .UseNpgsql(_fixture.ConnectionString)
                .Options;

            await using var context = new TestDbContext(options);
            var schema1Exists = await schemaManager.SchemaExistsAsync(context, $"tenant_{tenant1}");
            var schema2Exists = await schemaManager.SchemaExistsAsync(context, $"tenant_{tenant2}");

            schema1Exists.Should().BeTrue("tenant1 schema should exist in database");
            schema2Exists.Should().BeTrue("tenant2 schema should exist in database");
        }
        finally
        {
            await CleanupSchemasAsync(schemaManager, tenant1, tenant2);
        }
    }

    [Fact]
    public async Task ProvisionTenant_ShouldCreateTablesInTenantSchema()
    {
        // Arrange
        await using var sp = BuildServiceProvider();
        using var scope = sp.CreateScope();
        var schemaManager = scope.ServiceProvider.GetRequiredService<ISchemaManager>();
        var tenantContextAccessor = scope.ServiceProvider.GetRequiredService<ITenantContextAccessor<string>>();
        var contextFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<TestDbContext>>();

        var tenantId = $"mig_{Guid.NewGuid():N}"[..15];

        try
        {
            // Act
            await ProvisionTenantWithTablesAsync(schemaManager, tenantId);

            // Assert - Set tenant context and verify we can query
            var schemaName = $"tenant_{tenantId}";
            tenantContextAccessor.SetTenantContext(new TenantContext<string>(tenantId, schemaName));

            try
            {
                await using var context = await contextFactory.CreateDbContextAsync();

                // If no exception is thrown and we can query, the tables were created
                var canQuery = await context.TestEntities.AnyAsync();
                canQuery.Should().BeFalse("newly created tenant should have no data");
            }
            finally
            {
                tenantContextAccessor.SetTenantContext(null);
            }
        }
        finally
        {
            await CleanupSchemasAsync(schemaManager, tenantId);
        }
    }

    [Fact]
    public async Task TenantDataIsolation_DataInOneTenant_ShouldNotBeVisibleToOtherTenant()
    {
        // Arrange
        await using var sp = BuildServiceProvider();
        using var scope = sp.CreateScope();
        var schemaManager = scope.ServiceProvider.GetRequiredService<ISchemaManager>();
        var tenantContextAccessor = scope.ServiceProvider.GetRequiredService<ITenantContextAccessor<string>>();
        var contextFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<TestDbContext>>();

        var tenantA = $"tA_{Guid.NewGuid():N}"[..15];
        var tenantB = $"tB_{Guid.NewGuid():N}"[..15];

        try
        {
            // Provision both tenants
            await ProvisionTenantWithTablesAsync(schemaManager, tenantA);
            await ProvisionTenantWithTablesAsync(schemaManager, tenantB);

            // Act - Write data to Tenant A
            var schemaA = $"tenant_{tenantA}";
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
            var schemaB = $"tenant_{tenantB}";
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

                tenantAData.Should().HaveCount(1);
                tenantAData.Should().ContainSingle(e => e.Name == "Tenant A Data");
                tenantAData.Should().NotContain(e => e.Name == "Tenant B Data");
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

                tenantBData.Should().HaveCount(1);
                tenantBData.Should().ContainSingle(e => e.Name == "Tenant B Data");
                tenantBData.Should().NotContain(e => e.Name == "Tenant A Data");
            }
            finally
            {
                tenantContextAccessor.SetTenantContext(null);
            }
        }
        finally
        {
            await CleanupSchemasAsync(schemaManager, tenantA, tenantB);
        }
    }

    [Fact]
    public async Task MultipleTenantsProvisioning_ShouldCreateIsolatedSchemasWithTables()
    {
        // Arrange
        await using var sp = BuildServiceProvider();
        using var scope = sp.CreateScope();
        var schemaManager = scope.ServiceProvider.GetRequiredService<ISchemaManager>();
        var tenantContextAccessor = scope.ServiceProvider.GetRequiredService<ITenantContextAccessor<string>>();
        var contextFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<TestDbContext>>();

        var tenants = Enumerable.Range(1, 3)
            .Select(i => $"m{i}_{Guid.NewGuid():N}"[..15])
            .ToList();

        try
        {
            // Act - Provision all tenants with tables
            foreach (var tenant in tenants)
            {
                await ProvisionTenantWithTablesAsync(schemaManager, tenant);
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
                schemaExists.Should().BeTrue($"schema for tenant {tenant} should exist");

                // Verify we can query the tables in each schema
                tenantContextAccessor.SetTenantContext(new TenantContext<string>(tenant, schemaName));
                try
                {
                    await using var tenantContext = await contextFactory.CreateDbContextAsync();
                    var count = await tenantContext.TestEntities.CountAsync();
                    count.Should().Be(0, $"tenant {tenant} should have empty tables");
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
    public async Task ConcurrentTenantProvisioning_ShouldCreateIsolatedSchemas()
    {
        // Arrange
        await using var sp = BuildServiceProvider();
        var tenants = Enumerable.Range(1, 5)
            .Select(i => $"c{i}_{Guid.NewGuid():N}"[..15])
            .ToList();

        ISchemaManager? schemaManager = null;

        try
        {
            // Act - Provision tenants concurrently
            var tasks = tenants.Select(async tenantId =>
            {
                using var scope = sp.CreateScope();
                var sm = scope.ServiceProvider.GetRequiredService<ISchemaManager>();
                schemaManager ??= sm;

                await ProvisionTenantWithTablesAsync(sm, tenantId);
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
                exists.Should().BeTrue($"tenant {tenant} schema should exist after concurrent provisioning");
            }
        }
        finally
        {
            using var cleanupScope = sp.CreateScope();
            var cleanupSchemaManager = cleanupScope.ServiceProvider.GetRequiredService<ISchemaManager>();
            await CleanupSchemasAsync(cleanupSchemaManager, tenants.ToArray());
        }
    }

    [Fact]
    public async Task TenantDeletion_ShouldRemoveSchemaAndData()
    {
        // Arrange
        await using var sp = BuildServiceProvider();
        using var scope = sp.CreateScope();
        var schemaManager = scope.ServiceProvider.GetRequiredService<ISchemaManager>();
        var tenantContextAccessor = scope.ServiceProvider.GetRequiredService<ITenantContextAccessor<string>>();
        var contextFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<TestDbContext>>();

        var tenantId = $"del_{Guid.NewGuid():N}"[..15];
        var schemaName = $"tenant_{tenantId}";

        // Provision and add data
        await ProvisionTenantWithTablesAsync(schemaManager, tenantId);

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

        // Act - Delete the schema
        var options = new DbContextOptionsBuilder<TestDbContext>()
            .UseNpgsql(_fixture.ConnectionString)
            .Options;

        await using var adminContext = new TestDbContext(options);
        await schemaManager.DropSchemaAsync(adminContext, schemaName, cascade: true);

        // Assert - Schema should no longer exist
        var exists = await schemaManager.SchemaExistsAsync(adminContext, schemaName);
        exists.Should().BeFalse("schema should not exist after deletion");
    }

    [Fact]
    public async Task TenantArchiveAndRestore_ShouldPreserveData()
    {
        // Arrange
        await using var sp = BuildServiceProvider();
        using var scope = sp.CreateScope();
        var schemaManager = scope.ServiceProvider.GetRequiredService<ISchemaManager>();
        var tenantContextAccessor = scope.ServiceProvider.GetRequiredService<ITenantContextAccessor<string>>();
        var contextFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<TestDbContext>>();

        var tenantId = $"arc_{Guid.NewGuid():N}"[..15];
        var schemaName = $"tenant_{tenantId}";
        var archivedSchemaName = $"archived_{schemaName}";

        try
        {
            // Provision and add data
            await ProvisionTenantWithTablesAsync(schemaManager, tenantId);

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

            // Act - Archive the tenant by renaming schema
            var options = new DbContextOptionsBuilder<TestDbContext>()
                .UseNpgsql(_fixture.ConnectionString)
                .Options;

            await using var adminContext = new TestDbContext(options);
            await schemaManager.RenameSchemaAsync(adminContext, schemaName, archivedSchemaName);

            // Assert - Original schema should not exist
            var existsAfterArchive = await schemaManager.SchemaExistsAsync(adminContext, schemaName);
            existsAfterArchive.Should().BeFalse("original schema should not exist after archive");

            var archivedExists = await schemaManager.SchemaExistsAsync(adminContext, archivedSchemaName);
            archivedExists.Should().BeTrue("archived schema should exist");

            // Act - Restore the tenant
            await schemaManager.RenameSchemaAsync(adminContext, archivedSchemaName, schemaName);

            // Assert - Original schema should exist again and data should be preserved
            var existsAfterRestore = await schemaManager.SchemaExistsAsync(adminContext, schemaName);
            existsAfterRestore.Should().BeTrue("schema should exist after restore");

            tenantContextAccessor.SetTenantContext(new TenantContext<string>(tenantId, schemaName));
            try
            {
                await using var context = await contextFactory.CreateDbContextAsync();
                var data = await context.TestEntities.ToListAsync();
                data.Should().ContainSingle(e => e.Name == "Archived Data");
            }
            finally
            {
                tenantContextAccessor.SetTenantContext(null);
            }
        }
        finally
        {
            await CleanupSchemasAsync(schemaManager, tenantId);
        }
    }

    [Fact]
    public async Task CrossTenantQueries_ShouldNotLeak()
    {
        // Arrange
        await using var sp = BuildServiceProvider();
        using var scope = sp.CreateScope();
        var schemaManager = scope.ServiceProvider.GetRequiredService<ISchemaManager>();
        var tenantContextAccessor = scope.ServiceProvider.GetRequiredService<ITenantContextAccessor<string>>();
        var contextFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<TestDbContext>>();

        var tenant1 = $"x1_{Guid.NewGuid():N}"[..15];
        var tenant2 = $"x2_{Guid.NewGuid():N}"[..15];

        try
        {
            await ProvisionTenantWithTablesAsync(schemaManager, tenant1);
            await ProvisionTenantWithTablesAsync(schemaManager, tenant2);

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
                count1.Should().Be(10, "tenant1 should have exactly 10 entities");

                var names1 = await readCtx1.TestEntities.Select(e => e.Name).ToListAsync();
                names1.Should().OnlyContain(n => n.StartsWith("Tenant1_"), "tenant1 should only see tenant1 data");
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
                count2.Should().Be(5, "tenant2 should have exactly 5 entities");

                var names2 = await readCtx2.TestEntities.Select(e => e.Name).ToListAsync();
                names2.Should().OnlyContain(n => n.StartsWith("Tenant2_"), "tenant2 should only see tenant2 data");
            }
            finally
            {
                tenantContextAccessor.SetTenantContext(null);
            }
        }
        finally
        {
            await CleanupSchemasAsync(schemaManager, tenant1, tenant2);
        }
    }

    [Fact]
    public async Task GetSchemas_ShouldReturnAllProvisionedTenantSchemas()
    {
        // Arrange
        await using var sp = BuildServiceProvider();
        using var scope = sp.CreateScope();
        var schemaManager = scope.ServiceProvider.GetRequiredService<ISchemaManager>();

        var uniquePrefix = Guid.NewGuid().ToString("N")[..6];
        var tenants = new[] { $"{uniquePrefix}_a", $"{uniquePrefix}_b", $"{uniquePrefix}_c" };

        try
        {
            foreach (var tenant in tenants)
            {
                await ProvisionTenantWithTablesAsync(schemaManager, tenant);
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
                schemaList.Should().Contain($"tenant_{tenant}", $"schema for tenant {tenant} should be in the list");
            }
        }
        finally
        {
            await CleanupSchemasAsync(schemaManager, tenants);
        }
    }

    [Fact]
    public async Task SwitchingTenantContext_ShouldQueryCorrectSchema()
    {
        // Arrange
        await using var sp = BuildServiceProvider();
        using var scope = sp.CreateScope();
        var schemaManager = scope.ServiceProvider.GetRequiredService<ISchemaManager>();
        var tenantContextAccessor = scope.ServiceProvider.GetRequiredService<ITenantContextAccessor<string>>();
        var contextFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<TestDbContext>>();

        var tenant1 = $"sw1_{Guid.NewGuid():N}"[..15];
        var tenant2 = $"sw2_{Guid.NewGuid():N}"[..15];

        try
        {
            await ProvisionTenantWithTablesAsync(schemaManager, tenant1);
            await ProvisionTenantWithTablesAsync(schemaManager, tenant2);

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
                    data1.Should().NotBeNull();
                    data1!.Name.Should().Be("Tenant1_Unique_Data");
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
                    data2.Should().NotBeNull();
                    data2!.Name.Should().Be("Tenant2_Unique_Data");
                }
                finally
                {
                    tenantContextAccessor.SetTenantContext(null);
                }
            }
        }
        finally
        {
            await CleanupSchemasAsync(schemaManager, tenant1, tenant2);
        }
    }

    [Fact]
    public async Task FiveTenants_WithFiveUniqueRecordsEach_ShouldMaintainCompleteIsolation()
    {
        // Arrange
        await using var sp = BuildServiceProvider();
        using var scope = sp.CreateScope();
        var schemaManager = scope.ServiceProvider.GetRequiredService<ISchemaManager>();
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
            // Provision all 5 tenants
            foreach (var tenant in tenants)
            {
                await ProvisionTenantWithTablesAsync(schemaManager, tenant);
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
                    records.Should().HaveCount(5, $"tenant {tenant} should have exactly 5 records");

                    // All records should belong to this tenant
                    var recordNames = records.Select(r => r.Name).ToList();
                    foreach (var expectedName in expectedDataPerTenant[tenant])
                    {
                        recordNames.Should().Contain(expectedName,
                            $"tenant {tenant} should contain its own record '{expectedName}'");
                    }

                    // No records from other tenants should be present
                    foreach (var otherTenant in tenants.Where(t => t != tenant))
                    {
                        foreach (var otherTenantRecord in expectedDataPerTenant[otherTenant])
                        {
                            recordNames.Should().NotContain(otherTenantRecord,
                                $"tenant {tenant} should NOT contain record '{otherTenantRecord}' from tenant {otherTenant}");
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

                    foundRecord.Should().BeNull(
                        $"record '{tenant1SpecificRecord}' from tenant {tenant1} should NOT be found in tenant {otherTenant}");

                    // Also verify by name pattern - no records starting with tenant1's prefix
                    var leakedRecords = await context.TestEntities
                        .Where(e => e.Name.StartsWith(tenant1))
                        .ToListAsync();

                    leakedRecords.Should().BeEmpty(
                        $"tenant {otherTenant} should have no records belonging to tenant {tenant1}");
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
}
