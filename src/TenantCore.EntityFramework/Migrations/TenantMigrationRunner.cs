using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using TenantCore.EntityFramework.Abstractions;
using TenantCore.EntityFramework.Configuration;
using TenantCore.EntityFramework.Context;
using TenantCore.EntityFramework.Events;

namespace TenantCore.EntityFramework.Migrations;

/// <summary>
/// Runs migrations across all tenant schemas.
/// </summary>
/// <typeparam name="TContext">The DbContext type.</typeparam>
/// <typeparam name="TKey">The type of the tenant identifier.</typeparam>
public class TenantMigrationRunner<TContext, TKey>
    where TContext : TenantDbContext<TKey>
    where TKey : notnull
{
    private readonly IServiceProvider _serviceProvider;
    private readonly TenantCoreOptions _options;
    private readonly ITenantStrategy<TKey> _strategy;
    private readonly ITenantContextAccessor<TKey> _contextAccessor;
    private readonly ITenantEventPublisher<TKey> _eventPublisher;
    private readonly ILogger<TenantMigrationRunner<TContext, TKey>> _logger;

    public TenantMigrationRunner(
        IServiceProvider serviceProvider,
        TenantCoreOptions options,
        ITenantStrategy<TKey> strategy,
        ITenantContextAccessor<TKey> contextAccessor,
        ITenantEventPublisher<TKey> eventPublisher,
        ILogger<TenantMigrationRunner<TContext, TKey>> logger)
    {
        _serviceProvider = serviceProvider;
        _options = options;
        _strategy = strategy;
        _contextAccessor = contextAccessor;
        _eventPublisher = eventPublisher;
        _logger = logger;
    }

    /// <summary>
    /// Applies pending migrations to all tenant schemas.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task MigrateAllTenantsAsync(CancellationToken cancellationToken = default)
    {
        using var scope = _serviceProvider.CreateScope();
        var contextFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<TContext>>();

        await using var tempContext = await contextFactory.CreateDbContextAsync(cancellationToken);
        var tenantIds = await _strategy.GetTenantsAsync(tempContext, cancellationToken);

        var tenantList = tenantIds.ToList();
        _logger.LogInformation("Starting migration for {Count} tenants", tenantList.Count);

        var migrationOptions = _options.Migrations;
        var semaphore = new SemaphoreSlim(migrationOptions.ParallelMigrations);
        var failures = new List<(string TenantId, Exception Exception)>();

        var tasks = tenantList.Select(async tenantId =>
        {
            await semaphore.WaitAsync(cancellationToken);
            try
            {
                await MigrateTenantInternalAsync(tenantId, cancellationToken);
            }
            catch (Exception ex) when (migrationOptions.FailureBehavior != MigrationFailureBehavior.StopAll)
            {
                lock (failures)
                {
                    failures.Add((tenantId, ex));
                }

                if (migrationOptions.FailureBehavior == MigrationFailureBehavior.Skip)
                {
                    _logger.LogWarning(ex, "Migration failed for tenant {TenantId}, skipping", tenantId);
                }
            }
            finally
            {
                semaphore.Release();
            }
        });

        await Task.WhenAll(tasks);

        if (failures.Count > 0)
        {
            _logger.LogError("Migration completed with {FailureCount} failures out of {TotalCount} tenants",
                failures.Count, tenantList.Count);

            if (migrationOptions.FailureBehavior == MigrationFailureBehavior.ContinueOthers)
            {
                throw new AggregateException(
                    $"Migration failed for {failures.Count} tenants",
                    failures.Select(f => f.Exception));
            }
        }
        else
        {
            _logger.LogInformation("Successfully migrated all {Count} tenants", tenantList.Count);
        }
    }

    /// <summary>
    /// Applies pending migrations to a specific tenant schema.
    /// </summary>
    /// <param name="tenantId">The tenant identifier.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task MigrateTenantAsync(TKey tenantId, CancellationToken cancellationToken = default)
    {
        var schemaName = _options.SchemaPerTenant.GenerateSchemaName(tenantId);
        await MigrateTenantInternalAsync(schemaName, cancellationToken);
    }

    private async Task MigrateTenantInternalAsync(string tenantId, CancellationToken cancellationToken)
    {
        var schemaName = _options.SchemaPerTenant.SchemaPrefix + tenantId;
        if (tenantId.StartsWith(_options.SchemaPerTenant.SchemaPrefix, StringComparison.OrdinalIgnoreCase))
        {
            schemaName = tenantId;
            tenantId = _options.SchemaPerTenant.ExtractTenantId(tenantId);
        }

        _logger.LogDebug("Starting migration for tenant {TenantId} in schema {Schema}", tenantId, schemaName);

        using var scope = _serviceProvider.CreateScope();

        // Create a tenant context for this migration
        var tenantContext = new TenantContext<TKey>(ParseTenantId(tenantId), schemaName);
        _contextAccessor.SetTenantContext(tenantContext);

        try
        {
            var contextFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<TContext>>();
            await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);

            // Apply migrations
            var migrator = context.GetService<IMigrator>();

            var pendingMigrations = (await context.Database.GetPendingMigrationsAsync(cancellationToken)).ToList();

            if (pendingMigrations.Count == 0)
            {
                _logger.LogDebug("No pending migrations for tenant {TenantId}", tenantId);
                return;
            }

            _logger.LogInformation("Applying {Count} migrations to tenant {TenantId}: {Migrations}",
                pendingMigrations.Count, tenantId, string.Join(", ", pendingMigrations));

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(_options.Migrations.Timeout);

            await ExecuteWithRetryAsync(async () =>
            {
                await migrator.MigrateAsync(cancellationToken: cts.Token);
            }, cancellationToken);

            foreach (var migration in pendingMigrations)
            {
                await _eventPublisher.PublishMigrationAppliedAsync(tenantContext.TenantId, migration, cancellationToken);
            }

            _logger.LogInformation("Successfully applied migrations to tenant {TenantId}", tenantId);
        }
        finally
        {
            _contextAccessor.SetTenantContext(null);
        }
    }

    private async Task ExecuteWithRetryAsync(Func<Task> action, CancellationToken cancellationToken)
    {
        var options = _options.Migrations;
        var attempt = 0;

        while (true)
        {
            try
            {
                await action();
                return;
            }
            catch (Exception ex) when (options.RetryOnFailure && attempt < options.RetryCount)
            {
                attempt++;
                _logger.LogWarning(ex, "Migration attempt {Attempt} failed, retrying in {Delay}",
                    attempt, options.RetryDelay);
                await Task.Delay(options.RetryDelay, cancellationToken);
            }
        }
    }

    private static TKey ParseTenantId(string tenantIdString)
    {
        var type = typeof(TKey);

        if (type == typeof(string))
            return (TKey)(object)tenantIdString;

        if (type == typeof(Guid))
            return (TKey)(object)Guid.Parse(tenantIdString);

        if (type == typeof(int))
            return (TKey)(object)int.Parse(tenantIdString);

        if (type == typeof(long))
            return (TKey)(object)long.Parse(tenantIdString);

        throw new NotSupportedException($"Tenant key type {type.Name} is not supported for parsing");
    }
}
