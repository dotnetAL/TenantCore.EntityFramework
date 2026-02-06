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
    /// <param name="tenantId">The tenant identifier.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task PublishTenantCreatedAsync(TKey tenantId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Publishes a tenant deleted event.
    /// </summary>
    /// <param name="tenantId">The tenant identifier.</param>
    /// <param name="hardDelete">Whether this was a hard delete (permanent) or soft delete (archived).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task PublishTenantDeletedAsync(TKey tenantId, bool hardDelete, CancellationToken cancellationToken = default);

    /// <summary>
    /// Publishes a tenant archived event.
    /// </summary>
    /// <param name="tenantId">The tenant identifier.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task PublishTenantArchivedAsync(TKey tenantId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Publishes a tenant restored event.
    /// </summary>
    /// <param name="tenantId">The tenant identifier.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task PublishTenantRestoredAsync(TKey tenantId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Publishes a migration applied event.
    /// </summary>
    /// <param name="tenantId">The tenant identifier.</param>
    /// <param name="migrationName">The name of the migration that was applied.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task PublishMigrationAppliedAsync(TKey tenantId, string migrationName, CancellationToken cancellationToken = default);

    /// <summary>
    /// Publishes a tenant resolved event.
    /// </summary>
    /// <param name="tenantId">The tenant identifier.</param>
    /// <param name="resolverName">The name of the resolver that resolved the tenant.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
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
    /// <param name="event">The tenant created event.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task OnTenantCreatedAsync(TenantCreatedEvent<TKey> @event, CancellationToken cancellationToken = default);

    /// <summary>
    /// Called when a tenant is deleted.
    /// </summary>
    /// <param name="event">The tenant deleted event.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task OnTenantDeletedAsync(TenantDeletedEvent<TKey> @event, CancellationToken cancellationToken = default);

    /// <summary>
    /// Called when a tenant is archived.
    /// </summary>
    /// <param name="event">The tenant archived event.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task OnTenantArchivedAsync(TenantArchivedEvent<TKey> @event, CancellationToken cancellationToken = default);

    /// <summary>
    /// Called when a tenant is restored.
    /// </summary>
    /// <param name="event">The tenant restored event.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task OnTenantRestoredAsync(TenantRestoredEvent<TKey> @event, CancellationToken cancellationToken = default);

    /// <summary>
    /// Called when a migration is applied.
    /// </summary>
    /// <param name="event">The migration applied event.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task OnMigrationAppliedAsync(MigrationAppliedEvent<TKey> @event, CancellationToken cancellationToken = default);

    /// <summary>
    /// Called when a tenant is resolved.
    /// </summary>
    /// <param name="event">The tenant resolved event.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task OnTenantResolvedAsync(TenantResolvedEvent<TKey> @event, CancellationToken cancellationToken = default);
}
