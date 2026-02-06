namespace TenantCore.EntityFramework.ControlDb;

/// <summary>
/// Request model for creating a new tenant in the control database.
/// </summary>
/// <param name="TenantSlug">The URL-friendly slug for the tenant. Must be unique.</param>
/// <param name="TenantSchema">The database schema name for this tenant.</param>
/// <param name="TenantDatabase">The optional database name if using separate databases.</param>
/// <param name="TenantDbServer">The optional database server if using separate servers.</param>
/// <param name="TenantDbUser">The optional database user for this tenant.</param>
/// <param name="TenantDbPassword">The optional plaintext database password (will be encrypted by the store).</param>
/// <param name="TenantApiKey">The optional plaintext API key (will be hashed by the store).</param>
public record CreateTenantRequest(
    string TenantSlug,
    string TenantSchema,
    string? TenantDatabase = null,
    string? TenantDbServer = null,
    string? TenantDbUser = null,
    string? TenantDbPassword = null,
    string? TenantApiKey = null);
