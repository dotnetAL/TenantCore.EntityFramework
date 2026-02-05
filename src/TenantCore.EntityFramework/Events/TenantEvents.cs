namespace TenantCore.EntityFramework.Events;

/// <summary>
/// Event raised when a tenant is created.
/// </summary>
/// <typeparam name="TKey">The type of the tenant identifier.</typeparam>
/// <param name="TenantId">The tenant identifier.</param>
/// <param name="Timestamp">The timestamp when the event occurred.</param>
public record TenantCreatedEvent<TKey>(TKey TenantId, DateTimeOffset Timestamp) where TKey : notnull
{
    /// <summary>
    /// Initializes a new instance of the <see cref="TenantCreatedEvent{TKey}"/> record with the current timestamp.
    /// </summary>
    /// <param name="tenantId">The tenant identifier.</param>
    public TenantCreatedEvent(TKey tenantId) : this(tenantId, DateTimeOffset.UtcNow) { }
}

/// <summary>
/// Event raised when a tenant is deleted.
/// </summary>
/// <typeparam name="TKey">The type of the tenant identifier.</typeparam>
/// <param name="TenantId">The tenant identifier.</param>
/// <param name="HardDelete">Whether this was a hard delete.</param>
/// <param name="Timestamp">The timestamp when the event occurred.</param>
public record TenantDeletedEvent<TKey>(TKey TenantId, bool HardDelete, DateTimeOffset Timestamp) where TKey : notnull
{
    /// <summary>
    /// Initializes a new instance of the <see cref="TenantDeletedEvent{TKey}"/> record with the current timestamp.
    /// </summary>
    /// <param name="tenantId">The tenant identifier.</param>
    /// <param name="hardDelete">Whether this was a hard delete.</param>
    public TenantDeletedEvent(TKey tenantId, bool hardDelete) : this(tenantId, hardDelete, DateTimeOffset.UtcNow) { }
}

/// <summary>
/// Event raised when a tenant is archived.
/// </summary>
/// <typeparam name="TKey">The type of the tenant identifier.</typeparam>
/// <param name="TenantId">The tenant identifier.</param>
/// <param name="Timestamp">The timestamp when the event occurred.</param>
public record TenantArchivedEvent<TKey>(TKey TenantId, DateTimeOffset Timestamp) where TKey : notnull
{
    /// <summary>
    /// Initializes a new instance of the <see cref="TenantArchivedEvent{TKey}"/> record with the current timestamp.
    /// </summary>
    /// <param name="tenantId">The tenant identifier.</param>
    public TenantArchivedEvent(TKey tenantId) : this(tenantId, DateTimeOffset.UtcNow) { }
}

/// <summary>
/// Event raised when a tenant is restored.
/// </summary>
/// <typeparam name="TKey">The type of the tenant identifier.</typeparam>
/// <param name="TenantId">The tenant identifier.</param>
/// <param name="Timestamp">The timestamp when the event occurred.</param>
public record TenantRestoredEvent<TKey>(TKey TenantId, DateTimeOffset Timestamp) where TKey : notnull
{
    /// <summary>
    /// Initializes a new instance of the <see cref="TenantRestoredEvent{TKey}"/> record with the current timestamp.
    /// </summary>
    /// <param name="tenantId">The tenant identifier.</param>
    public TenantRestoredEvent(TKey tenantId) : this(tenantId, DateTimeOffset.UtcNow) { }
}

/// <summary>
/// Event raised when a migration is applied to a tenant.
/// </summary>
/// <typeparam name="TKey">The type of the tenant identifier.</typeparam>
/// <param name="TenantId">The tenant identifier.</param>
/// <param name="MigrationName">The name of the migration that was applied.</param>
/// <param name="Timestamp">The timestamp when the event occurred.</param>
public record MigrationAppliedEvent<TKey>(TKey TenantId, string MigrationName, DateTimeOffset Timestamp) where TKey : notnull
{
    /// <summary>
    /// Initializes a new instance of the <see cref="MigrationAppliedEvent{TKey}"/> record with the current timestamp.
    /// </summary>
    /// <param name="tenantId">The tenant identifier.</param>
    /// <param name="migrationName">The name of the migration that was applied.</param>
    public MigrationAppliedEvent(TKey tenantId, string migrationName) : this(tenantId, migrationName, DateTimeOffset.UtcNow) { }
}

/// <summary>
/// Event raised when a tenant is resolved.
/// </summary>
/// <typeparam name="TKey">The type of the tenant identifier.</typeparam>
/// <param name="TenantId">The tenant identifier.</param>
/// <param name="ResolverName">The name of the resolver that resolved the tenant.</param>
/// <param name="Timestamp">The timestamp when the event occurred.</param>
public record TenantResolvedEvent<TKey>(TKey TenantId, string ResolverName, DateTimeOffset Timestamp) where TKey : notnull
{
    /// <summary>
    /// Initializes a new instance of the <see cref="TenantResolvedEvent{TKey}"/> record with the current timestamp.
    /// </summary>
    /// <param name="tenantId">The tenant identifier.</param>
    /// <param name="resolverName">The name of the resolver that resolved the tenant.</param>
    public TenantResolvedEvent(TKey tenantId, string resolverName) : this(tenantId, resolverName, DateTimeOffset.UtcNow) { }
}
