using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using TenantCore.EntityFramework.Abstractions;
using TenantCore.EntityFramework.Utilities;

namespace TenantCore.EntityFramework.Resolvers;

/// <summary>
/// Resolves tenant from route values.
/// </summary>
/// <typeparam name="TKey">The type of the tenant identifier.</typeparam>
public class RouteValueTenantResolver<TKey> : ITenantResolver<TKey> where TKey : notnull
{
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly string _routeParameterName;
    private readonly Func<string, TKey>? _parser;

    /// <summary>
    /// Gets the priority of this resolver.
    /// </summary>
    public int Priority { get; init; } = 150;

    /// <summary>
    /// Creates a new route value tenant resolver with default parameter name "tenantId".
    /// </summary>
    /// <param name="httpContextAccessor">The HTTP context accessor.</param>
    public RouteValueTenantResolver(IHttpContextAccessor httpContextAccessor)
        : this(httpContextAccessor, "tenantId")
    {
    }

    /// <summary>
    /// Creates a new route value tenant resolver with a custom parameter name.
    /// </summary>
    /// <param name="httpContextAccessor">The HTTP context accessor.</param>
    /// <param name="routeParameterName">The route parameter name to extract tenant from.</param>
    /// <param name="parser">Optional parser to convert string to TKey.</param>
    public RouteValueTenantResolver(
        IHttpContextAccessor httpContextAccessor,
        string routeParameterName,
        Func<string, TKey>? parser = null)
    {
        _httpContextAccessor = httpContextAccessor;
        _routeParameterName = routeParameterName;
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

        if (!httpContext.GetRouteData().Values.TryGetValue(_routeParameterName, out var routeValue))
        {
            return Task.FromResult<TKey?>(default);
        }

        var value = routeValue?.ToString();
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
