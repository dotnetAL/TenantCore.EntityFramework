namespace TenantCore.EntityFramework.Events;

/// <summary>
/// Event raised when a tenant is created.
/// </summary>
/// <typeparam name="TKey">The type of the tenant identifier.</typeparam>
public record TenantCreatedEvent<TKey>(TKey TenantId, DateTimeOffset Timestamp) where TKey : notnull
{
    public TenantCreatedEvent(TKey tenantId) : this(tenantId, DateTimeOffset.UtcNow) { }
}

/// <summary>
/// Event raised when a tenant is deleted.
/// </summary>
/// <typeparam name="TKey">The type of the tenant identifier.</typeparam>
public record TenantDeletedEvent<TKey>(TKey TenantId, bool HardDelete, DateTimeOffset Timestamp) where TKey : notnull
{
    public TenantDeletedEvent(TKey tenantId, bool hardDelete) : this(tenantId, hardDelete, DateTimeOffset.UtcNow) { }
}

/// <summary>
/// Event raised when a tenant is archived.
/// </summary>
/// <typeparam name="TKey">The type of the tenant identifier.</typeparam>
public record TenantArchivedEvent<TKey>(TKey TenantId, DateTimeOffset Timestamp) where TKey : notnull
{
    public TenantArchivedEvent(TKey tenantId) : this(tenantId, DateTimeOffset.UtcNow) { }
}

/// <summary>
/// Event raised when a tenant is restored.
/// </summary>
/// <typeparam name="TKey">The type of the tenant identifier.</typeparam>
public record TenantRestoredEvent<TKey>(TKey TenantId, DateTimeOffset Timestamp) where TKey : notnull
{
    public TenantRestoredEvent(TKey tenantId) : this(tenantId, DateTimeOffset.UtcNow) { }
}

/// <summary>
/// Event raised when a migration is applied to a tenant.
/// </summary>
/// <typeparam name="TKey">The type of the tenant identifier.</typeparam>
public record MigrationAppliedEvent<TKey>(TKey TenantId, string MigrationName, DateTimeOffset Timestamp) where TKey : notnull
{
    public MigrationAppliedEvent(TKey tenantId, string migrationName) : this(tenantId, migrationName, DateTimeOffset.UtcNow) { }
}

/// <summary>
/// Event raised when a tenant is resolved.
/// </summary>
/// <typeparam name="TKey">The type of the tenant identifier.</typeparam>
public record TenantResolvedEvent<TKey>(TKey TenantId, string ResolverName, DateTimeOffset Timestamp) where TKey : notnull
{
    public TenantResolvedEvent(TKey tenantId, string resolverName) : this(tenantId, resolverName, DateTimeOffset.UtcNow) { }
}
