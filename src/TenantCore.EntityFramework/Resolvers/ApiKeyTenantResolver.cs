using Microsoft.AspNetCore.Http;
using TenantCore.EntityFramework.Abstractions;
using TenantCore.EntityFramework.ControlDb;
using TenantCore.EntityFramework.Utilities;

namespace TenantCore.EntityFramework.Resolvers;

/// <summary>
/// Resolves tenant from an API key header by looking up the hashed key in the tenant store.
/// </summary>
/// <typeparam name="TKey">The type of the tenant identifier.</typeparam>
public class ApiKeyTenantResolver<TKey> : ITenantResolver<TKey> where TKey : notnull
{
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly ITenantStore? _tenantStore;
    private readonly string _headerName;
    private readonly Func<string, TKey>? _parser;

    /// <summary>
    /// Gets the priority of this resolver.
    /// Default is 175 (between Claims at 150 and Path at 125).
    /// </summary>
    public int Priority { get; init; } = 175;

    /// <summary>
    /// Creates a new API key tenant resolver with default header name "X-Api-Key".
    /// </summary>
    /// <param name="httpContextAccessor">The HTTP context accessor.</param>
    /// <param name="tenantStore">The tenant store for API key lookup.</param>
    public ApiKeyTenantResolver(IHttpContextAccessor httpContextAccessor, ITenantStore? tenantStore)
        : this(httpContextAccessor, tenantStore, "X-Api-Key")
    {
    }

    /// <summary>
    /// Creates a new API key tenant resolver with a custom header name.
    /// </summary>
    /// <param name="httpContextAccessor">The HTTP context accessor.</param>
    /// <param name="tenantStore">The tenant store for API key lookup.</param>
    /// <param name="headerName">The header name to extract API key from.</param>
    /// <param name="parser">Optional parser to convert Guid to TKey.</param>
    public ApiKeyTenantResolver(
        IHttpContextAccessor httpContextAccessor,
        ITenantStore? tenantStore,
        string headerName,
        Func<string, TKey>? parser = null)
    {
        _httpContextAccessor = httpContextAccessor;
        _tenantStore = tenantStore;
        _headerName = headerName;
        _parser = parser;
    }

    /// <inheritdoc />
    public async Task<TKey?> ResolveTenantAsync(CancellationToken cancellationToken = default)
    {
        if (_tenantStore == null)
        {
            return default;
        }

        var httpContext = _httpContextAccessor.HttpContext;
        if (httpContext == null)
        {
            return default;
        }

        if (!httpContext.Request.Headers.TryGetValue(_headerName, out var headerValue))
        {
            return default;
        }

        var apiKey = headerValue.FirstOrDefault();
        if (string.IsNullOrEmpty(apiKey))
        {
            return default;
        }

        try
        {
            // Compute SHA-256 hash of the API key
            var apiKeyHash = ApiKeyHasher.ComputeHash(apiKey);

            // Look up tenant by API key hash
            var tenant = await _tenantStore.GetTenantByApiKeyHashAsync(apiKeyHash, cancellationToken);

            // Only return tenant if found and status is Active
            if (tenant == null || tenant.Status != TenantStatus.Active)
            {
                return default;
            }

            // Convert Guid to TKey
            var tenantIdString = tenant.TenantId.ToString();
            var tenantId = _parser != null ? _parser(tenantIdString) : TenantKeyParser<TKey>.Parse(tenantIdString);

            return tenantId;
        }
        catch
        {
            return default;
        }
    }
}
