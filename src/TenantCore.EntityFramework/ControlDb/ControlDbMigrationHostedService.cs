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
    private readonly TenantCoreOptions _options;
    private readonly ILogger<ControlDbMigrationHostedService> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="ControlDbMigrationHostedService"/> class.
    /// </summary>
    /// <param name="serviceProvider">The service provider.</param>
    /// <param name="options">The tenant configuration options.</param>
    /// <param name="logger">The logger instance.</param>
    public ControlDbMigrationHostedService(
        IServiceProvider serviceProvider,
        TenantCoreOptions options,
        ILogger<ControlDbMigrationHostedService> logger)
    {
        _serviceProvider = serviceProvider;
        _options = options;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        if (!_options.ControlDb.Enabled)
        {
            _logger.LogDebug("Control database is not enabled, skipping migrations");
            return;
        }

        if (!_options.ControlDb.ApplyMigrationsOnStartup)
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

            // Use EnsureCreated for the control database since it has a simple schema
            // For production, consider using actual EF migrations
            await context.Database.EnsureCreatedAsync(cancellationToken);

            _logger.LogInformation("Control database migration completed successfully");
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
