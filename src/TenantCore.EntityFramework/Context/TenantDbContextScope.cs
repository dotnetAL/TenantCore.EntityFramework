using Microsoft.EntityFrameworkCore;

namespace TenantCore.EntityFramework.Context;

/// <summary>
/// A lightweight scope that holds a tenant-scoped <typeparamref name="TContext"/> and
/// restores the previous tenant context on disposal.
/// </summary>
/// <typeparam name="TContext">The DbContext type.</typeparam>
public sealed class TenantDbContextScope<TContext> : IAsyncDisposable where TContext : DbContext
{
    private readonly IDisposable _tenantScope;
    private bool _disposed;

    /// <summary>
    /// Gets the tenant-scoped DbContext.
    /// </summary>
    public TContext Context { get; }

    internal TenantDbContextScope(TContext context, IDisposable tenantScope)
    {
        Context = context;
        _tenantScope = tenantScope;
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;

        try
        {
            await Context.DisposeAsync();
        }
        finally
        {
            _tenantScope.Dispose();
        }
    }
}
