namespace TenantCore.EntityFramework.ControlDb;

/// <summary>
/// Interface for tenant storage operations. Implement this interface for custom tenant store implementations (BYO).
/// </summary>
public interface ITenantStore
{
    /// <summary>
    /// Gets all tenants with the specified statuses.
    /// </summary>
    /// <param name="statuses">The statuses to filter by. If empty, returns all tenants.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A collection of tenant records.</returns>
    Task<IReadOnlyList<TenantRecord>> GetTenantsAsync(
        TenantStatus[]? statuses = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets a tenant by its unique identifier.
    /// </summary>
    /// <param name="tenantId">The tenant identifier.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The tenant record, or null if not found.</returns>
    Task<TenantRecord?> GetTenantAsync(Guid tenantId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets a tenant by its slug.
    /// </summary>
    /// <param name="slug">The tenant slug.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The tenant record, or null if not found.</returns>
    Task<TenantRecord?> GetTenantBySlugAsync(string slug, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets a tenant by verifying the provided API key against stored hashes.
    /// </summary>
    /// <param name="apiKey">The plaintext API key to verify.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The tenant record if the API key is valid, or null if not found or invalid.</returns>
    Task<TenantRecord?> GetTenantByApiKeyAsync(string apiKey, CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates a new tenant.
    /// </summary>
    /// <param name="tenantId">The unique identifier for the new tenant.</param>
    /// <param name="request">The tenant creation request.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The created tenant record.</returns>
    Task<TenantRecord> CreateTenantAsync(
        Guid tenantId,
        CreateTenantRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Updates an existing tenant.
    /// </summary>
    /// <param name="tenantId">The tenant identifier.</param>
    /// <param name="slug">The new slug (or null to keep existing).</param>
    /// <param name="database">The new database (or null to keep existing).</param>
    /// <param name="dbServer">The new database server (or null to keep existing).</param>
    /// <param name="dbUser">The new database user (or null to keep existing).</param>
    /// <param name="dbPassword">The new database password in plaintext (or null to keep existing).</param>
    /// <param name="apiKey">The new API key in plaintext (or null to keep existing).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The updated tenant record.</returns>
    Task<TenantRecord> UpdateTenantAsync(
        Guid tenantId,
        string? slug = null,
        string? database = null,
        string? dbServer = null,
        string? dbUser = null,
        string? dbPassword = null,
        string? apiKey = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Updates the status of a tenant.
    /// </summary>
    /// <param name="tenantId">The tenant identifier.</param>
    /// <param name="status">The new status.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The updated tenant record.</returns>
    Task<TenantRecord> UpdateStatusAsync(
        Guid tenantId,
        TenantStatus status,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes a tenant from the control database.
    /// </summary>
    /// <param name="tenantId">The tenant identifier.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True if the tenant was deleted, false if not found.</returns>
    Task<bool> DeleteTenantAsync(Guid tenantId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the decrypted database password for a tenant.
    /// </summary>
    /// <param name="tenantId">The tenant identifier.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The decrypted password, or null if not set or tenant not found.</returns>
    Task<string?> GetTenantPasswordAsync(Guid tenantId, CancellationToken cancellationToken = default);
}
