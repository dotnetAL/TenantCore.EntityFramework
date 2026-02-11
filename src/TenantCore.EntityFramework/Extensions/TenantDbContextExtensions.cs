using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TenantCore.EntityFramework.Abstractions;
using TenantCore.EntityFramework.Context;

namespace TenantCore.EntityFramework.Extensions;

/// <summary>
/// Extension methods for <see cref="TenantDbContext{TKey}"/>.
/// </summary>
public static class TenantDbContextExtensions
{
    /// <summary>
    /// Ensures the tenant's schema exists and applies any pending EF Core migrations.
    /// The context must already have a tenant schema set (via <see cref="TenantDbContext{TKey}.CurrentTenantSchema"/>).
    /// </summary>
    /// <typeparam name="TKey">The type of the tenant identifier.</typeparam>
    /// <param name="context">The tenant DbContext.</param>
    /// <param name="serviceProvider">The service provider to resolve <see cref="ISchemaManager"/>.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <exception cref="InvalidOperationException">Thrown when no tenant schema is set on the context.</exception>
    public static async Task MigrateTenantAsync<TKey>(
        this TenantDbContext<TKey> context,
        IServiceProvider serviceProvider,
        CancellationToken cancellationToken = default) where TKey : notnull
    {
        ArgumentNullException.ThrowIfNull(serviceProvider);

        var schema = context.CurrentTenantSchema;
        if (string.IsNullOrEmpty(schema))
        {
            throw new InvalidOperationException(
                "No tenant schema is set on the context. Ensure a tenant context has been established before calling MigrateTenantAsync.");
        }

        var schemaManager = serviceProvider.GetRequiredService<ISchemaManager>();
        await schemaManager.CreateSchemaAsync(context, schema, cancellationToken);
        await context.Database.MigrateAsync(cancellationToken);
    }
}
