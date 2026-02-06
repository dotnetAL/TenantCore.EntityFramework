namespace TenantCore.EntityFramework.ControlDb.Entities;

/// <summary>
/// Entity representing a tenant in the control database.
/// </summary>
public class TenantEntity
{
    /// <summary>
    /// Gets or sets the unique identifier for the tenant.
    /// </summary>
    public Guid TenantId { get; set; }

    /// <summary>
    /// Gets or sets the URL-friendly slug for the tenant.
    /// Must be unique across all tenants.
    /// </summary>
    public string TenantSlug { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the current status of the tenant.
    /// </summary>
    public TenantStatus Status { get; set; } = TenantStatus.Pending;

    /// <summary>
    /// Gets or sets the database schema name for this tenant.
    /// Must be unique across all tenants.
    /// </summary>
    public string TenantSchema { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the optional database name if using separate databases.
    /// </summary>
    public string? TenantDatabase { get; set; }

    /// <summary>
    /// Gets or sets the optional database server if using separate servers.
    /// </summary>
    public string? TenantDbServer { get; set; }

    /// <summary>
    /// Gets or sets the optional database user for this tenant.
    /// </summary>
    public string? TenantDbUser { get; set; }

    /// <summary>
    /// Gets or sets the encrypted database password.
    /// Stored encrypted using <see cref="ITenantPasswordProtector"/>.
    /// </summary>
    public string? TenantDbPasswordEncrypted { get; set; }

    /// <summary>
    /// Gets or sets the SHA-256 hash of the API key (64 lowercase hex characters).
    /// </summary>
    public string? TenantApiKeyHash { get; set; }

    /// <summary>
    /// Gets or sets the timestamp when the tenant was created.
    /// </summary>
    public DateTime CreatedAt { get; set; }

    /// <summary>
    /// Gets or sets the timestamp when the tenant was last updated.
    /// </summary>
    public DateTime UpdatedAt { get; set; }

    /// <summary>
    /// Converts this entity to a <see cref="TenantRecord"/>.
    /// </summary>
    /// <returns>A read-only projection of this entity.</returns>
    public TenantRecord ToRecord() => new(
        TenantId,
        TenantSlug,
        Status,
        TenantSchema,
        TenantDatabase,
        TenantDbServer,
        TenantDbUser,
        CreatedAt,
        UpdatedAt);
}
