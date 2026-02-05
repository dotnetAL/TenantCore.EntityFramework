using Microsoft.EntityFrameworkCore;

namespace TenantCore.EntityFramework.Migrations;

/// <summary>
/// Tracks migration status across tenant schemas.
/// </summary>
public class MigrationTracker
{
    /// <summary>
    /// Gets the migration status for all tenants.
    /// </summary>
    /// <typeparam name="TContext">The DbContext type.</typeparam>
    /// <param name="context">A DbContext instance.</param>
    /// <param name="tenantSchemas">The list of tenant schema names.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A dictionary of tenant schema to migration status.</returns>
    public async Task<Dictionary<string, TenantMigrationStatus>> GetMigrationStatusAsync<TContext>(
        TContext context,
        IEnumerable<string> tenantSchemas,
        CancellationToken cancellationToken = default) where TContext : DbContext
    {
        var result = new Dictionary<string, TenantMigrationStatus>();
        var allMigrations = context.Database.GetMigrations().ToList();

        foreach (var schema in tenantSchemas)
        {
            try
            {
                var appliedMigrations = await GetAppliedMigrationsForSchemaAsync(context, schema, cancellationToken);
                var pendingMigrations = allMigrations.Except(appliedMigrations).ToList();

                result[schema] = new TenantMigrationStatus
                {
                    SchemaName = schema,
                    AppliedMigrations = appliedMigrations,
                    PendingMigrations = pendingMigrations,
                    IsUpToDate = pendingMigrations.Count == 0
                };
            }
            catch (Exception ex)
            {
                result[schema] = new TenantMigrationStatus
                {
                    SchemaName = schema,
                    AppliedMigrations = new List<string>(),
                    PendingMigrations = allMigrations,
                    IsUpToDate = false,
                    Error = ex.Message
                };
            }
        }

        return result;
    }

    private async Task<List<string>> GetAppliedMigrationsForSchemaAsync<TContext>(
        TContext context,
        string schema,
        CancellationToken cancellationToken) where TContext : DbContext
    {
        var historyTable = $"\"{schema}\".\"__EFMigrationsHistory\"";

        var sql = $@"
            SELECT ""MigrationId""
            FROM {historyTable}
            ORDER BY ""MigrationId""";

        var result = new List<string>();

        await using var command = context.Database.GetDbConnection().CreateCommand();
        command.CommandText = sql;

        if (context.Database.GetDbConnection().State != System.Data.ConnectionState.Open)
        {
            await context.Database.GetDbConnection().OpenAsync(cancellationToken);
        }

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            result.Add(reader.GetString(0));
        }

        return result;
    }
}

/// <summary>
/// Represents the migration status for a tenant.
/// </summary>
public class TenantMigrationStatus
{
    /// <summary>
    /// Gets or sets the tenant schema name.
    /// </summary>
    public required string SchemaName { get; init; }

    /// <summary>
    /// Gets or sets the list of applied migrations.
    /// </summary>
    public required IReadOnlyList<string> AppliedMigrations { get; init; }

    /// <summary>
    /// Gets or sets the list of pending migrations.
    /// </summary>
    public required IReadOnlyList<string> PendingMigrations { get; init; }

    /// <summary>
    /// Gets or sets whether the tenant is up to date with all migrations.
    /// </summary>
    public bool IsUpToDate { get; init; }

    /// <summary>
    /// Gets or sets any error that occurred while checking status.
    /// </summary>
    public string? Error { get; init; }
}
