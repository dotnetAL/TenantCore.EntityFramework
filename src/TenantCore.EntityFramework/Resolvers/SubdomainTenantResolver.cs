using Microsoft.AspNetCore.Http;
using TenantCore.EntityFramework.Abstractions;

namespace TenantCore.EntityFramework.Resolvers;

/// <summary>
/// Resolves tenant from the request host subdomain.
/// </summary>
/// <typeparam name="TKey">The type of the tenant identifier.</typeparam>
public class SubdomainTenantResolver<TKey> : ITenantResolver<TKey> where TKey : notnull
{
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly string _baseDomain;
    private readonly Func<string, TKey>? _parser;

    /// <summary>
    /// Gets the priority of this resolver.
    /// </summary>
    public int Priority { get; init; } = 50;

    /// <summary>
    /// Creates a new subdomain tenant resolver.
    /// </summary>
    /// <param name="httpContextAccessor">The HTTP context accessor.</param>
    /// <param name="baseDomain">The base domain (e.g., "example.com"). Subdomain before this is extracted as tenant.</param>
    /// <param name="parser">Optional parser to convert string to TKey.</param>
    public SubdomainTenantResolver(
        IHttpContextAccessor httpContextAccessor,
        string baseDomain,
        Func<string, TKey>? parser = null)
    {
        _httpContextAccessor = httpContextAccessor;
        _baseDomain = baseDomain.TrimStart('.');
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

        var host = httpContext.Request.Host.Host;
        if (string.IsNullOrEmpty(host))
        {
            return Task.FromResult<TKey?>(default);
        }

        // Check if host ends with base domain
        if (!host.EndsWith(_baseDomain, StringComparison.OrdinalIgnoreCase))
        {
            return Task.FromResult<TKey?>(default);
        }

        // Extract subdomain
        var subdomain = ExtractSubdomain(host);
        if (string.IsNullOrEmpty(subdomain))
        {
            return Task.FromResult<TKey?>(default);
        }

        try
        {
            var tenantId = _parser != null ? _parser(subdomain) : ParseTenantId(subdomain);
            return Task.FromResult<TKey?>(tenantId);
        }
        catch
        {
            return Task.FromResult<TKey?>(default);
        }
    }

    private string? ExtractSubdomain(string host)
    {
        // Remove base domain from host
        var baseDomainWithDot = "." + _baseDomain;

        if (host.Equals(_baseDomain, StringComparison.OrdinalIgnoreCase))
        {
            // No subdomain - just the base domain
            return null;
        }

        if (!host.EndsWith(baseDomainWithDot, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var subdomain = host[..^baseDomainWithDot.Length];

        // Handle multi-level subdomains by taking only the first part
        // e.g., "tenant1.api.example.com" -> "tenant1"
        var firstPart = subdomain.Split('.').FirstOrDefault();

        // Ignore common non-tenant subdomains
        if (string.IsNullOrEmpty(firstPart) ||
            firstPart.Equals("www", StringComparison.OrdinalIgnoreCase) ||
            firstPart.Equals("api", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return firstPart;
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
