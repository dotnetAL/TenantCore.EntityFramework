using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using TenantCore.EntityFramework.Abstractions;
using TenantCore.EntityFramework.Configuration;
using TenantCore.EntityFramework.Context;
using TenantCore.EntityFramework.Events;
using TenantCore.EntityFramework.Migrations;
using TenantCore.EntityFramework.Strategies;

namespace TenantCore.EntityFramework.Lifecycle;

/// <summary>
/// Manages the complete lifecycle of tenants including provisioning, migration, and deletion.
/// </summary>
/// <typeparam name="TContext">The DbContext type.</typeparam>
/// <typeparam name="TKey">The type of the tenant identifier.</typeparam>
public class TenantManager<TContext, TKey> : ITenantManager<TKey>
    where TContext : TenantDbContext<TKey>
    where TKey : notnull
{
    private readonly IServiceProvider _serviceProvider;
    private readonly TenantCoreOptions _options;
    private readonly SchemaPerTenantStrategy<TKey> _strategy;
    private readonly ITenantContextAccessor<TKey> _contextAccessor;
    private readonly TenantMigrationRunner<TContext, TKey> _migrationRunner;
    private readonly ITenantEventPublisher<TKey> _eventPublisher;
    private readonly ILogger<TenantManager<TContext, TKey>> _logger;

    public TenantManager(
        IServiceProvider serviceProvider,
        TenantCoreOptions options,
        SchemaPerTenantStrategy<TKey> strategy,
        ITenantContextAccessor<TKey> contextAccessor,
        TenantMigrationRunner<TContext, TKey> migrationRunner,
        ITenantEventPublisher<TKey> eventPublisher,
        ILogger<TenantManager<TContext, TKey>> logger)
    {
        _serviceProvider = serviceProvider;
        _options = options;
        _strategy = strategy;
        _contextAccessor = contextAccessor;
        _migrationRunner = migrationRunner;
        _eventPublisher = eventPublisher;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task ProvisionTenantAsync(TKey tenantId, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Provisioning tenant {TenantId}", tenantId);

        using var scope = _serviceProvider.CreateScope();
        var contextFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<TContext>>();

        // Use a temporary context for schema creation
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);

        // Create the schema
        await _strategy.ProvisionTenantAsync(context, tenantId, cancellationToken);

        // Apply migrations
        await _migrationRunner.MigrateTenantAsync(tenantId, cancellationToken);

        // Run seeders
        await RunSeedersAsync(tenantId, cancellationToken);

        // Publish event
        await _eventPublisher.PublishTenantCreatedAsync(tenantId, cancellationToken);

        _logger.LogInformation("Successfully provisioned tenant {TenantId}", tenantId);
    }

    /// <inheritdoc />
    public async Task<bool> TenantExistsAsync(TKey tenantId, CancellationToken cancellationToken = default)
    {
        using var scope = _serviceProvider.CreateScope();
        var contextFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<TContext>>();
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);

        return await _strategy.TenantExistsAsync(context, tenantId, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<IEnumerable<string>> GetTenantsAsync(CancellationToken cancellationToken = default)
    {
        using var scope = _serviceProvider.CreateScope();
        var contextFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<TContext>>();
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);

        return await _strategy.GetTenantsAsync(context, cancellationToken);
    }

    /// <inheritdoc />
    public async Task DeleteTenantAsync(TKey tenantId, bool hardDelete = false, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Deleting tenant {TenantId} (hardDelete: {HardDelete})", tenantId, hardDelete);

        using var scope = _serviceProvider.CreateScope();
        var contextFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<TContext>>();
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);

        await _strategy.DeleteTenantAsync(context, tenantId, hardDelete, cancellationToken);

        await _eventPublisher.PublishTenantDeletedAsync(tenantId, hardDelete, cancellationToken);

        _logger.LogInformation("Successfully deleted tenant {TenantId}", tenantId);
    }

    /// <inheritdoc />
    public async Task ArchiveTenantAsync(TKey tenantId, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Archiving tenant {TenantId}", tenantId);

        using var scope = _serviceProvider.CreateScope();
        var contextFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<TContext>>();
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);

        await _strategy.ArchiveTenantAsync(context, tenantId, cancellationToken);

        await _eventPublisher.PublishTenantArchivedAsync(tenantId, cancellationToken);

        _logger.LogInformation("Successfully archived tenant {TenantId}", tenantId);
    }

    /// <inheritdoc />
    public async Task RestoreTenantAsync(TKey tenantId, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Restoring tenant {TenantId}", tenantId);

        using var scope = _serviceProvider.CreateScope();
        var contextFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<TContext>>();
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);

        await _strategy.RestoreTenantAsync(context, tenantId, cancellationToken);

        await _eventPublisher.PublishTenantRestoredAsync(tenantId, cancellationToken);

        _logger.LogInformation("Successfully restored tenant {TenantId}", tenantId);
    }

    /// <inheritdoc />
    public async Task MigrateTenantAsync(TKey tenantId, CancellationToken cancellationToken = default)
    {
        await _migrationRunner.MigrateTenantAsync(tenantId, cancellationToken);
    }

    /// <inheritdoc />
    public async Task MigrateAllTenantsAsync(CancellationToken cancellationToken = default)
    {
        await _migrationRunner.MigrateAllTenantsAsync(cancellationToken);
    }

    private async Task RunSeedersAsync(TKey tenantId, CancellationToken cancellationToken)
    {
        var seeders = _serviceProvider.GetServices<ITenantSeeder<TKey>>()
            .OrderBy(s => s.Order)
            .ToList();

        if (seeders.Count == 0)
        {
            _logger.LogDebug("No tenant seeders registered");
            return;
        }

        _logger.LogDebug("Running {Count} tenant seeders for tenant {TenantId}", seeders.Count, tenantId);

        // Create a scoped context for seeding
        var schemaName = _options.SchemaPerTenant.GenerateSchemaName(tenantId);
        var tenantContext = new TenantContext<TKey>(tenantId, schemaName);
        _contextAccessor.SetTenantContext(tenantContext);

        try
        {
            using var scope = _serviceProvider.CreateScope();
            var contextFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<TContext>>();
            await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);

            foreach (var seeder in seeders)
            {
                _logger.LogDebug("Running seeder {SeederType}", seeder.GetType().Name);
                await seeder.SeedAsync(context, tenantId, cancellationToken);
            }

            await context.SaveChangesAsync(cancellationToken);
        }
        finally
        {
            _contextAccessor.SetTenantContext(null);
        }
    }
}
