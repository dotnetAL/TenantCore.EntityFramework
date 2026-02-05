namespace TenantCore.EntityFramework.Configuration;

/// <summary>
/// Configuration options for TenantCore.
/// </summary>
public class TenantCoreOptions
{
    /// <summary>
    /// Gets or sets the default connection string for the database.
    /// </summary>
    public string? ConnectionString { get; set; }

    /// <summary>
    /// Gets or sets the options for schema-per-tenant isolation.
    /// </summary>
    public SchemaPerTenantOptions SchemaPerTenant { get; set; } = new();

    /// <summary>
    /// Gets or sets the migration options.
    /// </summary>
    public MigrationOptions Migrations { get; set; } = new();

    /// <summary>
    /// Gets or sets the behavior when tenant resolution fails.
    /// </summary>
    public TenantNotFoundBehavior TenantNotFoundBehavior { get; set; } = TenantNotFoundBehavior.Throw;

    /// <summary>
    /// Gets or sets whether to enable tenant caching.
    /// </summary>
    public bool EnableTenantCaching { get; set; } = true;

    /// <summary>
    /// Gets or sets the tenant cache duration.
    /// </summary>
    public TimeSpan TenantCacheDuration { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>
    /// Gets or sets whether to validate tenants after resolution.
    /// </summary>
    public bool ValidateTenantOnResolution { get; set; } = true;

    /// <summary>
    /// Gets or sets whether to automatically provision tenant resources on first access.
    /// </summary>
    public bool AutoProvisionTenant { get; set; } = false;

    internal List<Type> TenantResolverTypes { get; } = new();
    internal List<Type> TenantSeederTypes { get; } = new();
    internal Type? TenantStrategyType { get; set; }
    internal Type? TenantValidatorType { get; set; }
}

/// <summary>
/// Defines the behavior when a tenant cannot be resolved.
/// </summary>
public enum TenantNotFoundBehavior
{
    /// <summary>
    /// Throws an exception when tenant is not found.
    /// </summary>
    Throw,

    /// <summary>
    /// Returns null and allows the operation to continue.
    /// </summary>
    ReturnNull,

    /// <summary>
    /// Uses a default tenant when the requested tenant is not found.
    /// </summary>
    UseDefault
}
