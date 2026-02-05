namespace TenantCore.EntityFramework.Abstractions;

/// <summary>
/// Resolves the current tenant identifier from the execution context.
/// </summary>
/// <typeparam name="TKey">The type of the tenant identifier.</typeparam>
public interface ITenantResolver<TKey> where TKey : notnull
{
    /// <summary>
    /// Resolves the current tenant identifier.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The tenant identifier, or null if no tenant could be resolved.</returns>
    Task<TKey?> ResolveTenantAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the priority of this resolver. Higher values indicate higher priority.
    /// Used when multiple resolvers are registered to determine resolution order.
    /// </summary>
    int Priority => 0;
}

/// <summary>
/// Validates resolved tenant identifiers.
/// </summary>
/// <typeparam name="TKey">The type of the tenant identifier.</typeparam>
public interface ITenantValidator<TKey> where TKey : notnull
{
    /// <summary>
    /// Validates whether a tenant identifier is valid and active.
    /// </summary>
    /// <param name="tenantId">The tenant identifier to validate.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True if the tenant is valid; otherwise, false.</returns>
    Task<bool> ValidateTenantAsync(TKey tenantId, CancellationToken cancellationToken = default);
}

/// <summary>
/// Composite resolver that chains multiple resolvers with fallback support.
/// </summary>
/// <typeparam name="TKey">The type of the tenant identifier.</typeparam>
public interface ITenantResolverPipeline<TKey> where TKey : notnull
{
    /// <summary>
    /// Resolves the tenant using the configured pipeline of resolvers.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The resolved tenant identifier, or null if no resolver succeeded.</returns>
    Task<TKey?> ResolveAsync(CancellationToken cancellationToken = default);
}
