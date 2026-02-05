using Microsoft.EntityFrameworkCore;

namespace TenantCore.EntityFramework.Abstractions;

/// <summary>
/// Provides seed data for newly provisioned tenants.
/// </summary>
/// <typeparam name="TKey">The type of the tenant identifier.</typeparam>
public interface ITenantSeeder<TKey> where TKey : notnull
{
    /// <summary>
    /// Seeds initial data for a newly provisioned tenant.
    /// </summary>
    /// <param name="context">The DbContext scoped to the tenant.</param>
    /// <param name="tenantId">The tenant identifier.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task SeedAsync(DbContext context, TKey tenantId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the order in which this seeder should run relative to others.
    /// Lower values run first.
    /// </summary>
    int Order => 0;
}
