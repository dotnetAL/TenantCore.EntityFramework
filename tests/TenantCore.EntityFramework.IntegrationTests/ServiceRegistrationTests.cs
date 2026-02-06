using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TenantCore.EntityFramework.Abstractions;
using TenantCore.EntityFramework.ControlDb;
using TenantCore.EntityFramework.Extensions;
using TenantCore.EntityFramework.Lifecycle;
using TenantCore.EntityFramework.Migrations;
using TenantCore.EntityFramework.PostgreSql;
using TenantCore.EntityFramework.Strategies;
using Xunit;

namespace TenantCore.EntityFramework.IntegrationTests;

/// <summary>
/// Tests that verify DI service registration works correctly,
/// including service lifetime compatibility.
/// </summary>
[Trait("Category", "Integration")]
public class ServiceRegistrationTests
{
    private const string TestConnectionString = "Host=localhost;Database=test;Username=test;Password=test";

    [Fact]
    public void ServiceRegistration_WithoutControlDatabase_ShouldBuildSuccessfully()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();

        services.AddTenantCore<string>(options =>
        {
            options.UsePostgreSql(TestConnectionString);
            options.UseSchemaPerTenant();
        });

        services.AddTenantDbContextPostgreSql<TestDbContext, string>(TestConnectionString);

        // Act & Assert - should not throw
        using var provider = services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateScopes = true,
            ValidateOnBuild = true
        });

        // Verify key services are resolvable
        using var scope = provider.CreateScope();
        var strategy = scope.ServiceProvider.GetRequiredService<ITenantStrategy<string>>();
        var manager = scope.ServiceProvider.GetRequiredService<ITenantManager<string>>();

        Assert.NotNull(strategy);
        Assert.NotNull(manager);
        Assert.IsType<SchemaPerTenantStrategy<string>>(strategy);
    }

    [Fact]
    public void ServiceRegistration_WithControlDatabase_ShouldBuildSuccessfully()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();

        services.AddTenantCore<string>(options =>
        {
            options.UsePostgreSql(TestConnectionString);
            options.UseSchemaPerTenant();
        });

        // Add control database (this registers ITenantStore as scoped)
        services.AddTenantControlDatabase(
            dbOptions => dbOptions.UseNpgsql(TestConnectionString),
            options =>
            {
                options.EnableCaching = true;
                options.ApplyMigrationsOnStartup = false; // Don't try to migrate in test
            });

        services.AddTenantDbContextPostgreSql<TestDbContext, string>(TestConnectionString);

        // Act & Assert - should not throw (this was the bug - singleton depending on scoped)
        using var provider = services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateScopes = true,
            ValidateOnBuild = true
        });

        // Verify key services are resolvable within a scope
        using var scope = provider.CreateScope();
        var strategy = scope.ServiceProvider.GetRequiredService<ITenantStrategy<string>>();
        var manager = scope.ServiceProvider.GetRequiredService<ITenantManager<string>>();
        var tenantStore = scope.ServiceProvider.GetRequiredService<ITenantStore>();

        Assert.NotNull(strategy);
        Assert.NotNull(manager);
        Assert.NotNull(tenantStore);
    }

    [Fact]
    public void ServiceRegistration_WithApiKeyResolver_ShouldBuildSuccessfully()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();

        services.AddTenantCore<string>(options =>
        {
            options.UsePostgreSql(TestConnectionString);
            options.UseSchemaPerTenant();
        });

        services.AddTenantControlDatabase(
            dbOptions => dbOptions.UseNpgsql(TestConnectionString),
            options => options.ApplyMigrationsOnStartup = false);

        services.AddApiKeyTenantResolver<string>("X-Api-Key");
        services.AddTenantDbContextPostgreSql<TestDbContext, string>(TestConnectionString);

        // Act & Assert
        using var provider = services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateScopes = true,
            ValidateOnBuild = true
        });

        using var scope = provider.CreateScope();
        var resolvers = scope.ServiceProvider.GetServices<ITenantResolver<string>>();

        Assert.NotEmpty(resolvers);
    }

    [Fact]
    public void ServiceRegistration_MigrationRunner_ShouldBeResolvableWithControlDatabase()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();

        services.AddTenantCore<string>(options =>
        {
            options.UsePostgreSql(TestConnectionString);
            options.UseSchemaPerTenant();
        });

        services.AddTenantControlDatabase(
            dbOptions => dbOptions.UseNpgsql(TestConnectionString),
            options => options.ApplyMigrationsOnStartup = false);

        services.AddTenantDbContextPostgreSql<TestDbContext, string>(TestConnectionString);

        // Act
        using var provider = services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateScopes = true,
            ValidateOnBuild = true
        });

        // Assert - MigrationRunner should be resolvable (it depends on ITenantStrategy)
        using var scope = provider.CreateScope();
        var migrationRunner = scope.ServiceProvider.GetRequiredService<TenantMigrationRunner<TestDbContext, string>>();

        Assert.NotNull(migrationRunner);
    }

    [Fact]
    public void ServiceRegistration_TenantManager_ShouldReceiveTenantStore()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();

        services.AddTenantCore<string>(options =>
        {
            options.UsePostgreSql(TestConnectionString);
            options.UseSchemaPerTenant();
        });

        services.AddTenantControlDatabase(
            dbOptions => dbOptions.UseNpgsql(TestConnectionString),
            options => options.ApplyMigrationsOnStartup = false);

        services.AddTenantDbContextPostgreSql<TestDbContext, string>(TestConnectionString);

        // Act
        using var provider = services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateScopes = true,
            ValidateOnBuild = true
        });

        using var scope = provider.CreateScope();
        var manager = scope.ServiceProvider.GetRequiredService<ITenantManager<string>>();

        // Assert - TenantManager should be the concrete type that accepts ITenantStore
        Assert.IsType<TenantManager<TestDbContext, string>>(manager);
    }
}
