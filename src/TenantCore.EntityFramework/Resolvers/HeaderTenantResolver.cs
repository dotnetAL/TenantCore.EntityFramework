using Microsoft.AspNetCore.Http;
using TenantCore.EntityFramework.Abstractions;

namespace TenantCore.EntityFramework.Resolvers;

/// <summary>
/// Resolves tenant from an HTTP header.
/// </summary>
/// <typeparam name="TKey">The type of the tenant identifier.</typeparam>
public class HeaderTenantResolver<TKey> : ITenantResolver<TKey> where TKey : notnull
{
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly string _headerName;
    private readonly Func<string, TKey>? _parser;

    /// <summary>
    /// Gets the priority of this resolver.
    /// </summary>
    public int Priority { get; init; } = 100;

    /// <summary>
    /// Creates a new header tenant resolver with default header name "X-Tenant-Id".
    /// </summary>
    /// <param name="httpContextAccessor">The HTTP context accessor.</param>
    public HeaderTenantResolver(IHttpContextAccessor httpContextAccessor)
        : this(httpContextAccessor, "X-Tenant-Id")
    {
    }

    /// <summary>
    /// Creates a new header tenant resolver with a custom header name.
    /// </summary>
    /// <param name="httpContextAccessor">The HTTP context accessor.</param>
    /// <param name="headerName">The header name to extract tenant from.</param>
    /// <param name="parser">Optional parser to convert string to TKey.</param>
    public HeaderTenantResolver(
        IHttpContextAccessor httpContextAccessor,
        string headerName,
        Func<string, TKey>? parser = null)
    {
        _httpContextAccessor = httpContextAccessor;
        _headerName = headerName;
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

        if (!httpContext.Request.Headers.TryGetValue(_headerName, out var headerValue))
        {
            return Task.FromResult<TKey?>(default);
        }

        var value = headerValue.FirstOrDefault();
        if (string.IsNullOrEmpty(value))
        {
            return Task.FromResult<TKey?>(default);
        }

        try
        {
            var tenantId = _parser != null ? _parser(value) : ParseTenantId(value);
            return Task.FromResult<TKey?>(tenantId);
        }
        catch
        {
            return Task.FromResult<TKey?>(default);
        }
    }

    private static TKey ParseTenantId(string value)
    {
        var type = typeof(TKey);

        if (type == typeof(string))
            return (TKey)(object)value;

        if (type == typeof(Guid))
            return (TKey)(object)Guid.Parse(value);

        if (type == typeof(int))
            return (TKey)(object)int.Parse(value);

        if (type == typeof(long))
            return (TKey)(object)long.Parse(value);

        throw new NotSupportedException($"Tenant key type {type.Name} is not supported");
    }
}
