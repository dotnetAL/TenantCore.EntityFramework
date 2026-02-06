namespace TenantCore.EntityFramework.Utilities;

/// <summary>
/// Parses string representations of tenant identifiers to their strongly-typed equivalents.
/// </summary>
/// <typeparam name="TKey">The type of the tenant identifier.</typeparam>
internal static class TenantKeyParser<TKey> where TKey : notnull
{
    /// <summary>
    /// Parses a string value to the tenant key type.
    /// </summary>
    /// <param name="value">The string value to parse.</param>
    /// <returns>The parsed tenant key.</returns>
    /// <exception cref="NotSupportedException">Thrown when the tenant key type is not supported.</exception>
    /// <exception cref="FormatException">Thrown when the value cannot be parsed to the target type.</exception>
    /// <exception cref="OverflowException">Thrown when the value is outside the range of the target type.</exception>
    public static TKey Parse(string value)
    {
        var type = typeof(TKey);

        if (type == typeof(string))
            return (TKey)(object)value;

        if (type == typeof(Guid))
            return (TKey)(object)Guid.Parse(value);

        if (type == typeof(int))
            return (TKey)(object)int.Parse(value);

        if (type == typeof(long))
            return (TKey)(object)long.Parse(value);

        throw new NotSupportedException($"Tenant key type {type.Name} is not supported for parsing");
    }
}
