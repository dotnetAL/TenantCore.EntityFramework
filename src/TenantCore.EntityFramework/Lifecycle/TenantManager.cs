using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using TenantCore.EntityFramework.Abstractions;
using TenantCore.EntityFramework.Configuration;
using TenantCore.EntityFramework.Context;
using TenantCore.EntityFramework.ControlDb;
using TenantCore.EntityFramework.Events;
using TenantCore.EntityFramework.Migrations;

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
    private readonly ITenantStrategy<TKey> _strategy;
    private readonly ITenantScopeFactory<TKey> _scopeFactory;
    private readonly TenantMigrationRunner<TContext, TKey> _migrationRunner;
    private readonly ITenantEventPublisher<TKey> _eventPublisher;
    private readonly ITenantStore? _tenantStore;
    private readonly ILogger<TenantManager<TContext, TKey>> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="TenantManager{TContext, TKey}"/> class.
    /// </summary>
    /// <param name="serviceProvider">The service provider.</param>
    /// <param name="options">The tenant configuration options.</param>
    /// <param name="strategy">The tenant isolation strategy.</param>
    /// <param name="scopeFactory">The tenant scope factory.</param>
    /// <param name="migrationRunner">The migration runner.</param>
    /// <param name="eventPublisher">The event publisher.</param>
    /// <param name="tenantStore">The optional tenant store for control database integration.</param>
    /// <param name="logger">The logger instance.</param>
    public TenantManager(
        IServiceProvider serviceProvider,
        TenantCoreOptions options,
        ITenantStrategy<TKey> strategy,
        ITenantScopeFactory<TKey> scopeFactory,
        TenantMigrationRunner<TContext, TKey> migrationRunner,
        ITenantEventPublisher<TKey> eventPublisher,
        ILogger<TenantManager<TContext, TKey>> logger,
        ITenantStore? tenantStore = null)
    {
        _serviceProvider = serviceProvider;
        _options = options;
        _strategy = strategy;
        _scopeFactory = scopeFactory;
        _migrationRunner = migrationRunner;
        _eventPublisher = eventPublisher;
        _tenantStore = tenantStore;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task ProvisionTenantAsync(TKey tenantId, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Provisioning tenant {TenantId}", tenantId);

        // If control database is enabled, create a record there first
        Guid? controlDbTenantId = null;
        if (_tenantStore != null)
        {
            controlDbTenantId = ConvertToGuid(tenantId);
            var schemaName = _options.SchemaPerTenant.GenerateSchemaName(tenantId);
            var request = new CreateTenantRequest(tenantId.ToString()!, schemaName);

            try
            {
                await _tenantStore.CreateTenantAsync(controlDbTenantId.Value, request, cancellationToken);
            }
            catch (TenantAlreadyExistsException)
            {
                throw;
            }
            catch (Microsoft.EntityFrameworkCore.DbUpdateException ex) when (IsDuplicateKeyException(ex))
            {
                throw new TenantAlreadyExistsException(tenantId, ex);
            }
        }

        try
        {
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

            // Update status to Active in control database
            if (_tenantStore != null && controlDbTenantId.HasValue)
            {
                await _tenantStore.UpdateStatusAsync(controlDbTenantId.Value, TenantStatus.Active, cancellationToken);
            }

            // Publish event
            await _eventPublisher.PublishTenantCreatedAsync(tenantId, cancellationToken);

            _logger.LogInformation("Successfully provisioned tenant {TenantId}", tenantId);
        }
        catch (Exception) when (_tenantStore != null && controlDbTenantId.HasValue)
        {
            // Rollback control database record on failure
            _logger.LogWarning("Provisioning failed, removing tenant {TenantId} from control database", tenantId);
            await _tenantStore.DeleteTenantAsync(controlDbTenantId.Value, cancellationToken);
            throw;
        }
    }

    private static Guid ConvertToGuid(TKey tenantId)
    {
        if (typeof(TKey) == typeof(Guid))
        {
            return (Guid)(object)tenantId;
        }

        if (typeof(TKey) == typeof(string))
        {
            var stringValue = (string)(object)tenantId;
            // Try to parse as Guid, otherwise generate a deterministic Guid from the string
            if (Guid.TryParse(stringValue, out var guid))
            {
                return guid;
            }
            // Generate deterministic Guid from string using MD5 hash
            using var md5 = System.Security.Cryptography.MD5.Create();
            var hash = md5.ComputeHash(System.Text.Encoding.UTF8.GetBytes(stringValue));
            return new Guid(hash);
        }

        throw new InvalidOperationException(
            $"Cannot convert {typeof(TKey).Name} to Guid. Control database requires TKey to be Guid or string.");
    }

    private static bool IsDuplicateKeyException(Microsoft.EntityFrameworkCore.DbUpdateException ex)
    {
        // Check for PostgreSQL unique constraint violation (23505)
        // or SQL Server (2627, 2601)
        var inner = ex.InnerException;
        if (inner == null) return false;

        var message = inner.Message;
        return message.Contains("23505") ||  // PostgreSQL unique_violation
               message.Contains("duplicate key") ||
               message.Contains("unique constraint");
    }

    /// <summary>
    /// Provisions a new tenant with control database integration.
    /// Creates the tenant record in the control database, provisions the schema, and sets status to Active.
    /// </summary>
    /// <param name="tenantId">The tenant identifier (must be Guid).</param>
    /// <param name="request">The tenant creation request containing metadata.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The created tenant record.</returns>
    /// <exception cref="InvalidOperationException">Thrown when control database is not configured or TKey is not Guid.</exception>
    public async Task<TenantRecord> ProvisionTenantAsync(
        Guid tenantId,
        CreateTenantRequest request,
        CancellationToken cancellationToken = default)
    {
        if (_tenantStore == null)
        {
            throw new InvalidOperationException(
                "Control database is not configured. Use UseControlDatabase() or UseTenantStore() in configuration.");
        }

        _logger.LogInformation("Provisioning tenant {TenantId} with slug {TenantSlug}", tenantId, request.TenantSlug);

        // Create tenant record in control database (status: Pending)
        var tenantRecord = await _tenantStore.CreateTenantAsync(tenantId, request, cancellationToken);

        try
        {
            // Convert Guid to TKey for provisioning
            var tenantIdAsKey = ConvertToTKey(tenantId);

            using var scope = _serviceProvider.CreateScope();
            var contextFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<TContext>>();

            await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);

            // Create the schema
            await _strategy.ProvisionTenantAsync(context, tenantIdAsKey, cancellationToken);

            // Apply migrations
            await _migrationRunner.MigrateTenantAsync(tenantIdAsKey, cancellationToken);

            // Run seeders
            await RunSeedersAsync(tenantIdAsKey, cancellationToken);

            // Update status to Active
            tenantRecord = await _tenantStore.UpdateStatusAsync(tenantId, TenantStatus.Active, cancellationToken);

            // Publish event
            await _eventPublisher.PublishTenantCreatedAsync(tenantIdAsKey, cancellationToken);

            _logger.LogInformation("Successfully provisioned tenant {TenantId}", tenantId);

            return tenantRecord;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to provision tenant {TenantId}, rolling back control database record", tenantId);

            // Rollback: delete the tenant record from control database
            await _tenantStore.DeleteTenantAsync(tenantId, cancellationToken);

            throw;
        }
    }

    private static TKey ConvertToTKey(Guid guid)
    {
        if (typeof(TKey) == typeof(Guid))
        {
            return (TKey)(object)guid;
        }

        if (typeof(TKey) == typeof(string))
        {
            return (TKey)(object)guid.ToString();
        }

        throw new InvalidOperationException(
            $"Cannot convert Guid to {typeof(TKey).Name}. Control database provisioning requires TKey to be Guid or string.");
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

        // Also remove from control database if enabled
        if (_tenantStore != null)
        {
            var guid = ConvertToGuid(tenantId);
            try
            {
                await _tenantStore.DeleteTenantAsync(guid, cancellationToken);
                _logger.LogDebug("Removed tenant {TenantId} from control database", tenantId);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to remove tenant {TenantId} from control database", tenantId);
            }
        }

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

        using var tenantScope = _scopeFactory.CreateScope(tenantId);

        using var scope = _serviceProvider.CreateScope();
        var contextFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<TContext>>();
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);

        foreach (var seeder in seeders)
        {
            _logger.LogDebug("Running seeder {SeederType}", seeder.GetType().Name);
            await seeder.SeedAsync(context, tenantId, cancellationToken);
        }
    }
}
