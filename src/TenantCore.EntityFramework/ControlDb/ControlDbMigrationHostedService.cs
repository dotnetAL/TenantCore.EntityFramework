using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using TenantCore.EntityFramework.Configuration;

namespace TenantCore.EntityFramework.ControlDb;

/// <summary>
/// Hosted service that applies control database migrations on application startup.
/// </summary>
public class ControlDbMigrationHostedService : IHostedService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ControlDbOptions _controlDbOptions;
    private readonly ILogger<ControlDbMigrationHostedService> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="ControlDbMigrationHostedService"/> class.
    /// </summary>
    /// <param name="serviceProvider">The service provider.</param>
    /// <param name="controlDbOptions">The control database options.</param>
    /// <param name="logger">The logger instance.</param>
    public ControlDbMigrationHostedService(
        IServiceProvider serviceProvider,
        ControlDbOptions controlDbOptions,
        ILogger<ControlDbMigrationHostedService> logger)
    {
        _serviceProvider = serviceProvider;
        _controlDbOptions = controlDbOptions;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        if (!_controlDbOptions.Enabled)
        {
            _logger.LogDebug("Control database is not enabled, skipping migrations");
            return;
        }

        if (!_controlDbOptions.ApplyMigrationsOnStartup)
        {
            _logger.LogDebug("Control database migration on startup is disabled");
            return;
        }

        _logger.LogInformation("Applying control database migrations on startup");

        try
        {
            using var scope = _serviceProvider.CreateScope();
            var contextFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<ControlDbContext>>();

            await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);

            // Get pending migrations before applying
            var pendingMigrations = await context.Database.GetPendingMigrationsAsync(cancellationToken);
            var pendingList = pendingMigrations.ToList();

            if (pendingList.Count > 0)
            {
                _logger.LogInformation("Applying {Count} pending migration(s): {Migrations}",
                    pendingList.Count, string.Join(", ", pendingList));

                await context.Database.MigrateAsync(cancellationToken);

                _logger.LogInformation("Control database migrations applied successfully");
            }
            else
            {
                _logger.LogDebug("Control database is up to date, no pending migrations");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to apply control database migrations on startup");
            throw;
        }
    }

    /// <inheritdoc />
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
