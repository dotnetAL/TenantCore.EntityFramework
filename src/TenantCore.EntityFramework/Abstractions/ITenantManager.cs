namespace TenantCore.EntityFramework.Abstractions;

/// <summary>
/// Manages tenant lifecycle operations including provisioning, archiving, and deletion.
/// </summary>
/// <typeparam name="TKey">The type of the tenant identifier.</typeparam>
public interface ITenantManager<TKey> where TKey : notnull
{
    /// <summary>
    /// Provisions a new tenant, creating all necessary database resources.
    /// </summary>
    /// <param name="tenantId">The unique identifier for the new tenant.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <exception cref="TenantAlreadyExistsException">Thrown if the tenant already exists.</exception>
    Task ProvisionTenantAsync(TKey tenantId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Checks if a tenant's database resources exist.
    /// </summary>
    /// <param name="tenantId">The tenant identifier.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True if the tenant resources exist; otherwise, false.</returns>
    Task<bool> TenantExistsAsync(TKey tenantId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets all existing tenant identifiers from the database.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A collection of tenant schema/resource identifiers.</returns>
    Task<IEnumerable<string>> GetTenantsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes a tenant and optionally all associated data.
    /// </summary>
    /// <param name="tenantId">The tenant identifier.</param>
    /// <param name="hardDelete">If true, permanently deletes all tenant data; if false, performs a soft delete.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <exception cref="TenantNotFoundException">Thrown if the tenant does not exist.</exception>
    Task DeleteTenantAsync(TKey tenantId, bool hardDelete = false, CancellationToken cancellationToken = default);

    /// <summary>
    /// Archives a tenant, backing up data and marking it as inactive.
    /// </summary>
    /// <param name="tenantId">The tenant identifier.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <exception cref="TenantNotFoundException">Thrown if the tenant does not exist.</exception>
    Task ArchiveTenantAsync(TKey tenantId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Restores a previously archived tenant.
    /// </summary>
    /// <param name="tenantId">The tenant identifier.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <exception cref="TenantNotFoundException">Thrown if the archived tenant does not exist.</exception>
    Task RestoreTenantAsync(TKey tenantId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Applies pending migrations to a specific tenant.
    /// </summary>
    /// <param name="tenantId">The tenant identifier.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task MigrateTenantAsync(TKey tenantId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Applies pending migrations to all existing tenants.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task MigrateAllTenantsAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Exception thrown when attempting to create a tenant that already exists.
/// </summary>
public class TenantAlreadyExistsException : Exception
{
    /// <summary>
    /// Gets the tenant identifier that caused the exception.
    /// </summary>
    public object TenantId { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="TenantAlreadyExistsException"/> class.
    /// </summary>
    /// <param name="tenantId">The tenant identifier that already exists.</param>
    public TenantAlreadyExistsException(object tenantId)
        : base($"Tenant '{tenantId}' already exists.")
    {
        TenantId = tenantId;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="TenantAlreadyExistsException"/> class with an inner exception.
    /// </summary>
    /// <param name="tenantId">The tenant identifier that already exists.</param>
    /// <param name="innerException">The exception that caused this exception.</param>
    public TenantAlreadyExistsException(object tenantId, Exception innerException)
        : base($"Tenant '{tenantId}' already exists.", innerException)
    {
        TenantId = tenantId;
    }
}

/// <summary>
/// Exception thrown when a tenant cannot be found.
/// </summary>
public class TenantNotFoundException : Exception
{
    /// <summary>
    /// Gets the tenant identifier that was not found.
    /// </summary>
    public object TenantId { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="TenantNotFoundException"/> class.
    /// </summary>
    /// <param name="tenantId">The tenant identifier that was not found.</param>
    public TenantNotFoundException(object tenantId)
        : base($"Tenant '{tenantId}' was not found.")
    {
        TenantId = tenantId;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="TenantNotFoundException"/> class with an inner exception.
    /// </summary>
    /// <param name="tenantId">The tenant identifier that was not found.</param>
    /// <param name="innerException">The exception that caused this exception.</param>
    public TenantNotFoundException(object tenantId, Exception innerException)
        : base($"Tenant '{tenantId}' was not found.", innerException)
    {
        TenantId = tenantId;
    }
}
