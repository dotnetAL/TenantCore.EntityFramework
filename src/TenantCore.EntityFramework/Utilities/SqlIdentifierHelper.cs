namespace TenantCore.EntityFramework.Utilities;

/// <summary>
/// Helper methods for escaping SQL identifiers.
/// </summary>
internal static class SqlIdentifierHelper
{
    /// <summary>
    /// Escapes double quotes in a SQL identifier by doubling them.
    /// </summary>
    /// <param name="identifier">The identifier to escape.</param>
    /// <returns>The escaped identifier.</returns>
    public static string EscapeDoubleQuotes(string identifier)
    {
        return identifier.Replace("\"", "\"\"");
    }
}
