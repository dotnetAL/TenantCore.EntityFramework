using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using TenantCore.EntityFramework.Configuration;
using TenantCore.EntityFramework.Context;

namespace TenantCore.EntityFramework.Migrations;

/// <summary>
/// Hosted service that applies migrations on application startup.
/// </summary>
/// <typeparam name="TContext">The DbContext type.</typeparam>
/// <typeparam name="TKey">The type of the tenant identifier.</typeparam>
public class TenantMigrationHostedService<TContext, TKey> : IHostedService
    where TContext : TenantDbContext<TKey>
    where TKey : notnull
{
    private readonly IServiceProvider _serviceProvider;
    private readonly TenantCoreOptions _options;
    private readonly ILogger<TenantMigrationHostedService<TContext, TKey>> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="TenantMigrationHostedService{TContext, TKey}"/> class.
    /// </summary>
    /// <param name="serviceProvider">The service provider for creating scopes.</param>
    /// <param name="options">The tenant configuration options.</param>
    /// <param name="logger">The logger instance.</param>
    public TenantMigrationHostedService(
        IServiceProvider serviceProvider,
        TenantCoreOptions options,
        ILogger<TenantMigrationHostedService<TContext, TKey>> logger)
    {
        _serviceProvider = serviceProvider;
        _options = options;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        if (!_options.Migrations.ApplyOnStartup)
        {
            _logger.LogDebug("Automatic migration on startup is disabled");
            return;
        }

        _logger.LogInformation("Applying {Context} migrations to all tenants on startup", typeof(TContext).Name);

        try
        {
            using var scope = _serviceProvider.CreateScope();
            var migrationRunner = scope.ServiceProvider
                .GetRequiredService<TenantMigrationRunner<TContext, TKey>>();
            await migrationRunner.MigrateAllTenantsAsync(cancellationToken);
            _logger.LogInformation("Startup migration for {Context} completed successfully", typeof(TContext).Name);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to apply {Context} migrations on startup", typeof(TContext).Name);
            throw;
        }
    }

    /// <inheritdoc />
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
