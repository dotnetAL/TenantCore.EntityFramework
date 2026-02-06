using Microsoft.Extensions.Caching.Memory;
using TenantCore.EntityFramework.Configuration;

namespace TenantCore.EntityFramework.ControlDb;

/// <summary>
/// Decorator that adds caching to an <see cref="ITenantStore"/> implementation.
/// </summary>
/// <remarks>
/// This decorator caches tenant records by ID and slug.
/// For security, passwords are never cached - <see cref="GetTenantPasswordAsync"/> always
/// delegates directly to the inner store.
/// API key verification is not cached since it involves salted hashes and must verify
/// against all stored hashes.
/// Cache entries are invalidated on create, update, and delete operations.
/// </remarks>
public class CachedTenantStore : ITenantStore
{
    private readonly ITenantStore _inner;
    private readonly IMemoryCache _cache;
    private readonly ControlDbOptions _options;

    private const string CacheKeyPrefix = "ControlDb:";
    private const string TenantsKeyPrefix = CacheKeyPrefix + "Tenants:";
    private const string TenantKeyPrefix = CacheKeyPrefix + "Tenant:";
    private const string TenantBySlugKeyPrefix = CacheKeyPrefix + "TenantBySlug:";

    /// <summary>
    /// Initializes a new instance of the <see cref="CachedTenantStore"/> class.
    /// </summary>
    /// <param name="inner">The inner tenant store to wrap.</param>
    /// <param name="cache">The memory cache.</param>
    /// <param name="options">The control database options.</param>
    public CachedTenantStore(ITenantStore inner, IMemoryCache cache, ControlDbOptions options)
    {
        _inner = inner;
        _cache = cache;
        _options = options;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<TenantRecord>> GetTenantsAsync(
        TenantStatus[]? statuses = null,
        CancellationToken cancellationToken = default)
    {
        var cacheKey = GetTenantsCacheKey(statuses);

        if (_cache.TryGetValue(cacheKey, out IReadOnlyList<TenantRecord>? cached) && cached != null)
        {
            return cached;
        }

        var result = await _inner.GetTenantsAsync(statuses, cancellationToken);

        _cache.Set(cacheKey, result, _options.CacheDuration);

        return result;
    }

    /// <inheritdoc />
    public async Task<TenantRecord?> GetTenantAsync(Guid tenantId, CancellationToken cancellationToken = default)
    {
        var cacheKey = $"{TenantKeyPrefix}{tenantId}";

        if (_cache.TryGetValue(cacheKey, out TenantRecord? cached))
        {
            return cached;
        }

        var result = await _inner.GetTenantAsync(tenantId, cancellationToken);

        if (result != null)
        {
            _cache.Set(cacheKey, result, _options.CacheDuration);
        }

        return result;
    }

    /// <inheritdoc />
    public async Task<TenantRecord?> GetTenantBySlugAsync(string slug, CancellationToken cancellationToken = default)
    {
        var cacheKey = $"{TenantBySlugKeyPrefix}{slug}";

        if (_cache.TryGetValue(cacheKey, out TenantRecord? cached))
        {
            return cached;
        }

        var result = await _inner.GetTenantBySlugAsync(slug, cancellationToken);

        if (result != null)
        {
            _cache.Set(cacheKey, result, _options.CacheDuration);
        }

        return result;
    }

    /// <inheritdoc />
    public Task<TenantRecord?> GetTenantByApiKeyAsync(string apiKey, CancellationToken cancellationToken = default)
    {
        // API key verification is not cached for security reasons:
        // 1. Salted hashes mean we can't use the API key as a cache key
        // 2. Caching would require storing the plaintext API key
        // 3. Each verification must check against all stored hashes
        return _inner.GetTenantByApiKeyAsync(apiKey, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<TenantRecord> CreateTenantAsync(
        Guid tenantId,
        CreateTenantRequest request,
        CancellationToken cancellationToken = default)
    {
        var result = await _inner.CreateTenantAsync(tenantId, request, cancellationToken);

        InvalidateAllCaches();
        CacheTenant(result);

        return result;
    }

    /// <inheritdoc />
    public async Task<TenantRecord> UpdateTenantAsync(
        Guid tenantId,
        string? slug = null,
        string? database = null,
        string? dbServer = null,
        string? dbUser = null,
        string? dbPassword = null,
        string? apiKey = null,
        CancellationToken cancellationToken = default)
    {
        // Get old record to invalidate old slug cache
        var oldRecord = await _inner.GetTenantAsync(tenantId, cancellationToken);

        var result = await _inner.UpdateTenantAsync(
            tenantId, slug, database, dbServer, dbUser, dbPassword, apiKey, cancellationToken);

        InvalidateTenantCaches(tenantId, oldRecord?.TenantSlug);
        CacheTenant(result);

        return result;
    }

    /// <inheritdoc />
    public async Task<TenantRecord> UpdateStatusAsync(
        Guid tenantId,
        TenantStatus status,
        CancellationToken cancellationToken = default)
    {
        var result = await _inner.UpdateStatusAsync(tenantId, status, cancellationToken);

        InvalidateTenantCaches(tenantId, result.TenantSlug);
        CacheTenant(result);

        return result;
    }

    /// <inheritdoc />
    public async Task<bool> DeleteTenantAsync(Guid tenantId, CancellationToken cancellationToken = default)
    {
        // Get old record to invalidate slug cache
        var oldRecord = await _inner.GetTenantAsync(tenantId, cancellationToken);

        var result = await _inner.DeleteTenantAsync(tenantId, cancellationToken);

        if (result)
        {
            InvalidateTenantCaches(tenantId, oldRecord?.TenantSlug);
        }

        return result;
    }

    /// <inheritdoc />
    public Task<string?> GetTenantPasswordAsync(Guid tenantId, CancellationToken cancellationToken = default)
    {
        // Never cache passwords
        return _inner.GetTenantPasswordAsync(tenantId, cancellationToken);
    }

    private void CacheTenant(TenantRecord record)
    {
        _cache.Set($"{TenantKeyPrefix}{record.TenantId}", record, _options.CacheDuration);
        _cache.Set($"{TenantBySlugKeyPrefix}{record.TenantSlug}", record, _options.CacheDuration);
    }

    private void InvalidateTenantCaches(Guid tenantId, string? slug)
    {
        _cache.Remove($"{TenantKeyPrefix}{tenantId}");

        if (slug != null)
        {
            _cache.Remove($"{TenantBySlugKeyPrefix}{slug}");
        }

        // Invalidate all list caches since tenant data changed
        InvalidateAllCaches();
    }

    private void InvalidateAllCaches()
    {
        // IMemoryCache doesn't support pattern-based removal, so we rely on cache expiration
        // For more aggressive invalidation, consider using a cache with tagging support
        // or maintaining a list of known cache keys

        // Remove known status combinations
        foreach (TenantStatus status in Enum.GetValues<TenantStatus>())
        {
            _cache.Remove($"{TenantsKeyPrefix}{status}");
        }

        // Remove the "all tenants" key
        _cache.Remove($"{TenantsKeyPrefix}all");
    }

    private static string GetTenantsCacheKey(TenantStatus[]? statuses)
    {
        if (statuses == null || statuses.Length == 0)
        {
            return $"{TenantsKeyPrefix}all";
        }

        var sortedStatuses = statuses.OrderBy(s => s).Select(s => ((int)s).ToString());
        return $"{TenantsKeyPrefix}{string.Join(",", sortedStatuses)}";
    }
}
