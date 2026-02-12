using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using TenantCore.EntityFramework.Abstractions;
using TenantCore.EntityFramework.Configuration;

namespace TenantCore.EntityFramework.Extensions;

/// <summary>
/// Middleware that resolves the tenant for each request.
/// </summary>
/// <typeparam name="TKey">The type of the tenant identifier.</typeparam>
public class TenantMiddleware<TKey> where TKey : notnull
{
    private readonly RequestDelegate _next;

    /// <summary>
    /// Initializes a new instance of the <see cref="TenantMiddleware{TKey}"/> class.
    /// </summary>
    /// <param name="next">The next middleware in the pipeline.</param>
    public TenantMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    /// <summary>
    /// Invokes the middleware to resolve the tenant for the current request.
    /// </summary>
    /// <param name="context">The HTTP context.</param>
    public async Task InvokeAsync(HttpContext context)
    {
        var options = context.RequestServices.GetRequiredService<TenantCoreOptions>();

        // Check if path is excluded from tenant resolution
        if (IsPathExcluded(context.Request.Path, options.ExcludedPaths))
        {
            await _next(context);
            return;
        }

        var pipeline = context.RequestServices.GetRequiredService<ITenantResolverPipeline<TKey>>();

        try
        {
            var tenantId = await pipeline.ResolveAsync(context.RequestAborted);

            // If tenant is null and behavior is to throw, the pipeline already threw
            // Otherwise continue with the request
            await _next(context);
        }
        catch (TenantNotFoundException) when (options.TenantNotFoundBehavior != TenantNotFoundBehavior.Throw)
        {
            // Handle based on configuration
            await _next(context);
        }
        catch (TenantNotFoundException)
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            context.Response.ContentType = "application/json";
            await context.Response.WriteAsync("{\"error\":\"Access denied\"}", context.RequestAborted);
        }
    }

    private static bool IsPathExcluded(PathString path, List<string> excludedPaths)
    {
        if (excludedPaths.Count == 0)
            return false;

        var pathValue = path.Value ?? string.Empty;
        foreach (var excludedPath in excludedPaths)
        {
            if (pathValue.StartsWith(excludedPath, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }
}

/// <summary>
/// Extension methods for adding tenant middleware to the pipeline.
/// </summary>
public static class TenantMiddlewareExtensions
{
    /// <summary>
    /// Adds the tenant resolution middleware to the pipeline.
    /// </summary>
    /// <typeparam name="TKey">The type of the tenant identifier.</typeparam>
    /// <param name="app">The application builder.</param>
    /// <returns>The application builder for chaining.</returns>
    public static Microsoft.AspNetCore.Builder.IApplicationBuilder UseTenantResolution<TKey>(
        this Microsoft.AspNetCore.Builder.IApplicationBuilder app) where TKey : notnull
    {
        return app.UseMiddleware<TenantMiddleware<TKey>>();
    }
}
