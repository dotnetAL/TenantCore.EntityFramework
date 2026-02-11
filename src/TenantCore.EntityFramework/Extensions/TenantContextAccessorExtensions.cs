using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TenantCore.EntityFramework.Abstractions;
using TenantCore.EntityFramework.Configuration;
using TenantCore.EntityFramework.Context;

namespace TenantCore.EntityFramework.Extensions;

/// <summary>
/// Extension methods for <see cref="ITenantContextAccessor{TKey}"/> providing convenient
/// DbContext creation and migration operations.
/// </summary>
public static class TenantContextAccessorExtensions
{
    /// <summary>
    /// Gets a DbContext for the current tenant context. A tenant context must already be set.
    /// </summary>
    /// <typeparam name="TContext">The DbContext type.</typeparam>
    /// <typeparam name="TKey">The type of the tenant identifier.</typeparam>
    /// <param name="accessor">The tenant context accessor.</param>
    /// <param name="serviceProvider">The service provider to resolve <see cref="IDbContextFactory{TContext}"/>.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A DbContext scoped to the current tenant.</returns>
    /// <exception cref="InvalidOperationException">Thrown when no tenant context is set.</exception>
    public static async Task<TContext> GetTenantDbContextAsync<TContext, TKey>(
        this ITenantContextAccessor<TKey> accessor,
        IServiceProvider serviceProvider,
        CancellationToken cancellationToken = default)
        where TContext : TenantDbContext<TKey>
        where TKey : notnull
    {
        ArgumentNullException.ThrowIfNull(serviceProvider);

        if (accessor.TenantContext == null)
        {
            throw new InvalidOperationException(
                "No tenant context is set. Call SetTenantContext or use the overload that accepts a tenantId.");
        }

        var contextFactory = serviceProvider.GetRequiredService<IDbContextFactory<TContext>>();
        return await contextFactory.CreateDbContextAsync(cancellationToken);
    }

    /// <summary>
    /// Sets the tenant context for the specified tenant and returns a DbContext for it.
    /// Restores the previous tenant context when the returned context is disposed.
    /// </summary>
    /// <typeparam name="TContext">The DbContext type.</typeparam>
    /// <typeparam name="TKey">The type of the tenant identifier.</typeparam>
    /// <param name="accessor">The tenant context accessor.</param>
    /// <param name="tenantId">The tenant identifier.</param>
    /// <param name="serviceProvider">The service provider.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A DbContext scoped to the specified tenant.</returns>
    public static async Task<TContext> GetTenantDbContextAsync<TContext, TKey>(
        this ITenantContextAccessor<TKey> accessor,
        TKey tenantId,
        IServiceProvider serviceProvider,
        CancellationToken cancellationToken = default)
        where TContext : TenantDbContext<TKey>
        where TKey : notnull
    {
        ArgumentNullException.ThrowIfNull(serviceProvider);

        var options = serviceProvider.GetRequiredService<TenantCoreOptions>();
        var schemaName = options.SchemaPerTenant.GenerateSchemaName(tenantId);

        accessor.SetTenantContext(new TenantContext<TKey>(tenantId, schemaName));

        var contextFactory = serviceProvider.GetRequiredService<IDbContextFactory<TContext>>();
        return await contextFactory.CreateDbContextAsync(cancellationToken);
    }

    /// <summary>
    /// Sets the tenant context, ensures the schema exists, and applies EF Core migrations
    /// for the specified tenant. Restores the previous tenant context in the finally block.
    /// </summary>
    /// <typeparam name="TContext">The DbContext type.</typeparam>
    /// <typeparam name="TKey">The type of the tenant identifier.</typeparam>
    /// <param name="accessor">The tenant context accessor.</param>
    /// <param name="tenantId">The tenant identifier.</param>
    /// <param name="serviceProvider">The service provider.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public static async Task MigrateTenantAsync<TContext, TKey>(
        this ITenantContextAccessor<TKey> accessor,
        TKey tenantId,
        IServiceProvider serviceProvider,
        CancellationToken cancellationToken = default)
        where TContext : TenantDbContext<TKey>
        where TKey : notnull
    {
        ArgumentNullException.ThrowIfNull(serviceProvider);

        var previousContext = accessor.TenantContext;
        try
        {
            var options = serviceProvider.GetRequiredService<TenantCoreOptions>();
            var schemaName = options.SchemaPerTenant.GenerateSchemaName(tenantId);

            accessor.SetTenantContext(new TenantContext<TKey>(tenantId, schemaName));

            var contextFactory = serviceProvider.GetRequiredService<IDbContextFactory<TContext>>();
            await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);

            var schemaManager = serviceProvider.GetRequiredService<ISchemaManager>();
            await schemaManager.CreateSchemaAsync(context, schemaName, cancellationToken);

            await context.Database.MigrateAsync(cancellationToken);
        }
        finally
        {
            accessor.SetTenantContext(previousContext);
        }
    }
}
