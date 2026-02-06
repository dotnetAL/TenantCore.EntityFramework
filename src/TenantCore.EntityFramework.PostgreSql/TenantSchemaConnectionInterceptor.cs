using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;
using TenantCore.EntityFramework.Abstractions;
using TenantCore.EntityFramework.Configuration;
using TenantCore.EntityFramework.PostgreSql.Utilities;

namespace TenantCore.EntityFramework.PostgreSql;

/// <summary>
/// Intercepts database commands to set the PostgreSQL search_path based on the current tenant context.
/// This ensures that unqualified table names in SQL (including migrations) resolve to the tenant's schema.
///
/// CRITICAL: This interceptor uses command interception (not just connection interception) to handle
/// connection pooling correctly. When connections are reused from the pool, they are already open,
/// so ConnectionOpened would not fire. By intercepting at the command level, we ensure search_path
/// is set correctly for every command, regardless of connection pool state.
/// </summary>
/// <typeparam name="TKey">The type of the tenant identifier.</typeparam>
public class TenantSchemaConnectionInterceptor<TKey> : DbCommandInterceptor
    where TKey : notnull
{
    private const string PublicSchema = "public";

    private readonly ITenantContextAccessor<TKey> _tenantContextAccessor;
    private readonly TenantCoreOptions _options;

    // Track the last schema set per connection to avoid redundant SET commands
    // We use ConditionalWeakTable to avoid memory leaks - entries are automatically
    // removed when the connection is garbage collected
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<DbConnection, SchemaTracker> _connectionSchemas = new();

    private class SchemaTracker
    {
        public string? CurrentSchema { get; set; }
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="TenantSchemaConnectionInterceptor{TKey}"/> class.
    /// </summary>
    /// <param name="tenantContextAccessor">The tenant context accessor.</param>
    /// <param name="options">The tenant core options.</param>
    public TenantSchemaConnectionInterceptor(
        ITenantContextAccessor<TKey> tenantContextAccessor,
        TenantCoreOptions options)
    {
        _tenantContextAccessor = tenantContextAccessor;
        _options = options;
    }

    /// <inheritdoc />
    public override InterceptionResult<DbDataReader> ReaderExecuting(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<DbDataReader> result)
    {
        SetSearchPathIfNeeded(command.Connection!);
        return base.ReaderExecuting(command, eventData, result);
    }

    /// <inheritdoc />
    public override async ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<DbDataReader> result,
        CancellationToken cancellationToken = default)
    {
        await SetSearchPathIfNeededAsync(command.Connection!, cancellationToken);
        return await base.ReaderExecutingAsync(command, eventData, result, cancellationToken);
    }

    /// <inheritdoc />
    public override InterceptionResult<int> NonQueryExecuting(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<int> result)
    {
        SetSearchPathIfNeeded(command.Connection!);
        return base.NonQueryExecuting(command, eventData, result);
    }

    /// <inheritdoc />
    public override async ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        await SetSearchPathIfNeededAsync(command.Connection!, cancellationToken);
        return await base.NonQueryExecutingAsync(command, eventData, result, cancellationToken);
    }

    /// <inheritdoc />
    public override InterceptionResult<object> ScalarExecuting(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<object> result)
    {
        SetSearchPathIfNeeded(command.Connection!);
        return base.ScalarExecuting(command, eventData, result);
    }

    /// <inheritdoc />
    public override async ValueTask<InterceptionResult<object>> ScalarExecutingAsync(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<object> result,
        CancellationToken cancellationToken = default)
    {
        await SetSearchPathIfNeededAsync(command.Connection!, cancellationToken);
        return await base.ScalarExecutingAsync(command, eventData, result, cancellationToken);
    }

    private void SetSearchPathIfNeeded(DbConnection connection)
    {
        var schemaName = _tenantContextAccessor.TenantContext?.SchemaName;

        // CRITICAL: If no tenant context, reset to public schema to prevent
        // accidental access to a previous tenant's data on pooled connections
        var effectiveSchema = string.IsNullOrEmpty(schemaName) ? PublicSchema : schemaName;

        var tracker = _connectionSchemas.GetOrCreateValue(connection);

        // Only set search_path if it's different from what's already set on this connection
        if (tracker.CurrentSchema == effectiveSchema)
        {
            return;
        }

        using var command = connection.CreateCommand();
        command.CommandText = $"SET search_path TO \"{EscapeIdentifier(effectiveSchema)}\", public";
        command.ExecuteNonQuery();

        tracker.CurrentSchema = effectiveSchema;
    }

    private async Task SetSearchPathIfNeededAsync(DbConnection connection, CancellationToken cancellationToken)
    {
        var schemaName = _tenantContextAccessor.TenantContext?.SchemaName;

        // CRITICAL: If no tenant context, reset to public schema to prevent
        // accidental access to a previous tenant's data on pooled connections
        var effectiveSchema = string.IsNullOrEmpty(schemaName) ? PublicSchema : schemaName;

        var tracker = _connectionSchemas.GetOrCreateValue(connection);

        // Only set search_path if it's different from what's already set on this connection
        if (tracker.CurrentSchema == effectiveSchema)
        {
            return;
        }

        await using var command = connection.CreateCommand();
        command.CommandText = $"SET search_path TO \"{EscapeIdentifier(effectiveSchema)}\", public";
        await command.ExecuteNonQueryAsync(cancellationToken);

        tracker.CurrentSchema = effectiveSchema;
    }

    private static string EscapeIdentifier(string identifier)
    {
        return PostgreSqlIdentifierHelper.EscapeIdentifier(identifier);
    }
}
