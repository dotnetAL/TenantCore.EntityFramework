using TenantCore.EntityFramework.Abstractions;

namespace TenantCore.EntityFramework.Context;

/// <summary>
/// Creates a temporary scope for a specific tenant. Disposes back to the original tenant context.
/// </summary>
/// <typeparam name="TKey">The type of the tenant identifier.</typeparam>
public sealed class TenantScope<TKey> : IDisposable where TKey : notnull
{
    private readonly ITenantContextAccessor<TKey> _accessor;
    private readonly TenantContext<TKey>? _previousContext;
    private bool _disposed;

    /// <summary>
    /// Creates a new tenant scope with the specified tenant.
    /// </summary>
    /// <param name="accessor">The tenant context accessor.</param>
    /// <param name="tenantId">The tenant identifier to scope to.</param>
    /// <param name="schemaName">Optional schema name for the tenant.</param>
    public TenantScope(ITenantContextAccessor<TKey> accessor, TKey tenantId, string? schemaName = null)
    {
        _accessor = accessor;
        _previousContext = accessor.TenantContext;
        accessor.SetTenantContext(new TenantContext<TKey>(tenantId) { SchemaName = schemaName });
    }

    /// <summary>
    /// Creates a new tenant scope with the specified tenant context.
    /// </summary>
    /// <param name="accessor">The tenant context accessor.</param>
    /// <param name="context">The tenant context to use.</param>
    public TenantScope(ITenantContextAccessor<TKey> accessor, TenantContext<TKey> context)
    {
        _accessor = accessor;
        _previousContext = accessor.TenantContext;
        accessor.SetTenantContext(context);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _accessor.SetTenantContext(_previousContext);
    }
}

/// <summary>
/// Factory for creating tenant scopes.
/// </summary>
/// <typeparam name="TKey">The type of the tenant identifier.</typeparam>
public interface ITenantScopeFactory<TKey> where TKey : notnull
{
    /// <summary>
    /// Creates a new tenant scope for the specified tenant.
    /// </summary>
    /// <param name="tenantId">The tenant identifier.</param>
    /// <returns>A disposable tenant scope.</returns>
    TenantScope<TKey> CreateScope(TKey tenantId);

    /// <summary>
    /// Creates a new tenant scope with the specified context.
    /// </summary>
    /// <param name="context">The tenant context.</param>
    /// <returns>A disposable tenant scope.</returns>
    TenantScope<TKey> CreateScope(TenantContext<TKey> context);
}

/// <summary>
/// Default implementation of <see cref="ITenantScopeFactory{TKey}"/>.
/// </summary>
/// <typeparam name="TKey">The type of the tenant identifier.</typeparam>
public class TenantScopeFactory<TKey> : ITenantScopeFactory<TKey> where TKey : notnull
{
    private readonly ITenantContextAccessor<TKey> _accessor;
    private readonly Configuration.SchemaPerTenantOptions _schemaOptions;

    /// <summary>
    /// Initializes a new instance of the <see cref="TenantScopeFactory{TKey}"/> class.
    /// </summary>
    /// <param name="accessor">The tenant context accessor.</param>
    /// <param name="options">The tenant configuration options.</param>
    public TenantScopeFactory(
        ITenantContextAccessor<TKey> accessor,
        Configuration.TenantCoreOptions options)
    {
        _accessor = accessor;
        _schemaOptions = options.SchemaPerTenant;
    }

    /// <inheritdoc />
    public TenantScope<TKey> CreateScope(TKey tenantId)
    {
        var schemaName = _schemaOptions.GenerateSchemaName(tenantId);
        return new TenantScope<TKey>(_accessor, tenantId, schemaName);
    }

    /// <inheritdoc />
    public TenantScope<TKey> CreateScope(TenantContext<TKey> context)
    {
        return new TenantScope<TKey>(_accessor, context);
    }
}
