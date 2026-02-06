using TenantCore.EntityFramework.Abstractions;

namespace TenantCore.EntityFramework.Context;

/// <summary>
/// Default implementation of <see cref="ITenantContextAccessor{TKey}"/> using AsyncLocal storage.
/// </summary>
/// <typeparam name="TKey">The type of the tenant identifier.</typeparam>
public class TenantContextAccessor<TKey> : ITenantContextAccessor<TKey> where TKey : notnull
{
    private static readonly AsyncLocal<TenantContextHolder<TKey>> TenantContextCurrent = new();

    /// <inheritdoc />
    public TenantContext<TKey>? TenantContext => TenantContextCurrent.Value?.Context;

    /// <inheritdoc />
    public void SetTenantContext(TenantContext<TKey>? context)
    {
        var holder = TenantContextCurrent.Value;

        if (context != null)
        {
            // Setting a new context - create new holder to isolate from parent async contexts
            TenantContextCurrent.Value = new TenantContextHolder<TKey> { Context = context };
        }
        else
        {
            // Clearing context - explicitly set to new empty holder to prevent
            // inheriting stale context from parent async contexts
            if (holder != null)
            {
                holder.Context = null;
            }
            // CRITICAL: Always create a new holder when clearing, even if holder was null.
            // This ensures we don't inherit a stale context from a parent async context.
            TenantContextCurrent.Value = new TenantContextHolder<TKey> { Context = null };
        }
    }

    private class TenantContextHolder<T> where T : notnull
    {
        public TenantContext<T>? Context;
    }
}
