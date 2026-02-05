namespace TenantCore.EntityFramework.PostgreSql.Utilities;

/// <summary>
/// Helper methods for validating and escaping PostgreSQL identifiers.
/// </summary>
internal static class PostgreSqlIdentifierHelper
{
    /// <summary>
    /// Escapes double quotes in a PostgreSQL identifier by doubling them.
    /// </summary>
    /// <param name="identifier">The identifier to escape.</param>
    /// <returns>The escaped identifier.</returns>
    public static string EscapeIdentifier(string identifier)
    {
        return identifier.Replace("\"", "\"\"");
    }

    /// <summary>
    /// Validates a PostgreSQL identifier for safety.
    /// </summary>
    /// <param name="identifier">The identifier to validate.</param>
    /// <param name="type">The type of identifier (e.g., "schema", "role") for error messages.</param>
    /// <exception cref="ArgumentException">Thrown when the identifier is invalid.</exception>
    public static void ValidateIdentifier(string identifier, string type)
    {
        if (string.IsNullOrWhiteSpace(identifier))
        {
            throw new ArgumentException($"{type} name cannot be null or empty.", type);
        }

        if (identifier.Length > 63)
        {
            throw new ArgumentException($"{type} name '{identifier}' exceeds maximum length of 63 characters.", type);
        }

        foreach (var c in identifier)
        {
            if (!char.IsLetterOrDigit(c) && c != '_')
            {
                throw new ArgumentException($"{type} name '{identifier}' contains invalid character '{c}'. Only alphanumeric characters and underscores are allowed.", type);
            }
        }

        if (!char.IsLetter(identifier[0]) && identifier[0] != '_')
        {
            throw new ArgumentException($"{type} name '{identifier}' must start with a letter or underscore.", type);
        }
    }
}
