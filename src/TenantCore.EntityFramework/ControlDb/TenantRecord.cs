namespace TenantCore.EntityFramework.ControlDb;

/// <summary>
/// Represents a tenant record from the control database.
/// This is a read-only projection of tenant data.
/// </summary>
/// <param name="TenantId">The unique identifier for the tenant.</param>
/// <param name="TenantSlug">The URL-friendly slug for the tenant.</param>
/// <param name="Status">The current status of the tenant.</param>
/// <param name="TenantSchema">The database schema name for this tenant.</param>
/// <param name="TenantDatabase">The optional database name if using separate databases.</param>
/// <param name="TenantDbServer">The optional database server if using separate servers.</param>
/// <param name="TenantDbUser">The optional database user for this tenant.</param>
/// <param name="CreatedAt">The timestamp when the tenant was created.</param>
/// <param name="UpdatedAt">The timestamp when the tenant was last updated.</param>
public record TenantRecord(
    Guid TenantId,
    string TenantSlug,
    TenantStatus Status,
    string TenantSchema,
    string? TenantDatabase,
    string? TenantDbServer,
    string? TenantDbUser,
    DateTime CreatedAt,
    DateTime UpdatedAt);
