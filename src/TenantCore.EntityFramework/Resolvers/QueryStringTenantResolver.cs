using Microsoft.AspNetCore.Http;
using TenantCore.EntityFramework.Abstractions;
using TenantCore.EntityFramework.Utilities;

namespace TenantCore.EntityFramework.Resolvers;

/// <summary>
/// Resolves tenant from query string parameter.
/// </summary>
/// <typeparam name="TKey">The type of the tenant identifier.</typeparam>
public class QueryStringTenantResolver<TKey> : ITenantResolver<TKey> where TKey : notnull
{
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly string _parameterName;
    private readonly Func<string, TKey>? _parser;

    /// <summary>
    /// Gets the priority of this resolver.
    /// </summary>
    public int Priority { get; init; } = 25;

    /// <summary>
    /// Creates a new query string tenant resolver with default parameter name "tenant".
    /// </summary>
    /// <param name="httpContextAccessor">The HTTP context accessor.</param>
    public QueryStringTenantResolver(IHttpContextAccessor httpContextAccessor)
        : this(httpContextAccessor, "tenant")
    {
    }

    /// <summary>
    /// Creates a new query string tenant resolver with a custom parameter name.
    /// </summary>
    /// <param name="httpContextAccessor">The HTTP context accessor.</param>
    /// <param name="parameterName">The query parameter name to extract tenant from.</param>
    /// <param name="parser">Optional parser to convert string to TKey.</param>
    public QueryStringTenantResolver(
        IHttpContextAccessor httpContextAccessor,
        string parameterName,
        Func<string, TKey>? parser = null)
    {
        _httpContextAccessor = httpContextAccessor;
        _parameterName = parameterName;
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

        if (!httpContext.Request.Query.TryGetValue(_parameterName, out var queryValue))
        {
            return Task.FromResult<TKey?>(default);
        }

        var value = queryValue.FirstOrDefault();
        if (string.IsNullOrEmpty(value))
        {
            return Task.FromResult<TKey?>(default);
        }

        try
        {
            var tenantId = _parser != null ? _parser(value) : TenantKeyParser<TKey>.Parse(value);
            return Task.FromResult<TKey?>(tenantId);
        }
        catch
        {
            return Task.FromResult<TKey?>(default);
        }
    }

}
