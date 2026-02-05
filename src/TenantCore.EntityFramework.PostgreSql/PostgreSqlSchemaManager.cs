using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using TenantCore.EntityFramework.Abstractions;
using TenantCore.EntityFramework.PostgreSql.Utilities;

namespace TenantCore.EntityFramework.PostgreSql;

/// <summary>
/// PostgreSQL implementation of schema management operations.
/// </summary>
public class PostgreSqlSchemaManager : ISchemaManager
{
    private readonly ILogger<PostgreSqlSchemaManager> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="PostgreSqlSchemaManager"/> class.
    /// </summary>
    /// <param name="logger">The logger instance.</param>
    public PostgreSqlSchemaManager(ILogger<PostgreSqlSchemaManager> logger)
    {
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task CreateSchemaAsync(DbContext context, string schemaName, CancellationToken cancellationToken = default)
    {
        ValidateSchemaName(schemaName);

        _logger.LogDebug("Creating schema {Schema}", schemaName);

        // Use parameterized approach where possible, but schema names can't be parameterized
        // so we validate strictly instead
        var sql = $"CREATE SCHEMA IF NOT EXISTS \"{EscapeIdentifier(schemaName)}\"";

        await context.Database.ExecuteSqlRawAsync(sql, cancellationToken);

        _logger.LogInformation("Created schema {Schema}", schemaName);
    }

    /// <inheritdoc />
    public async Task DropSchemaAsync(DbContext context, string schemaName, bool cascade = true, CancellationToken cancellationToken = default)
    {
        ValidateSchemaName(schemaName);

        _logger.LogWarning("Dropping schema {Schema} (cascade: {Cascade})", schemaName, cascade);

        var cascadeClause = cascade ? "CASCADE" : "RESTRICT";
        var sql = $"DROP SCHEMA IF EXISTS \"{EscapeIdentifier(schemaName)}\" {cascadeClause}";

        await context.Database.ExecuteSqlRawAsync(sql, cancellationToken);

        _logger.LogInformation("Dropped schema {Schema}", schemaName);
    }

    /// <inheritdoc />
    public async Task<bool> SchemaExistsAsync(DbContext context, string schemaName, CancellationToken cancellationToken = default)
    {
        ValidateSchemaName(schemaName);

        var connection = context.Database.GetDbConnection();
        if (connection.State != System.Data.ConnectionState.Open)
        {
            await connection.OpenAsync(cancellationToken);
        }

        await using var command = connection.CreateCommand();
        command.CommandText = @"
            SELECT EXISTS (
                SELECT 1
                FROM information_schema.schemata
                WHERE schema_name = @schemaName
            )";

        var parameter = command.CreateParameter();
        parameter.ParameterName = "@schemaName";
        parameter.Value = schemaName;
        command.Parameters.Add(parameter);

        var result = await command.ExecuteScalarAsync(cancellationToken);
        return result is true or 1 or "t" or "True";
    }

    /// <inheritdoc />
    public async Task<IEnumerable<string>> GetSchemasAsync(DbContext context, string prefix, CancellationToken cancellationToken = default)
    {
        var connection = context.Database.GetDbConnection();
        if (connection.State != System.Data.ConnectionState.Open)
        {
            await connection.OpenAsync(cancellationToken);
        }

        await using var command = connection.CreateCommand();
        command.CommandText = @"
            SELECT schema_name
            FROM information_schema.schemata
            WHERE schema_name LIKE @prefix || '%'
            ORDER BY schema_name";

        var parameter = command.CreateParameter();
        parameter.ParameterName = "@prefix";
        parameter.Value = prefix;
        command.Parameters.Add(parameter);

        var schemas = new List<string>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            schemas.Add(reader.GetString(0));
        }

        return schemas;
    }

    /// <inheritdoc />
    public async Task SetCurrentSchemaAsync(DbContext context, string schemaName, CancellationToken cancellationToken = default)
    {
        ValidateSchemaName(schemaName);

        var sql = $"SET search_path TO \"{EscapeIdentifier(schemaName)}\", public";
        await context.Database.ExecuteSqlRawAsync(sql, cancellationToken);

        _logger.LogDebug("Set search_path to {Schema}", schemaName);
    }

    /// <inheritdoc />
    public async Task RenameSchemaAsync(DbContext context, string oldName, string newName, CancellationToken cancellationToken = default)
    {
        ValidateSchemaName(oldName);
        ValidateSchemaName(newName);

        _logger.LogInformation("Renaming schema {OldName} to {NewName}", oldName, newName);

        var sql = $"ALTER SCHEMA \"{EscapeIdentifier(oldName)}\" RENAME TO \"{EscapeIdentifier(newName)}\"";
        await context.Database.ExecuteSqlRawAsync(sql, cancellationToken);

        _logger.LogInformation("Renamed schema {OldName} to {NewName}", oldName, newName);
    }

    /// <summary>
    /// Grants usage permission on a schema to a role.
    /// </summary>
    /// <param name="context">The DbContext.</param>
    /// <param name="schemaName">The schema name.</param>
    /// <param name="roleName">The role name.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task GrantSchemaUsageAsync(DbContext context, string schemaName, string roleName, CancellationToken cancellationToken = default)
    {
        ValidateSchemaName(schemaName);
        ValidateIdentifier(roleName, "role");

        var sql = $"GRANT USAGE ON SCHEMA \"{EscapeIdentifier(schemaName)}\" TO \"{EscapeIdentifier(roleName)}\"";
        await context.Database.ExecuteSqlRawAsync(sql, cancellationToken);
    }

    /// <summary>
    /// Grants all privileges on all tables in a schema to a role.
    /// </summary>
    /// <param name="context">The DbContext.</param>
    /// <param name="schemaName">The schema name.</param>
    /// <param name="roleName">The role name.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task GrantAllPrivilegesAsync(DbContext context, string schemaName, string roleName, CancellationToken cancellationToken = default)
    {
        ValidateSchemaName(schemaName);
        ValidateIdentifier(roleName, "role");

        var sql = $"GRANT ALL PRIVILEGES ON ALL TABLES IN SCHEMA \"{EscapeIdentifier(schemaName)}\" TO \"{EscapeIdentifier(roleName)}\"";
        await context.Database.ExecuteSqlRawAsync(sql, cancellationToken);
    }

    /// <summary>
    /// Revokes all privileges from a role on a schema.
    /// </summary>
    /// <param name="context">The DbContext.</param>
    /// <param name="schemaName">The schema name.</param>
    /// <param name="roleName">The role name.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task RevokeAllPrivilegesAsync(DbContext context, string schemaName, string roleName, CancellationToken cancellationToken = default)
    {
        ValidateSchemaName(schemaName);
        ValidateIdentifier(roleName, "role");

        var sql = $"REVOKE ALL PRIVILEGES ON ALL TABLES IN SCHEMA \"{EscapeIdentifier(schemaName)}\" FROM \"{EscapeIdentifier(roleName)}\"";
        await context.Database.ExecuteSqlRawAsync(sql, cancellationToken);
    }

    private static void ValidateSchemaName(string schemaName)
    {
        PostgreSqlIdentifierHelper.ValidateIdentifier(schemaName, "schema");
    }

    private static void ValidateIdentifier(string identifier, string type)
    {
        PostgreSqlIdentifierHelper.ValidateIdentifier(identifier, type);
    }

    private static string EscapeIdentifier(string identifier)
    {
        return PostgreSqlIdentifierHelper.EscapeIdentifier(identifier);
    }
}
