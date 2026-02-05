using System.Text.RegularExpressions;

namespace TenantCore.EntityFramework.Configuration;

/// <summary>
/// Configuration options for schema-per-tenant isolation strategy.
/// </summary>
public partial class SchemaPerTenantOptions
{
    /// <summary>
    /// Gets or sets the prefix for tenant schema names.
    /// Default is "tenant_".
    /// </summary>
    public string SchemaPrefix { get; set; } = "tenant_";

    /// <summary>
    /// Gets or sets the name of the shared schema for cross-tenant entities.
    /// Default is "public".
    /// </summary>
    public string SharedSchema { get; set; } = "public";

    /// <summary>
    /// Gets or sets whether to create the schema if it doesn't exist on first access.
    /// </summary>
    public bool CreateSchemaOnMissing { get; set; } = false;

    /// <summary>
    /// Gets or sets the prefix for archived tenant schemas.
    /// </summary>
    public string ArchivedSchemaPrefix { get; set; } = "archived_";

    /// <summary>
    /// Gets or sets whether to validate schema names for SQL injection.
    /// </summary>
    public bool ValidateSchemaNames { get; set; } = true;

    /// <summary>
    /// Gets or sets the maximum length for schema names. Default is 63 (PostgreSQL limit).
    /// </summary>
    public int MaxSchemaNameLength { get; set; } = 63;

    /// <summary>
    /// Gets or sets a custom schema name generator function.
    /// If null, uses the default pattern: {SchemaPrefix}{tenantId}
    /// </summary>
    public Func<object, string>? SchemaNameGenerator { get; set; }

    /// <summary>
    /// Generates a schema name for the given tenant identifier.
    /// </summary>
    /// <param name="tenantId">The tenant identifier.</param>
    /// <returns>The generated schema name.</returns>
    public string GenerateSchemaName(object tenantId)
    {
        var schemaName = SchemaNameGenerator?.Invoke(tenantId)
            ?? $"{SchemaPrefix}{SanitizeTenantId(tenantId)}";

        if (ValidateSchemaNames)
        {
            ValidateSchemaName(schemaName);
        }

        return schemaName;
    }

    /// <summary>
    /// Extracts the tenant identifier from a schema name.
    /// </summary>
    /// <param name="schemaName">The schema name.</param>
    /// <returns>The tenant identifier portion of the schema name.</returns>
    public string ExtractTenantId(string schemaName)
    {
        if (schemaName.StartsWith(SchemaPrefix, StringComparison.OrdinalIgnoreCase))
        {
            return schemaName[SchemaPrefix.Length..];
        }

        return schemaName;
    }

    private static string SanitizeTenantId(object tenantId)
    {
        var idString = tenantId.ToString() ?? string.Empty;
        // Replace any characters that aren't alphanumeric or underscore
        return SanitizeRegex().Replace(idString.ToLowerInvariant(), "_");
    }

    private void ValidateSchemaName(string schemaName)
    {
        if (string.IsNullOrWhiteSpace(schemaName))
        {
            throw new ArgumentException("Schema name cannot be null or empty.", nameof(schemaName));
        }

        if (schemaName.Length > MaxSchemaNameLength)
        {
            throw new ArgumentException($"Schema name '{schemaName}' exceeds maximum length of {MaxSchemaNameLength} characters.", nameof(schemaName));
        }

        if (!ValidSchemaNameRegex().IsMatch(schemaName))
        {
            throw new ArgumentException($"Schema name '{schemaName}' contains invalid characters. Only alphanumeric characters and underscores are allowed.", nameof(schemaName));
        }

        // Check for SQL keywords that could be problematic
        var upperName = schemaName.ToUpperInvariant();
        if (ReservedKeywords.Contains(upperName))
        {
            throw new ArgumentException($"Schema name '{schemaName}' is a reserved SQL keyword.", nameof(schemaName));
        }
    }

    [GeneratedRegex(@"[^a-z0-9_]")]
    private static partial Regex SanitizeRegex();

    [GeneratedRegex(@"^[a-z_][a-z0-9_]*$", RegexOptions.IgnoreCase)]
    private static partial Regex ValidSchemaNameRegex();

    private static readonly HashSet<string> ReservedKeywords = new(StringComparer.OrdinalIgnoreCase)
    {
        "PUBLIC", "INFORMATION_SCHEMA", "PG_CATALOG", "PG_TOAST", "PG_TEMP"
    };
}
