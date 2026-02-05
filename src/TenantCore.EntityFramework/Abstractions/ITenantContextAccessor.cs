namespace TenantCore.EntityFramework.Abstractions;

/// <summary>
/// Provides access to the current tenant context.
/// </summary>
/// <typeparam name="TKey">The type of the tenant identifier.</typeparam>
public interface ITenantContextAccessor<TKey> where TKey : notnull
{
    /// <summary>
    /// Gets the current tenant context.
    /// </summary>
    TenantContext<TKey>? TenantContext { get; }

    /// <summary>
    /// Sets the current tenant context.
    /// </summary>
    /// <param name="context">The tenant context to set.</param>
    void SetTenantContext(TenantContext<TKey>? context);
}

/// <summary>
/// Represents the context for the current tenant.
/// </summary>
/// <typeparam name="TKey">The type of the tenant identifier.</typeparam>
public class TenantContext<TKey> where TKey : notnull
{
    /// <summary>
    /// Gets the tenant identifier.
    /// </summary>
    public TKey TenantId { get; }

    /// <summary>
    /// Gets whether this is a valid tenant context.
    /// </summary>
    public bool IsValid => !EqualityComparer<TKey>.Default.Equals(TenantId, default!);

    /// <summary>
    /// Gets the schema name for this tenant (if using schema-per-tenant strategy).
    /// </summary>
    public string? SchemaName { get; init; }

    /// <summary>
    /// Gets custom properties associated with this tenant context.
    /// </summary>
    public IDictionary<string, object> Properties { get; } = new Dictionary<string, object>();

    /// <summary>
    /// Initializes a new instance of the <see cref="TenantContext{TKey}"/> class.
    /// </summary>
    /// <param name="tenantId">The tenant identifier.</param>
    public TenantContext(TKey tenantId)
    {
        TenantId = tenantId;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="TenantContext{TKey}"/> class with a schema name.
    /// </summary>
    /// <param name="tenantId">The tenant identifier.</param>
    /// <param name="schemaName">The database schema name for this tenant.</param>
    public TenantContext(TKey tenantId, string schemaName) : this(tenantId)
    {
        SchemaName = schemaName;
    }
}
