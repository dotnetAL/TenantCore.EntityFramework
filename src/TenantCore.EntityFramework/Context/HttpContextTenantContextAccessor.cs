using Microsoft.AspNetCore.Http;
using TenantCore.EntityFramework.Abstractions;

namespace TenantCore.EntityFramework.Context;

/// <summary>
/// Implementation of <see cref="ITenantContextAccessor{TKey}"/> that uses HttpContext.Items for ASP.NET Core.
/// This is more reliable than AsyncLocal in ASP.NET Core because it survives across middleware boundaries
/// where execution context may be reset.
/// </summary>
/// <typeparam name="TKey">The type of the tenant identifier.</typeparam>
public class HttpContextTenantContextAccessor<TKey> : ITenantContextAccessor<TKey> where TKey : notnull
{
    private const string TenantContextKey = "TenantCore.TenantContext";

    private readonly IHttpContextAccessor _httpContextAccessor;

    // Fallback to AsyncLocal for non-HTTP scenarios (background jobs, etc.)
    private static readonly AsyncLocal<TenantContextHolder<TKey>> FallbackContext = new();

    /// <summary>
    /// Initializes a new instance of the <see cref="HttpContextTenantContextAccessor{TKey}"/> class.
    /// </summary>
    /// <param name="httpContextAccessor">The HTTP context accessor.</param>
    public HttpContextTenantContextAccessor(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    /// <inheritdoc />
    public TenantContext<TKey>? TenantContext
    {
        get
        {
            // First try HttpContext.Items (ASP.NET Core web requests)
            var httpContext = _httpContextAccessor.HttpContext;
            if (httpContext != null)
            {
                if (httpContext.Items.TryGetValue(TenantContextKey, out var value))
                {
                    return value as TenantContext<TKey>;
                }
                return null;
            }

            // Fallback to AsyncLocal for non-HTTP scenarios
            return FallbackContext.Value?.Context;
        }
    }

    /// <inheritdoc />
    public void SetTenantContext(TenantContext<TKey>? context)
    {
        // Set in HttpContext.Items if available (ASP.NET Core web requests)
        var httpContext = _httpContextAccessor.HttpContext;
        if (httpContext != null)
        {
            if (context != null)
            {
                httpContext.Items[TenantContextKey] = context;
            }
            else
            {
                httpContext.Items.Remove(TenantContextKey);
            }
        }

        // Also set in AsyncLocal as fallback and for non-HTTP scenarios
        if (context != null)
        {
            FallbackContext.Value = new TenantContextHolder<TKey> { Context = context };
        }
        else
        {
            FallbackContext.Value = new TenantContextHolder<TKey> { Context = null };
        }
    }

    private class TenantContextHolder<T> where T : notnull
    {
        public TenantContext<T>? Context;
    }
}
