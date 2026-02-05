namespace TenantCore.EntityFramework.Events;

/// <summary>
/// Publishes tenant lifecycle events.
/// </summary>
/// <typeparam name="TKey">The type of the tenant identifier.</typeparam>
public interface ITenantEventPublisher<TKey> where TKey : notnull
{
    /// <summary>
    /// Publishes a tenant created event.
    /// </summary>
    Task PublishTenantCreatedAsync(TKey tenantId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Publishes a tenant deleted event.
    /// </summary>
    Task PublishTenantDeletedAsync(TKey tenantId, bool hardDelete, CancellationToken cancellationToken = default);

    /// <summary>
    /// Publishes a tenant archived event.
    /// </summary>
    Task PublishTenantArchivedAsync(TKey tenantId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Publishes a tenant restored event.
    /// </summary>
    Task PublishTenantRestoredAsync(TKey tenantId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Publishes a migration applied event.
    /// </summary>
    Task PublishMigrationAppliedAsync(TKey tenantId, string migrationName, CancellationToken cancellationToken = default);

    /// <summary>
    /// Publishes a tenant resolved event.
    /// </summary>
    Task PublishTenantResolvedAsync(TKey tenantId, string resolverName, CancellationToken cancellationToken = default);
}

/// <summary>
/// Subscribes to tenant lifecycle events.
/// </summary>
/// <typeparam name="TKey">The type of the tenant identifier.</typeparam>
public interface ITenantEventSubscriber<TKey> where TKey : notnull
{
    /// <summary>
    /// Called when a tenant is created.
    /// </summary>
    Task OnTenantCreatedAsync(TenantCreatedEvent<TKey> @event, CancellationToken cancellationToken = default);

    /// <summary>
    /// Called when a tenant is deleted.
    /// </summary>
    Task OnTenantDeletedAsync(TenantDeletedEvent<TKey> @event, CancellationToken cancellationToken = default);

    /// <summary>
    /// Called when a tenant is archived.
    /// </summary>
    Task OnTenantArchivedAsync(TenantArchivedEvent<TKey> @event, CancellationToken cancellationToken = default);

    /// <summary>
    /// Called when a tenant is restored.
    /// </summary>
    Task OnTenantRestoredAsync(TenantRestoredEvent<TKey> @event, CancellationToken cancellationToken = default);

    /// <summary>
    /// Called when a migration is applied.
    /// </summary>
    Task OnMigrationAppliedAsync(MigrationAppliedEvent<TKey> @event, CancellationToken cancellationToken = default);

    /// <summary>
    /// Called when a tenant is resolved.
    /// </summary>
    Task OnTenantResolvedAsync(TenantResolvedEvent<TKey> @event, CancellationToken cancellationToken = default);
}
