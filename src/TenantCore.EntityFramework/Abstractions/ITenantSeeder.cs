using Microsoft.EntityFrameworkCore;

namespace TenantCore.EntityFramework.Abstractions;

/// <summary>
/// Provides seed data for newly provisioned tenants.
/// </summary>
public interface ITenantSeeder
{
    /// <summary>
    /// Seeds initial data for a newly provisioned tenant.
    /// </summary>
    /// <param name="context">The DbContext scoped to the tenant.</param>
    /// <param name="tenantId">The tenant identifier.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task SeedAsync(DbContext context, object tenantId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the order in which this seeder should run relative to others.
    /// Lower values run first.
    /// </summary>
    int Order => 0;
}

/// <summary>
/// Strongly-typed tenant seeder.
/// </summary>
/// <typeparam name="TKey">The type of the tenant identifier.</typeparam>
public interface ITenantSeeder<TKey> : ITenantSeeder where TKey : notnull
{
    /// <summary>
    /// Seeds initial data for a newly provisioned tenant.
    /// </summary>
    /// <param name="context">The DbContext scoped to the tenant.</param>
    /// <param name="tenantId">The tenant identifier.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task SeedAsync(DbContext context, TKey tenantId, CancellationToken cancellationToken = default);
}

/// <summary>
/// Typed tenant seeder that works with a specific DbContext type.
/// </summary>
/// <typeparam name="TContext">The DbContext type.</typeparam>
/// <typeparam name="TKey">The type of the tenant identifier.</typeparam>
public interface ITenantSeeder<TContext, TKey> : ITenantSeeder<TKey>
    where TContext : DbContext
    where TKey : notnull
{
    /// <summary>
    /// Seeds initial data for a newly provisioned tenant.
    /// </summary>
    /// <param name="context">The DbContext scoped to the tenant.</param>
    /// <param name="tenantId">The tenant identifier.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task SeedAsync(TContext context, TKey tenantId, CancellationToken cancellationToken = default);
}
