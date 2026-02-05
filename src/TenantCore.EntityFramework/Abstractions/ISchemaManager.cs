using Microsoft.EntityFrameworkCore;

namespace TenantCore.EntityFramework.Abstractions;

/// <summary>
/// Manages database schema operations for tenant isolation.
/// </summary>
public interface ISchemaManager
{
    /// <summary>
    /// Creates a new schema in the database.
    /// </summary>
    /// <param name="context">A DbContext instance for database access.</param>
    /// <param name="schemaName">The name of the schema to create.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task CreateSchemaAsync(DbContext context, string schemaName, CancellationToken cancellationToken = default);

    /// <summary>
    /// Drops a schema and all its objects from the database.
    /// </summary>
    /// <param name="context">A DbContext instance for database access.</param>
    /// <param name="schemaName">The name of the schema to drop.</param>
    /// <param name="cascade">If true, drops all objects in the schema first.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task DropSchemaAsync(DbContext context, string schemaName, bool cascade = true, CancellationToken cancellationToken = default);

    /// <summary>
    /// Checks if a schema exists in the database.
    /// </summary>
    /// <param name="context">A DbContext instance for database access.</param>
    /// <param name="schemaName">The name of the schema to check.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True if the schema exists; otherwise, false.</returns>
    Task<bool> SchemaExistsAsync(DbContext context, string schemaName, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets all schema names matching the tenant pattern.
    /// </summary>
    /// <param name="context">A DbContext instance for database access.</param>
    /// <param name="prefix">The schema name prefix to filter by.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A collection of schema names.</returns>
    Task<IEnumerable<string>> GetSchemasAsync(DbContext context, string prefix, CancellationToken cancellationToken = default);

    /// <summary>
    /// Sets the search path / current schema for the connection.
    /// </summary>
    /// <param name="context">A DbContext instance for database access.</param>
    /// <param name="schemaName">The schema name to set as current.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task SetCurrentSchemaAsync(DbContext context, string schemaName, CancellationToken cancellationToken = default);

    /// <summary>
    /// Renames a schema.
    /// </summary>
    /// <param name="context">A DbContext instance for database access.</param>
    /// <param name="oldName">The current schema name.</param>
    /// <param name="newName">The new schema name.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task RenameSchemaAsync(DbContext context, string oldName, string newName, CancellationToken cancellationToken = default);
}
