using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using TenantCore.EntityFramework.Abstractions;
using TenantCore.EntityFramework.Context;

namespace TenantCore.EntityFramework.Events;

/// <summary>
/// Health check that verifies tenant database connectivity and migration status.
/// </summary>
/// <typeparam name="TContext">The DbContext type.</typeparam>
/// <typeparam name="TKey">The type of the tenant identifier.</typeparam>
public class TenantHealthCheck<TContext, TKey> : IHealthCheck
    where TContext : TenantDbContext<TKey>
    where TKey : notnull
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ITenantStrategy<TKey> _strategy;

    /// <summary>
    /// Initializes a new instance of the <see cref="TenantHealthCheck{TContext, TKey}"/> class.
    /// </summary>
    /// <param name="serviceProvider">The service provider.</param>
    /// <param name="strategy">The tenant isolation strategy.</param>
    public TenantHealthCheck(
        IServiceProvider serviceProvider,
        ITenantStrategy<TKey> strategy)
    {
        _serviceProvider = serviceProvider;
        _strategy = strategy;
    }

    /// <inheritdoc />
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            using var scope = _serviceProvider.CreateScope();
            var contextFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<TContext>>();

            await using var dbContext = await contextFactory.CreateDbContextAsync(cancellationToken);

            // Check basic connectivity
            if (!await dbContext.Database.CanConnectAsync(cancellationToken))
            {
                return HealthCheckResult.Unhealthy("Cannot connect to database");
            }

            // Get tenant count
            var tenants = await _strategy.GetTenantsAsync(dbContext, cancellationToken);
            var tenantCount = tenants.Count();

            // Check for pending migrations
            var pendingMigrations = await dbContext.Database.GetPendingMigrationsAsync(cancellationToken);
            var pendingCount = pendingMigrations.Count();

            var data = new Dictionary<string, object>
            {
                ["tenant_count"] = tenantCount,
                ["pending_migrations"] = pendingCount
            };

            if (pendingCount > 0)
            {
                return HealthCheckResult.Degraded(
                    $"Database has {pendingCount} pending migrations",
                    data: data);
            }

            return HealthCheckResult.Healthy(
                $"Database healthy with {tenantCount} tenants",
                data: data);
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy("Health check failed", ex);
        }
    }
}

/// <summary>
/// Health check that verifies a specific tenant's status.
/// </summary>
/// <typeparam name="TContext">The DbContext type.</typeparam>
/// <typeparam name="TKey">The type of the tenant identifier.</typeparam>
public class TenantSpecificHealthCheck<TContext, TKey> : IHealthCheck
    where TContext : TenantDbContext<TKey>
    where TKey : notnull
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ITenantContextAccessor<TKey> _contextAccessor;
    private readonly ITenantStrategy<TKey> _strategy;

    /// <summary>
    /// Initializes a new instance of the <see cref="TenantSpecificHealthCheck{TContext, TKey}"/> class.
    /// </summary>
    /// <param name="serviceProvider">The service provider.</param>
    /// <param name="contextAccessor">The tenant context accessor.</param>
    /// <param name="strategy">The tenant isolation strategy.</param>
    public TenantSpecificHealthCheck(
        IServiceProvider serviceProvider,
        ITenantContextAccessor<TKey> contextAccessor,
        ITenantStrategy<TKey> strategy)
    {
        _serviceProvider = serviceProvider;
        _contextAccessor = contextAccessor;
        _strategy = strategy;
    }

    /// <inheritdoc />
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        var tenantContext = _contextAccessor.TenantContext;

        if (tenantContext == null || !tenantContext.IsValid)
        {
            return HealthCheckResult.Degraded("No tenant context available");
        }

        try
        {
            using var scope = _serviceProvider.CreateScope();
            var contextFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<TContext>>();

            await using var dbContext = await contextFactory.CreateDbContextAsync(cancellationToken);

            // Check if tenant schema exists
            var exists = await _strategy.TenantExistsAsync(dbContext, tenantContext.TenantId, cancellationToken);

            if (!exists)
            {
                return HealthCheckResult.Unhealthy($"Tenant {tenantContext.TenantId} schema does not exist");
            }

            // Check connectivity
            if (!await dbContext.Database.CanConnectAsync(cancellationToken))
            {
                return HealthCheckResult.Unhealthy($"Cannot connect to tenant {tenantContext.TenantId} database");
            }

            return HealthCheckResult.Healthy($"Tenant {tenantContext.TenantId} healthy");
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy($"Tenant {tenantContext.TenantId} health check failed", ex);
        }
    }
}
