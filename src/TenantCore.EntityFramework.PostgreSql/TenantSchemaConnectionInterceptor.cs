using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;
using TenantCore.EntityFramework.Abstractions;
using TenantCore.EntityFramework.Configuration;
using TenantCore.EntityFramework.PostgreSql.Utilities;

namespace TenantCore.EntityFramework.PostgreSql;

/// <summary>
/// Intercepts database connections to set the PostgreSQL search_path based on the current tenant context.
/// This ensures that unqualified table names in SQL (including migrations) resolve to the tenant's schema.
/// </summary>
/// <typeparam name="TKey">The type of the tenant identifier.</typeparam>
public class TenantSchemaConnectionInterceptor<TKey> : DbConnectionInterceptor
    where TKey : notnull
{
    private readonly ITenantContextAccessor<TKey> _tenantContextAccessor;
    private readonly TenantCoreOptions _options;

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
    public override void ConnectionOpened(DbConnection connection, ConnectionEndEventData eventData)
    {
        SetSearchPath(connection);
        base.ConnectionOpened(connection, eventData);
    }

    /// <inheritdoc />
    public override async Task ConnectionOpenedAsync(
        DbConnection connection,
        ConnectionEndEventData eventData,
        CancellationToken cancellationToken = default)
    {
        await SetSearchPathAsync(connection, cancellationToken);
        await base.ConnectionOpenedAsync(connection, eventData, cancellationToken);
    }

    private void SetSearchPath(DbConnection connection)
    {
        var schemaName = _tenantContextAccessor.TenantContext?.SchemaName;
        if (string.IsNullOrEmpty(schemaName))
        {
            return;
        }

        using var command = connection.CreateCommand();
        command.CommandText = $"SET search_path TO \"{EscapeIdentifier(schemaName)}\", public";
        command.ExecuteNonQuery();
    }

    private async Task SetSearchPathAsync(DbConnection connection, CancellationToken cancellationToken)
    {
        var schemaName = _tenantContextAccessor.TenantContext?.SchemaName;
        if (string.IsNullOrEmpty(schemaName))
        {
            return;
        }

        await using var command = connection.CreateCommand();
        command.CommandText = $"SET search_path TO \"{EscapeIdentifier(schemaName)}\", public";
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static string EscapeIdentifier(string identifier)
    {
        return PostgreSqlIdentifierHelper.EscapeIdentifier(identifier);
    }
}
