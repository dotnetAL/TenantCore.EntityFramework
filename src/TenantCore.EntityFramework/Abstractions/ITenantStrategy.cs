using Microsoft.EntityFrameworkCore;

namespace TenantCore.EntityFramework.Abstractions;

/// <summary>
/// Defines a strongly-typed strategy for tenant isolation in the database.
/// </summary>
/// <typeparam name="TKey">The type of the tenant identifier.</typeparam>
public interface ITenantStrategy<TKey> where TKey : notnull
{
    /// <summary>
    /// Gets the name of this strategy.
    /// </summary>
    string Name { get; }

    /// <summary>
    /// Configures the DbContext options for the current tenant.
    /// </summary>
    /// <param name="optionsBuilder">The options builder to configure.</param>
    /// <param name="tenantId">The current tenant identifier.</param>
    void ConfigureDbContext(DbContextOptionsBuilder optionsBuilder, TKey tenantId);

    /// <summary>
    /// Applies tenant-specific model configuration.
    /// </summary>
    /// <param name="modelBuilder">The model builder to configure.</param>
    /// <param name="tenantId">The current tenant identifier.</param>
    void OnModelCreating(ModelBuilder modelBuilder, TKey tenantId);

    /// <summary>
    /// Provisions the necessary database resources for a new tenant.
    /// </summary>
    /// <param name="context">A DbContext instance for database access.</param>
    /// <param name="tenantId">The tenant identifier.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task ProvisionTenantAsync(DbContext context, TKey tenantId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes tenant resources from the database.
    /// </summary>
    /// <param name="context">A DbContext instance for database access.</param>
    /// <param name="tenantId">The tenant identifier.</param>
    /// <param name="hardDelete">If true, permanently delete all data; otherwise, soft delete.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task DeleteTenantAsync(DbContext context, TKey tenantId, bool hardDelete = false, CancellationToken cancellationToken = default);

    /// <summary>
    /// Checks if tenant resources exist.
    /// </summary>
    /// <param name="context">A DbContext instance for database access.</param>
    /// <param name="tenantId">The tenant identifier.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True if tenant resources exist; otherwise, false.</returns>
    Task<bool> TenantExistsAsync(DbContext context, TKey tenantId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets all existing tenant identifiers.
    /// </summary>
    /// <param name="context">A DbContext instance for database access.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A collection of tenant identifiers.</returns>
    Task<IEnumerable<string>> GetTenantsAsync(DbContext context, CancellationToken cancellationToken = default);

    /// <summary>
    /// Archives a tenant by renaming or marking its resources as inactive.
    /// </summary>
    /// <param name="context">A DbContext instance for database access.</param>
    /// <param name="tenantId">The tenant identifier.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task ArchiveTenantAsync(DbContext context, TKey tenantId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Restores a previously archived tenant.
    /// </summary>
    /// <param name="context">A DbContext instance for database access.</param>
    /// <param name="tenantId">The tenant identifier.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task RestoreTenantAsync(DbContext context, TKey tenantId, CancellationToken cancellationToken = default);
}
