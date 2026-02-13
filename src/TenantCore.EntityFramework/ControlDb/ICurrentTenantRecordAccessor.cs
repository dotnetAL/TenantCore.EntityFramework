namespace TenantCore.EntityFramework.ControlDb;

/// <summary>
/// Provides lazy, cached access to the <see cref="TenantRecord"/> for the current tenant.
/// Scoped lifetime: one instance per request, fetches from <see cref="ITenantStore"/> on first access
/// and caches the result for the scope lifetime.
/// </summary>
public interface ICurrentTenantRecordAccessor
{
    /// <summary>
    /// Gets the <see cref="TenantRecord"/> for the currently resolved tenant.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>
    /// The tenant record, or <c>null</c> when no tenant is resolved,
    /// no <see cref="ITenantStore"/> is registered, or the tenant is not found in the store.
    /// </returns>
    Task<TenantRecord?> GetCurrentTenantRecordAsync(CancellationToken cancellationToken = default);
}
