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
    private readonly TenantDbContextOptions<TContext>? _contextOptions;
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
    /// <param name="contextOptions">Optional per-context options for migration history table override.</param>
    /// <param name="tenantStore">The optional tenant store for control database integration.</param>
    public TenantMigrationRunner(
        IServiceProvider serviceProvider,
        TenantCoreOptions options,
        ITenantStrategy<TKey> strategy,
        ITenantContextAccessor<TKey> contextAccessor,
        ITenantEventPublisher<TKey> eventPublisher,
        ILogger<TenantMigrationRunner<TContext, TKey>> logger,
        TenantDbContextOptions<TContext>? contextOptions = null,
        ITenantStore? tenantStore = null)
    {
        _serviceProvider = serviceProvider;
        _options = options;
        _contextOptions = contextOptions;
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

            var migrator = context.GetService<IMigrator>();

            // Determine all known migrations from the model and which are already applied
            // in this tenant's schema-qualified history table (not EF's cached options)
            var allMigrations = context.Database.GetMigrations().ToList();
            var appliedMigrations = await GetAppliedMigrationsFromSchemaAsync(
                context, schemaName, cancellationToken);
            var pendingMigrations = allMigrations.Except(appliedMigrations).ToList();

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
        var migrationHistoryTable = _contextOptions?.MigrationHistoryTable
            ?? _options.Migrations.MigrationHistoryTable;
        var escapedHistoryTable = SqlIdentifierHelper.EscapeDoubleQuotes(migrationHistoryTable);

        // First, ensure the schema and migrations history table exist
        var setupSql = $@"
CREATE SCHEMA IF NOT EXISTS ""{escapedSchema}"";
SET search_path TO ""{escapedSchema}"", public;
CREATE TABLE IF NOT EXISTS ""{escapedSchema}"".""{escapedHistoryTable}"" (
    ""MigrationId"" character varying(150) NOT NULL,
    ""ProductVersion"" character varying(32) NOT NULL,
    CONSTRAINT ""PK_{escapedHistoryTable}"" PRIMARY KEY (""MigrationId"")
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
                SELECT ""MigrationId"" FROM ""{escapedSchema}"".""{escapedHistoryTable}""
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
        var schemaQualifiedSql = InjectSchemaIntoMigrationSql(sql, schemaName, migrationHistoryTable);

        _logger.LogDebug("Executing schema-qualified migration SQL for schema {Schema}. SQL length: {Length}",
            schemaName, schemaQualifiedSql.Length);
        _logger.LogTrace("Migration SQL for {Schema}:\n{Sql}", schemaName, schemaQualifiedSql);

        // Execute the migration SQL
        await context.Database.ExecuteSqlRawAsync(schemaQualifiedSql, cancellationToken);
    }

    /// <summary>
    /// Injects the tenant schema into migration SQL by:
    /// 1. Removing EF-generated schema creation blocks (we handle this in setup SQL)
    /// 2. Setting search_path so unqualified names resolve to tenant schema
    /// 3. Replacing any schema-qualified history table references with the correct schema
    /// </summary>
    private static string InjectSchemaIntoMigrationSql(string sql, string schemaName, string migrationHistoryTable)
    {
        var escapedSchema = SqlIdentifierHelper.EscapeDoubleQuotes(schemaName);
        var escapedHistoryTable = SqlIdentifierHelper.EscapeDoubleQuotes(migrationHistoryTable);

        // Remove EF-generated schema creation blocks (DO $EF$ ... END $EF$;)
        // These reference the cached schema from DbContextOptions and are incorrect
        // for tenants other than the first one. We create the schema in setup SQL instead.
        var modifiedSql = System.Text.RegularExpressions.Regex.Replace(
            sql,
            @"DO \$EF\$.*?END \$EF\$;\s*",
            string.Empty,
            System.Text.RegularExpressions.RegexOptions.Singleline);

        // Replace any schema-qualified history table references.
        // EF Core may generate these with quoted ("schema"."table") or unquoted (schema."table")
        // schema names, depending on the provider. The schema may be stale (from a cached
        // DbContextOptions for a different tenant), so we replace it with the correct target.
        var escapedHistoryTableRegex = System.Text.RegularExpressions.Regex.Escape(escapedHistoryTable);
        // Matches: optional_schema."historyTable" where schema can be quoted or unquoted
        var historyTablePattern = $@"(?:""[^""]+""\.|\w+\.)?""{ escapedHistoryTableRegex}""";
        var schemaQualifiedHistoryTable = $@"""{escapedSchema}"".""{escapedHistoryTable}""";

        modifiedSql = System.Text.RegularExpressions.Regex.Replace(
            modifiedSql,
            $@"CREATE TABLE IF NOT EXISTS\s+{historyTablePattern}",
            $@"CREATE TABLE IF NOT EXISTS {schemaQualifiedHistoryTable}");

        modifiedSql = System.Text.RegularExpressions.Regex.Replace(
            modifiedSql,
            $@"CREATE TABLE\s+{historyTablePattern}",
            $@"CREATE TABLE IF NOT EXISTS {schemaQualifiedHistoryTable}");

        modifiedSql = System.Text.RegularExpressions.Regex.Replace(
            modifiedSql,
            $@"INSERT INTO\s+{historyTablePattern}",
            $@"INSERT INTO {schemaQualifiedHistoryTable}");

        // Build the final SQL with search_path
        var builder = new System.Text.StringBuilder();
        builder.AppendLine($"SET search_path TO \"{escapedSchema}\", public;");
        builder.AppendLine();
        builder.Append(modifiedSql);

        return builder.ToString();
    }

    /// <summary>
    /// Queries applied migrations directly from the tenant's schema-qualified history table.
    /// This avoids relying on EF Core's cached MigrationsHistoryTable option, which may point
    /// to a different tenant's schema when the DbContextOptions are pooled/cached.
    /// </summary>
    private async Task<HashSet<string>> GetAppliedMigrationsFromSchemaAsync(
        TContext context,
        string schemaName,
        CancellationToken cancellationToken)
    {
        var migrationHistoryTable = _contextOptions?.MigrationHistoryTable
            ?? _options.Migrations.MigrationHistoryTable;
        var escapedSchema = SqlIdentifierHelper.EscapeDoubleQuotes(schemaName);
        var escapedHistoryTable = SqlIdentifierHelper.EscapeDoubleQuotes(migrationHistoryTable);

        var applied = new HashSet<string>();

        try
        {
            var connection = context.Database.GetDbConnection();
            if (connection.State != System.Data.ConnectionState.Open)
            {
                await connection.OpenAsync(cancellationToken);
            }

            await using var cmd = connection.CreateCommand();
            cmd.CommandText = $@"
                SELECT ""MigrationId"" FROM ""{escapedSchema}"".""{escapedHistoryTable}""
                ORDER BY ""MigrationId""";

            await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                applied.Add(reader.GetString(0));
            }
        }
        catch
        {
            // Table may not exist yet - that means no migrations are applied
        }

        return applied;
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
