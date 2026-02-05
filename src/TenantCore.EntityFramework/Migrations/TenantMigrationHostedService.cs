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
    private readonly TenantMigrationRunner<TContext, TKey> _migrationRunner;
    private readonly TenantCoreOptions _options;
    private readonly ILogger<TenantMigrationHostedService<TContext, TKey>> _logger;

    public TenantMigrationHostedService(
        TenantMigrationRunner<TContext, TKey> migrationRunner,
        TenantCoreOptions options,
        ILogger<TenantMigrationHostedService<TContext, TKey>> logger)
    {
        _migrationRunner = migrationRunner;
        _options = options;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        if (!_options.Migrations.ApplyOnStartup)
        {
            _logger.LogDebug("Automatic migration on startup is disabled");
            return;
        }

        _logger.LogInformation("Applying migrations to all tenants on startup");

        try
        {
            await _migrationRunner.MigrateAllTenantsAsync(cancellationToken);
            _logger.LogInformation("Startup migration completed successfully");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to apply migrations on startup");
            throw;
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
