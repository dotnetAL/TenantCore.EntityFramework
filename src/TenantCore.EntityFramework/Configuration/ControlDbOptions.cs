using TenantCore.EntityFramework.ControlDb;

namespace TenantCore.EntityFramework.Configuration;

/// <summary>
/// Configuration options for the tenant control database.
/// </summary>
public class ControlDbOptions
{
    /// <summary>
    /// Gets or sets whether the control database feature is enabled.
    /// </summary>
    public bool Enabled { get; set; }

    /// <summary>
    /// Gets or sets the connection string for the control database.
    /// </summary>
    public string? ConnectionString { get; set; }

    /// <summary>
    /// Gets or sets the schema name for the control database tables.
    /// </summary>
    public string Schema { get; set; } = "tenant_control";

    /// <summary>
    /// Gets or sets whether to apply control database migrations on startup.
    /// </summary>
    public bool ApplyMigrationsOnStartup { get; set; } = true;

    /// <summary>
    /// Gets or sets the assembly containing control database migrations.
    /// If not specified, defaults to "TenantCore.EntityFramework.PostgreSql" for PostgreSQL.
    /// </summary>
    public string? MigrationsAssembly { get; set; }

    /// <summary>
    /// Gets or sets whether to enable caching for tenant store operations.
    /// </summary>
    public bool EnableCaching { get; set; } = true;

    /// <summary>
    /// Gets or sets the cache duration for tenant store operations.
    /// </summary>
    public TimeSpan CacheDuration { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>
    /// Gets or sets the tenant statuses that should be included in migrations.
    /// Only tenants with these statuses will have migrations applied.
    /// </summary>
    public TenantStatus[] MigratableStatuses { get; set; } = [TenantStatus.Pending, TenantStatus.Active];

    /// <summary>
    /// Internal: Type of custom tenant store if using BYO implementation.
    /// </summary>
    internal Type? CustomTenantStoreType { get; set; }
}
