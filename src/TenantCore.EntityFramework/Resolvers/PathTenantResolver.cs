using Microsoft.AspNetCore.Http;
using TenantCore.EntityFramework.Abstractions;
using TenantCore.EntityFramework.Utilities;

namespace TenantCore.EntityFramework.Resolvers;

/// <summary>
/// Resolves tenant from URL path segments.
/// </summary>
/// <remarks>
/// Extracts tenant identifier from URL paths like:
/// <list type="bullet">
///   <item>/api/{tenant}/products → tenant at segment index 1</item>
///   <item>/{tenant}/api/products → tenant at segment index 0</item>
///   <item>/v1/tenants/{tenant}/orders → tenant at segment index 2</item>
/// </list>
/// </remarks>
/// <typeparam name="TKey">The type of the tenant identifier.</typeparam>
public class PathTenantResolver<TKey> : ITenantResolver<TKey> where TKey : notnull
{
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly int _segmentIndex;
    private readonly string? _pathPrefix;
    private readonly Func<string, TKey>? _parser;

    /// <summary>
    /// Gets the priority of this resolver.
    /// </summary>
    public int Priority { get; init; } = 125;

    /// <summary>
    /// Creates a new path tenant resolver that extracts tenant from the first path segment.
    /// </summary>
    /// <param name="httpContextAccessor">The HTTP context accessor.</param>
    public PathTenantResolver(IHttpContextAccessor httpContextAccessor)
        : this(httpContextAccessor, segmentIndex: 0)
    {
    }

    /// <summary>
    /// Creates a new path tenant resolver with a specific segment index.
    /// </summary>
    /// <param name="httpContextAccessor">The HTTP context accessor.</param>
    /// <param name="segmentIndex">The zero-based index of the path segment containing the tenant.</param>
    /// <param name="parser">Optional parser to convert string to TKey.</param>
    public PathTenantResolver(
        IHttpContextAccessor httpContextAccessor,
        int segmentIndex,
        Func<string, TKey>? parser = null)
    {
        _httpContextAccessor = httpContextAccessor;
        _segmentIndex = segmentIndex;
        _pathPrefix = null;
        _parser = parser;
    }

    /// <summary>
    /// Creates a new path tenant resolver that extracts tenant from the segment after a prefix.
    /// </summary>
    /// <param name="httpContextAccessor">The HTTP context accessor.</param>
    /// <param name="pathPrefix">The path prefix before the tenant segment (e.g., "/api" or "/v1/tenants").</param>
    /// <param name="parser">Optional parser to convert string to TKey.</param>
    /// <remarks>
    /// The resolver will look for the tenant in the segment immediately after the prefix.
    /// For example, with prefix "/api", a request to "/api/tenant1/products" resolves to "tenant1".
    /// </remarks>
    public PathTenantResolver(
        IHttpContextAccessor httpContextAccessor,
        string pathPrefix,
        Func<string, TKey>? parser = null)
    {
        _httpContextAccessor = httpContextAccessor;
        _segmentIndex = -1;
        _pathPrefix = pathPrefix.TrimEnd('/');
        _parser = parser;
    }

    /// <inheritdoc />
    public Task<TKey?> ResolveTenantAsync(CancellationToken cancellationToken = default)
    {
        var httpContext = _httpContextAccessor.HttpContext;
        if (httpContext == null)
        {
            return Task.FromResult<TKey?>(default);
        }

        var path = httpContext.Request.Path.Value;
        if (string.IsNullOrEmpty(path))
        {
            return Task.FromResult<TKey?>(default);
        }

        var tenantSegment = _pathPrefix != null
            ? ExtractSegmentAfterPrefix(path)
            : ExtractSegmentByIndex(path);

        if (string.IsNullOrEmpty(tenantSegment))
        {
            return Task.FromResult<TKey?>(default);
        }

        try
        {
            var tenantId = _parser != null ? _parser(tenantSegment) : TenantKeyParser<TKey>.Parse(tenantSegment);
            return Task.FromResult<TKey?>(tenantId);
        }
        catch
        {
            return Task.FromResult<TKey?>(default);
        }
    }

    private string? ExtractSegmentByIndex(string path)
    {
        var segments = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (_segmentIndex < 0 || _segmentIndex >= segments.Length)
        {
            return null;
        }

        return segments[_segmentIndex];
    }

    private string? ExtractSegmentAfterPrefix(string path)
    {
        if (_pathPrefix == null)
        {
            return null;
        }

        // Check if path starts with the prefix
        if (!path.StartsWith(_pathPrefix, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        // Get the remaining path after the prefix
        var remainingPath = path[_pathPrefix.Length..];
        if (string.IsNullOrEmpty(remainingPath) || remainingPath[0] != '/')
        {
            return null;
        }

        // Extract the first segment after the prefix
        var segments = remainingPath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        return segments.Length > 0 ? segments[0] : null;
    }
}
