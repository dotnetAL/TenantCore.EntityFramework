using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TenantCore.EntityFramework.Abstractions;
using TenantCore.EntityFramework.Configuration;
using TenantCore.EntityFramework.Context;

namespace TenantCore.EntityFramework.Extensions;

/// <summary>
/// Extension methods on <see cref="IServiceProvider"/> for obtaining tenant-scoped DbContexts.
/// </summary>
public static class ServiceProviderTenantExtensions
{
    /// <summary>
    /// Sets the tenant context for the specified tenant and returns a scope containing a
    /// tenant-scoped <typeparamref name="TContext"/>. Disposing the scope disposes the
    /// DbContext and restores the previous tenant context.
    /// </summary>
    /// <typeparam name="TContext">The DbContext type.</typeparam>
    /// <typeparam name="TKey">The type of the tenant identifier.</typeparam>
    /// <param name="serviceProvider">The service provider.</param>
    /// <param name="tenantId">The tenant identifier.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>An async-disposable scope whose <see cref="TenantDbContextScope{TContext}.Context"/>
    /// property is the tenant-scoped DbContext.</returns>
    public static async Task<TenantDbContextScope<TContext>> GetTenantDbContextAsync<TContext, TKey>(
        this IServiceProvider serviceProvider,
        TKey tenantId,
        CancellationToken cancellationToken = default)
        where TContext : TenantDbContext<TKey>
        where TKey : notnull
    {
        ArgumentNullException.ThrowIfNull(serviceProvider);
        ArgumentNullException.ThrowIfNull(tenantId);

        var accessor = serviceProvider.GetRequiredService<ITenantContextAccessor<TKey>>();
        var options = serviceProvider.GetRequiredService<TenantCoreOptions>();
        var contextFactory = serviceProvider.GetRequiredService<IDbContextFactory<TContext>>();

        var schemaName = options.SchemaPerTenant.GenerateSchemaName(tenantId);
        var tenantScope = new TenantScope<TKey>(accessor, tenantId, schemaName);

        TContext context;
        try
        {
            context = await contextFactory.CreateDbContextAsync(cancellationToken);
        }
        catch
        {
            tenantScope.Dispose();
            throw;
        }

        return new TenantDbContextScope<TContext>(context, tenantScope);
    }
}
