namespace TenantCore.EntityFramework.Attributes;

/// <summary>
/// Marks an entity as shared across all tenants.
/// Entities with this attribute will be placed in the shared schema instead of tenant schemas.
/// </summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false, AllowMultiple = false)]
public sealed class SharedEntityAttribute : Attribute
{
    /// <summary>
    /// Gets or sets the schema name for this shared entity.
    /// If null, the default shared schema will be used.
    /// </summary>
    public string? Schema { get; set; }

    /// <summary>
    /// Gets or sets the table name for this shared entity.
    /// If null, the default table naming convention will be used.
    /// </summary>
    public string? TableName { get; set; }

    public SharedEntityAttribute()
    {
    }

    public SharedEntityAttribute(string schema)
    {
        Schema = schema;
    }
}
