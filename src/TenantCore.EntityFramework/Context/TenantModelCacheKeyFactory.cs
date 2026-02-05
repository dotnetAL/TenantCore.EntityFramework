using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace TenantCore.EntityFramework.Context;

/// <summary>
/// Custom model cache key factory that includes the tenant schema in the cache key.
/// This ensures that EF Core creates a separate compiled model for each schema,
/// which is required for schema-per-tenant isolation to work correctly.
/// </summary>
/// <remarks>
/// Without this factory, EF Core would cache the model once and reuse it for all tenants,
/// causing queries to always target the schema that was used when the model was first built.
/// </remarks>
/// <typeparam name="TKey">The type of the tenant identifier.</typeparam>
public class TenantModelCacheKeyFactory<TKey> : IModelCacheKeyFactory where TKey : notnull
{
    /// <inheritdoc />
    public object Create(DbContext context, bool designTime)
    {
        if (context is TenantDbContext<TKey> tenantContext)
        {
            var schema = tenantContext.CurrentTenantSchema ?? "public";
            return (context.GetType(), schema, designTime);
        }

        return (context.GetType(), designTime);
    }
}
