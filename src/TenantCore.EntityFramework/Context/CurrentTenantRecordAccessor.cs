using TenantCore.EntityFramework.Abstractions;
using TenantCore.EntityFramework.ControlDb;

namespace TenantCore.EntityFramework.Context;

/// <summary>
/// Scoped implementation of <see cref="ICurrentTenantRecordAccessor"/> that lazy-loads
/// the <see cref="TenantRecord"/> from <see cref="ITenantStore"/> on first access
/// and caches it for the scope lifetime. If the tenant context changes mid-request
/// (e.g. via <see cref="TenantScope{TKey}"/>), the cached record is invalidated and re-fetched.
/// </summary>
/// <typeparam name="TKey">The type of the tenant identifier.</typeparam>
internal sealed class CurrentTenantRecordAccessor<TKey> : ICurrentTenantRecordAccessor
    where TKey : notnull
{
    private readonly ITenantContextAccessor<TKey> _tenantContextAccessor;
    private readonly ITenantStore? _tenantStore;

    private TenantRecord? _cachedRecord;
    private string? _cachedTenantIdString;
    private bool _hasFetched;

    public CurrentTenantRecordAccessor(
        ITenantContextAccessor<TKey> tenantContextAccessor,
        ITenantStore? tenantStore = null)
    {
        _tenantContextAccessor = tenantContextAccessor;
        _tenantStore = tenantStore;
    }

    public async Task<TenantRecord?> GetCurrentTenantRecordAsync(CancellationToken cancellationToken = default)
    {
        if (_tenantStore == null)
            return null;

        var tenantContext = _tenantContextAccessor.TenantContext;
        if (tenantContext == null || !tenantContext.IsValid)
            return null;

        var currentTenantIdString = tenantContext.TenantId.ToString()!;

        // If we've already fetched for this tenant ID, return the cached result
        if (_hasFetched && currentTenantIdString == _cachedTenantIdString)
            return _cachedRecord;

        // Fetch (or re-fetch if tenant changed mid-request)
        _cachedRecord = await _tenantStore.GetTenantBySlugAsync(currentTenantIdString, cancellationToken);
        _cachedTenantIdString = currentTenantIdString;
        _hasFetched = true;

        return _cachedRecord;
    }
}
