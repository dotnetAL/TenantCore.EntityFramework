using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using TenantCore.EntityFramework.Abstractions;
using TenantCore.EntityFramework.Configuration;
using TenantCore.EntityFramework.Context;
using TenantCore.EntityFramework.Events;
using TenantCore.EntityFramework.Lifecycle;
using TenantCore.EntityFramework.Migrations;
using TenantCore.EntityFramework.Resolvers;

namespace TenantCore.EntityFramework.Extensions;

/// <summary>
/// Extension methods for registering TenantCore services.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Adds TenantCore services with the specified tenant key type.
    /// </summary>
    /// <typeparam name="TKey">The type of the tenant identifier.</typeparam>
    /// <param name="services">The service collection.</param>
    /// <param name="configure">Action to configure TenantCore options.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddTenantCore<TKey>(
        this IServiceCollection services,
        Action<TenantCoreOptionsBuilder<TKey>> configure) where TKey : notnull
    {
        var options = new TenantCoreOptions();
        var builder = new TenantCoreOptionsBuilder<TKey>(options);
        configure(builder);

        services.AddSingleton(options);

        // Core services
        services.TryAddSingleton<ITenantContextAccessor<TKey>, TenantContextAccessor<TKey>>();
        services.TryAddSingleton<ITenantScopeFactory<TKey>, TenantScopeFactory<TKey>>();

        // Resolver pipeline
        services.TryAddScoped<ITenantResolverPipeline<TKey>, TenantResolverPipeline<TKey>>();

        // Register configured resolvers
        foreach (var resolverType in options.TenantResolverTypes)
        {
            services.AddScoped(typeof(ITenantResolver<TKey>), resolverType);
        }

        // Register configured seeders
        foreach (var seederType in options.TenantSeederTypes)
        {
            services.AddScoped(typeof(ITenantSeeder<TKey>), seederType);
        }

        // Register validator if configured
        if (options.TenantValidatorType != null)
        {
            services.TryAddScoped(typeof(ITenantValidator<TKey>), options.TenantValidatorType);
        }

        // Events
        services.TryAddSingleton<ITenantEventPublisher<TKey>, TenantEventPublisher<TKey>>();

        return services;
    }

    /// <summary>
    /// Adds automatic migration on startup for tenant schemas.
    /// </summary>
    /// <typeparam name="TContext">The DbContext type.</typeparam>
    /// <typeparam name="TKey">The type of the tenant identifier.</typeparam>
    /// <param name="services">The service collection.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddTenantMigrationHostedService<TContext, TKey>(
        this IServiceCollection services)
        where TContext : TenantDbContext<TKey>
        where TKey : notnull
    {
        services.AddHostedService<TenantMigrationHostedService<TContext, TKey>>();
        return services;
    }

    /// <summary>
    /// Adds a tenant event subscriber.
    /// </summary>
    /// <typeparam name="TKey">The type of the tenant identifier.</typeparam>
    /// <typeparam name="TSubscriber">The subscriber type.</typeparam>
    /// <param name="services">The service collection.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddTenantEventSubscriber<TKey, TSubscriber>(
        this IServiceCollection services)
        where TKey : notnull
        where TSubscriber : class, ITenantEventSubscriber<TKey>
    {
        services.AddScoped<ITenantEventSubscriber<TKey>, TSubscriber>();
        return services;
    }

    /// <summary>
    /// Adds health checks for tenant database.
    /// </summary>
    /// <typeparam name="TContext">The DbContext type.</typeparam>
    /// <typeparam name="TKey">The type of the tenant identifier.</typeparam>
    /// <param name="services">The service collection.</param>
    /// <param name="name">The health check name.</param>
    /// <param name="tags">Optional tags for the health check.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddTenantHealthChecks<TContext, TKey>(
        this IServiceCollection services,
        string name = "tenant-database",
        params string[] tags)
        where TContext : TenantDbContext<TKey>
        where TKey : notnull
    {
        services.AddHealthChecks()
            .AddCheck<TenantHealthCheck<TContext, TKey>>(name, tags: tags);

        return services;
    }

    /// <summary>
    /// Adds the header tenant resolver.
    /// </summary>
    /// <typeparam name="TKey">The type of the tenant identifier.</typeparam>
    /// <param name="services">The service collection.</param>
    /// <param name="headerName">The header name (default: X-Tenant-Id).</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddHeaderTenantResolver<TKey>(
        this IServiceCollection services,
        string headerName = "X-Tenant-Id")
        where TKey : notnull
    {
        services.AddHttpContextAccessor();
        services.AddScoped<ITenantResolver<TKey>>(sp =>
            new HeaderTenantResolver<TKey>(sp.GetRequiredService<Microsoft.AspNetCore.Http.IHttpContextAccessor>(), headerName));
        return services;
    }

    /// <summary>
    /// Adds the claims tenant resolver.
    /// </summary>
    /// <typeparam name="TKey">The type of the tenant identifier.</typeparam>
    /// <param name="services">The service collection.</param>
    /// <param name="claimType">The claim type (default: tenant_id).</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddClaimsTenantResolver<TKey>(
        this IServiceCollection services,
        string claimType = "tenant_id")
        where TKey : notnull
    {
        services.AddHttpContextAccessor();
        services.AddScoped<ITenantResolver<TKey>>(sp =>
            new ClaimsTenantResolver<TKey>(sp.GetRequiredService<Microsoft.AspNetCore.Http.IHttpContextAccessor>(), claimType));
        return services;
    }

    /// <summary>
    /// Adds the subdomain tenant resolver.
    /// </summary>
    /// <typeparam name="TKey">The type of the tenant identifier.</typeparam>
    /// <param name="services">The service collection.</param>
    /// <param name="baseDomain">The base domain (e.g., "example.com").</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddSubdomainTenantResolver<TKey>(
        this IServiceCollection services,
        string baseDomain)
        where TKey : notnull
    {
        services.AddHttpContextAccessor();
        services.AddScoped<ITenantResolver<TKey>>(sp =>
            new SubdomainTenantResolver<TKey>(sp.GetRequiredService<Microsoft.AspNetCore.Http.IHttpContextAccessor>(), baseDomain));
        return services;
    }

    /// <summary>
    /// Adds the path tenant resolver with segment index.
    /// </summary>
    /// <typeparam name="TKey">The type of the tenant identifier.</typeparam>
    /// <param name="services">The service collection.</param>
    /// <param name="segmentIndex">The zero-based index of the path segment containing the tenant (default: 0).</param>
    /// <returns>The service collection for chaining.</returns>
    /// <remarks>
    /// Examples with different segment indices:
    /// <list type="bullet">
    ///   <item>segmentIndex 0: /{tenant}/api/products</item>
    ///   <item>segmentIndex 1: /api/{tenant}/products</item>
    ///   <item>segmentIndex 2: /api/v1/{tenant}/products</item>
    /// </list>
    /// </remarks>
    public static IServiceCollection AddPathTenantResolver<TKey>(
        this IServiceCollection services,
        int segmentIndex = 0)
        where TKey : notnull
    {
        services.AddHttpContextAccessor();
        services.AddScoped<ITenantResolver<TKey>>(sp =>
            new PathTenantResolver<TKey>(sp.GetRequiredService<Microsoft.AspNetCore.Http.IHttpContextAccessor>(), segmentIndex));
        return services;
    }

    /// <summary>
    /// Adds the path tenant resolver with a path prefix.
    /// </summary>
    /// <typeparam name="TKey">The type of the tenant identifier.</typeparam>
    /// <param name="services">The service collection.</param>
    /// <param name="pathPrefix">The path prefix before the tenant segment (e.g., "/api" or "/v1/tenants").</param>
    /// <returns>The service collection for chaining.</returns>
    /// <remarks>
    /// The resolver extracts tenant from the segment immediately after the prefix.
    /// Example: With prefix "/api", request to "/api/tenant1/products" resolves to "tenant1".
    /// </remarks>
    public static IServiceCollection AddPathTenantResolverWithPrefix<TKey>(
        this IServiceCollection services,
        string pathPrefix)
        where TKey : notnull
    {
        services.AddHttpContextAccessor();
        services.AddScoped<ITenantResolver<TKey>>(sp =>
            new PathTenantResolver<TKey>(sp.GetRequiredService<Microsoft.AspNetCore.Http.IHttpContextAccessor>(), pathPrefix));
        return services;
    }
}
