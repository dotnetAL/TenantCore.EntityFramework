using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace TenantCore.EntityFramework.Events;

/// <summary>
/// Default implementation of <see cref="ITenantEventPublisher{TKey}"/> that dispatches to registered subscribers.
/// </summary>
/// <typeparam name="TKey">The type of the tenant identifier.</typeparam>
public class TenantEventPublisher<TKey> : ITenantEventPublisher<TKey> where TKey : notnull
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<TenantEventPublisher<TKey>> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="TenantEventPublisher{TKey}"/> class.
    /// </summary>
    /// <param name="serviceProvider">The service provider for resolving subscribers.</param>
    /// <param name="logger">The logger instance.</param>
    public TenantEventPublisher(
        IServiceProvider serviceProvider,
        ILogger<TenantEventPublisher<TKey>> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task PublishTenantCreatedAsync(TKey tenantId, CancellationToken cancellationToken = default)
    {
        var @event = new TenantCreatedEvent<TKey>(tenantId);
        _logger.LogDebug("Publishing TenantCreated event for tenant {TenantId}", tenantId);
        await DispatchToSubscribersAsync(s => s.OnTenantCreatedAsync(@event, cancellationToken));
    }

    /// <inheritdoc />
    public async Task PublishTenantDeletedAsync(TKey tenantId, bool hardDelete, CancellationToken cancellationToken = default)
    {
        var @event = new TenantDeletedEvent<TKey>(tenantId, hardDelete);
        _logger.LogDebug("Publishing TenantDeleted event for tenant {TenantId} (hardDelete: {HardDelete})", tenantId, hardDelete);
        await DispatchToSubscribersAsync(s => s.OnTenantDeletedAsync(@event, cancellationToken));
    }

    /// <inheritdoc />
    public async Task PublishTenantArchivedAsync(TKey tenantId, CancellationToken cancellationToken = default)
    {
        var @event = new TenantArchivedEvent<TKey>(tenantId);
        _logger.LogDebug("Publishing TenantArchived event for tenant {TenantId}", tenantId);
        await DispatchToSubscribersAsync(s => s.OnTenantArchivedAsync(@event, cancellationToken));
    }

    /// <inheritdoc />
    public async Task PublishTenantRestoredAsync(TKey tenantId, CancellationToken cancellationToken = default)
    {
        var @event = new TenantRestoredEvent<TKey>(tenantId);
        _logger.LogDebug("Publishing TenantRestored event for tenant {TenantId}", tenantId);
        await DispatchToSubscribersAsync(s => s.OnTenantRestoredAsync(@event, cancellationToken));
    }

    /// <inheritdoc />
    public async Task PublishMigrationAppliedAsync(TKey tenantId, string migrationName, CancellationToken cancellationToken = default)
    {
        var @event = new MigrationAppliedEvent<TKey>(tenantId, migrationName);
        _logger.LogDebug("Publishing MigrationApplied event for tenant {TenantId}, migration {MigrationName}", tenantId, migrationName);
        await DispatchToSubscribersAsync(s => s.OnMigrationAppliedAsync(@event, cancellationToken));
    }

    /// <inheritdoc />
    public async Task PublishTenantResolvedAsync(TKey tenantId, string resolverName, CancellationToken cancellationToken = default)
    {
        var @event = new TenantResolvedEvent<TKey>(tenantId, resolverName);
        _logger.LogTrace("Publishing TenantResolved event for tenant {TenantId} via {ResolverName}", tenantId, resolverName);
        await DispatchToSubscribersAsync(s => s.OnTenantResolvedAsync(@event, cancellationToken));
    }

    private async Task DispatchToSubscribersAsync(Func<ITenantEventSubscriber<TKey>, Task> dispatch)
    {
        var subscribers = _serviceProvider.GetServices<ITenantEventSubscriber<TKey>>();

        foreach (var subscriber in subscribers)
        {
            try
            {
                await dispatch(subscriber);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error dispatching event to subscriber {SubscriberType}", subscriber.GetType().Name);
            }
        }
    }
}
