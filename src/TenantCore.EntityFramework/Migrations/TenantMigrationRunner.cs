using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using TenantCore.EntityFramework.Abstractions;
using TenantCore.EntityFramework.Configuration;
using TenantCore.EntityFramework.Context;
using TenantCore.EntityFramework.ControlDb;
using TenantCore.EntityFramework.Events;
using TenantCore.EntityFramework.Utilities;

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
    private readonly ITenantStore? _tenantStore;
    private readonly ILogger<TenantMigrationRunner<TContext, TKey>> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="TenantMigrationRunner{TContext, TKey}"/> class.
    /// </summary>
    /// <param name="serviceProvider">The service provider.</param>
    /// <param name="options">The tenant configuration options.</param>
    /// <param name="strategy">The tenant isolation strategy.</param>
    /// <param name="contextAccessor">The tenant context accessor.</param>
    /// <param name="eventPublisher">The event publisher.</param>
    /// <param name="logger">The logger instance.</param>
    /// <param name="tenantStore">The optional tenant store for control database integration.</param>
    public TenantMigrationRunner(
        IServiceProvider serviceProvider,
        TenantCoreOptions options,
        ITenantStrategy<TKey> strategy,
        ITenantContextAccessor<TKey> contextAccessor,
        ITenantEventPublisher<TKey> eventPublisher,
        ILogger<TenantMigrationRunner<TContext, TKey>> logger,
        ITenantStore? tenantStore = null)
    {
        _serviceProvider = serviceProvider;
        _options = options;
        _strategy = strategy;
        _contextAccessor = contextAccessor;
        _eventPublisher = eventPublisher;
        _tenantStore = tenantStore;
        _logger = logger;
    }

    /// <summary>
    /// Applies pending migrations to all tenant schemas.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task MigrateAllTenantsAsync(CancellationToken cancellationToken = default)
    {
        var tenantList = await GetMigratableTenantsAsync(cancellationToken);

        _logger.LogInformation("Starting migration for {Count} tenants", tenantList.Count);

        var migrationOptions = _options.Migrations;
        var semaphore = new SemaphoreSlim(migrationOptions.ParallelMigrations);
        var failures = new List<(string TenantId, Exception Exception)>();

        var tasks = tenantList.Select(async tenant =>
        {
            await semaphore.WaitAsync(cancellationToken);
            try
            {
                var schemaName = tenant.Schema;
                var tenantId = TenantKeyParser<TKey>.Parse(tenant.TenantIdString);
                await MigrateTenantInternalAsync(schemaName, tenantId, cancellationToken);
            }
            catch (Exception ex) when (migrationOptions.FailureBehavior != MigrationFailureBehavior.StopAll)
            {
                lock (failures)
                {
                    failures.Add((tenant.TenantIdString, ex));
                }

                if (migrationOptions.FailureBehavior == MigrationFailureBehavior.Skip)
                {
                    _logger.LogWarning(ex, "Migration failed for tenant {TenantId}, skipping", tenant.TenantIdString);
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

    private async Task<List<(string TenantIdString, string Schema)>> GetMigratableTenantsAsync(
        CancellationToken cancellationToken)
    {
        // If tenant store is available, use it to get tenants with migratable statuses
        if (_tenantStore != null)
        {
            var migratableStatuses = _options.ControlDb.MigratableStatuses;
            var tenants = await _tenantStore.GetTenantsAsync(migratableStatuses, cancellationToken);

            _logger.LogDebug("Using control database to enumerate {Count} tenants with statuses: {Statuses}",
                tenants.Count, string.Join(", ", migratableStatuses));

            return tenants
                .Select(t => (t.TenantId.ToString(), t.TenantSchema))
                .ToList();
        }

        // Fall back to schema discovery
        using var scope = _serviceProvider.CreateScope();
        var contextFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<TContext>>();

        await using var tempContext = await contextFactory.CreateDbContextAsync(cancellationToken);
        var tenantIds = await _strategy.GetTenantsAsync(tempContext, cancellationToken);

        return tenantIds
            .Select(id => (id, _options.SchemaPerTenant.GenerateSchemaName(id)))
            .ToList();
    }

    /// <summary>
    /// Applies pending migrations to a specific tenant schema.
    /// </summary>
    /// <param name="tenantId">The tenant identifier.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task MigrateTenantAsync(TKey tenantId, CancellationToken cancellationToken = default)
    {
        var schemaName = _options.SchemaPerTenant.GenerateSchemaName(tenantId);
        await MigrateTenantInternalAsync(schemaName, tenantId, cancellationToken);
    }

    private async Task MigrateTenantInternalAsync(string schemaName, TKey tenantId, CancellationToken cancellationToken)
    {
        _logger.LogDebug("Starting migration for tenant {TenantId} in schema {Schema}", tenantId, schemaName);

        using var scope = _serviceProvider.CreateScope();

        // Create a tenant context for this migration
        var tenantContext = new TenantContext<TKey>(tenantId, schemaName);
        _contextAccessor.SetTenantContext(tenantContext);

        try
        {
            var contextFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<TContext>>();
            await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);

            // Get pending migrations
            var appliedMigrations = (await context.Database.GetAppliedMigrationsAsync(cancellationToken)).ToHashSet();
            var migrator = context.GetService<IMigrator>();

            // Generate the full migration SQL script
            var fullScript = migrator.GenerateScript();

            if (string.IsNullOrWhiteSpace(fullScript))
            {
                _logger.LogDebug("No migrations to apply for tenant {TenantId}", tenantId);
                return;
            }

            // Get pending migrations for logging
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
                // Inject schema into the migration SQL and execute
                await ApplyMigrationWithSchemaAsync(context, schemaName, pendingMigrations, cts.Token);
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

    /// <summary>
    /// Applies migrations by generating SQL, injecting the schema, and executing directly.
    /// This approach bypasses EF Core's internal schema handling which doesn't work well
    /// with dynamic per-tenant schemas.
    /// </summary>
    private async Task ApplyMigrationWithSchemaAsync(
        TContext context,
        string schemaName,
        List<string> pendingMigrations,
        CancellationToken cancellationToken)
    {
        var migrator = context.GetService<IMigrator>();
        var escapedSchema = SqlIdentifierHelper.EscapeDoubleQuotes(schemaName);

        // First, ensure the schema and migrations history table exist
        var setupSql = $@"
CREATE SCHEMA IF NOT EXISTS ""{escapedSchema}"";
SET search_path TO ""{escapedSchema}"", public;
CREATE TABLE IF NOT EXISTS ""{escapedSchema}"".""__EFMigrationsHistory"" (
    ""MigrationId"" character varying(150) NOT NULL,
    ""ProductVersion"" character varying(32) NOT NULL,
    CONSTRAINT ""PK___EFMigrationsHistory"" PRIMARY KEY (""MigrationId"")
);";

        await context.Database.ExecuteSqlRawAsync(setupSql, cancellationToken);

        // Get the last applied migration (if any) - need to query the tenant's history table
        string? lastAppliedMigration = null;
        try
        {
            var connection = context.Database.GetDbConnection();
            if (connection.State != System.Data.ConnectionState.Open)
            {
                await connection.OpenAsync(cancellationToken);
            }

            await using var cmd = connection.CreateCommand();
            cmd.CommandText = $@"
                SELECT ""MigrationId"" FROM ""{escapedSchema}"".""__EFMigrationsHistory""
                ORDER BY ""MigrationId"" DESC LIMIT 1";
            var result = await cmd.ExecuteScalarAsync(cancellationToken);
            lastAppliedMigration = result as string;
        }
        catch
        {
            // Table might not exist or be empty - that's fine
        }

        // Generate SQL only for migrations after the last applied one
        var sql = migrator.GenerateScript(
            fromMigration: lastAppliedMigration,
            toMigration: pendingMigrations.Last());

        if (string.IsNullOrWhiteSpace(sql))
        {
            _logger.LogDebug("No migration SQL generated for schema {Schema}", schemaName);
            return;
        }

        // Inject schema into the SQL
        var schemaQualifiedSql = InjectSchemaIntoMigrationSql(sql, schemaName);

        _logger.LogDebug("Executing schema-qualified migration SQL for schema {Schema}. SQL length: {Length}",
            schemaName, schemaQualifiedSql.Length);
        _logger.LogTrace("Migration SQL for {Schema}:\n{Sql}", schemaName, schemaQualifiedSql);

        // Execute the migration SQL
        await context.Database.ExecuteSqlRawAsync(schemaQualifiedSql, cancellationToken);
    }

    /// <summary>
    /// Injects the tenant schema into migration SQL by:
    /// 1. Setting search_path so unqualified names resolve to tenant schema
    /// 2. Updating the __EFMigrationsHistory table references to be schema-qualified
    /// </summary>
    private static string InjectSchemaIntoMigrationSql(string sql, string schemaName)
    {
        var escapedSchema = SqlIdentifierHelper.EscapeDoubleQuotes(schemaName);

        // Build the schema-qualified SQL
        var builder = new System.Text.StringBuilder();

        // Set search_path so unqualified names resolve to tenant schema
        builder.AppendLine($"SET search_path TO \"{escapedSchema}\", public;");
        builder.AppendLine();

        // Replace __EFMigrationsHistory references to be schema-qualified
        // This handles both CREATE TABLE and INSERT INTO statements
        var modifiedSql = sql
            .Replace(
                "CREATE TABLE \"__EFMigrationsHistory\"",
                $"CREATE TABLE IF NOT EXISTS \"{escapedSchema}\".\"__EFMigrationsHistory\"")
            .Replace(
                "INSERT INTO \"__EFMigrationsHistory\"",
                $"INSERT INTO \"{escapedSchema}\".\"__EFMigrationsHistory\"");

        builder.Append(modifiedSql);

        return builder.ToString();
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
}
