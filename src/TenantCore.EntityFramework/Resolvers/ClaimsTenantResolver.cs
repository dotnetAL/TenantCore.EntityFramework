using Microsoft.AspNetCore.Http;
using TenantCore.EntityFramework.Abstractions;

namespace TenantCore.EntityFramework.Resolvers;

/// <summary>
/// Resolves tenant from JWT/authentication claims.
/// </summary>
/// <typeparam name="TKey">The type of the tenant identifier.</typeparam>
public class ClaimsTenantResolver<TKey> : ITenantResolver<TKey> where TKey : notnull
{
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly string _claimType;
    private readonly Func<string, TKey>? _parser;

    /// <summary>
    /// Gets the priority of this resolver.
    /// </summary>
    public int Priority { get; init; } = 200;

    /// <summary>
    /// Creates a new claims tenant resolver with default claim type "tenant_id".
    /// </summary>
    /// <param name="httpContextAccessor">The HTTP context accessor.</param>
    public ClaimsTenantResolver(IHttpContextAccessor httpContextAccessor)
        : this(httpContextAccessor, "tenant_id")
    {
    }

    /// <summary>
    /// Creates a new claims tenant resolver with a custom claim type.
    /// </summary>
    /// <param name="httpContextAccessor">The HTTP context accessor.</param>
    /// <param name="claimType">The claim type to extract tenant from.</param>
    /// <param name="parser">Optional parser to convert string to TKey.</param>
    public ClaimsTenantResolver(
        IHttpContextAccessor httpContextAccessor,
        string claimType,
        Func<string, TKey>? parser = null)
    {
        _httpContextAccessor = httpContextAccessor;
        _claimType = claimType;
        _parser = parser;
    }

    /// <inheritdoc />
    public Task<TKey?> ResolveTenantAsync(CancellationToken cancellationToken = default)
    {
        var httpContext = _httpContextAccessor.HttpContext;
        if (httpContext?.User?.Identity?.IsAuthenticated != true)
        {
            return Task.FromResult<TKey?>(default);
        }

        var claim = httpContext.User.FindFirst(_claimType);
        if (claim == null || string.IsNullOrEmpty(claim.Value))
        {
            return Task.FromResult<TKey?>(default);
        }

        try
        {
            var tenantId = _parser != null ? _parser(claim.Value) : ParseTenantId(claim.Value);
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
