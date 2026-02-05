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
    public TenantContext<TKey>? TenantContext
    {
        get => TenantContextCurrent.Value?.Context;
    }

    /// <inheritdoc />
    public void SetTenantContext(TenantContext<TKey>? context)
    {
        var holder = TenantContextCurrent.Value;
        if (holder != null)
        {
            holder.Context = null;
        }

        if (context != null)
        {
            TenantContextCurrent.Value = new TenantContextHolder<TKey> { Context = context };
        }
    }

    private class TenantContextHolder<T> where T : notnull
    {
        public TenantContext<T>? Context;
    }
}
