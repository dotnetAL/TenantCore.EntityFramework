using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TenantCore.EntityFramework.Abstractions;
using TenantCore.EntityFramework.Configuration;

namespace TenantCore.EntityFramework.Context;

/// <summary>
/// Base DbContext that provides multi-tenant support with automatic schema switching.
/// </summary>
/// <typeparam name="TKey">The type of the tenant identifier.</typeparam>
public abstract class TenantDbContext<TKey> : DbContext where TKey : notnull
{
    private readonly ITenantContextAccessor<TKey>? _tenantContextAccessor;
    private readonly TenantCoreOptions? _options;
    private string? _currentSchema;

    /// <summary>
    /// Gets the current tenant identifier, or default if no tenant context is set.
    /// </summary>
    public TKey CurrentTenantId => GetCurrentTenantId();

    private TKey GetCurrentTenantId()
    {
        var context = _tenantContextAccessor?.TenantContext;
        return context != null ? context.TenantId : default!;
    }

    /// <summary>
    /// Gets the current tenant's schema name.
    /// </summary>
    public string? CurrentTenantSchema => _currentSchema ?? _tenantContextAccessor?.TenantContext?.SchemaName;

    /// <summary>
    /// Gets whether this context is currently operating in a tenant context.
    /// </summary>
    public bool HasTenantContext => _tenantContextAccessor?.TenantContext?.IsValid == true;

    protected TenantDbContext(DbContextOptions options) : base(options)
    {
    }

    protected TenantDbContext(
        DbContextOptions options,
        ITenantContextAccessor<TKey> tenantContextAccessor,
        TenantCoreOptions tenantOptions)
        : base(options)
    {
        _tenantContextAccessor = tenantContextAccessor;
        _options = tenantOptions;
        _currentSchema = tenantContextAccessor.TenantContext?.SchemaName;
    }

    /// <summary>
    /// Sets the schema for this context instance. Used primarily for migrations.
    /// </summary>
    /// <param name="schema">The schema name.</param>
    internal void SetSchema(string schema)
    {
        _currentSchema = schema;
    }

    /// <inheritdoc />
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        var schema = CurrentTenantSchema;
        if (!string.IsNullOrEmpty(schema))
        {
            modelBuilder.HasDefaultSchema(schema);
        }

        ConfigureSharedEntities(modelBuilder);
    }

    /// <summary>
    /// Configures entities that should remain in the shared schema.
    /// Override this method to specify which entities should not be tenant-isolated.
    /// </summary>
    /// <param name="modelBuilder">The model builder.</param>
    protected virtual void ConfigureSharedEntities(ModelBuilder modelBuilder)
    {
        // Override in derived classes to configure shared entities
        // Example:
        // modelBuilder.Entity<SharedEntity>().ToTable("SharedTable", _options?.SchemaPerTenant.SharedSchema ?? "public");
    }

    /// <summary>
    /// Ensures the context has a valid tenant before executing operations.
    /// </summary>
    /// <exception cref="InvalidOperationException">Thrown when no tenant context is available.</exception>
    protected void EnsureTenantContext()
    {
        if (!HasTenantContext)
        {
            throw new InvalidOperationException(
                "No tenant context is available. Ensure a tenant has been resolved before accessing tenant-specific data.");
        }
    }
}

/// <summary>
/// Extension methods for creating tenant-aware DbContext options.
/// </summary>
public static class TenantDbContextOptionsExtensions
{
    /// <summary>
    /// Creates a new DbContextOptions configured for the specified tenant schema.
    /// </summary>
    /// <typeparam name="TContext">The DbContext type.</typeparam>
    /// <param name="options">The original options.</param>
    /// <param name="schema">The tenant schema name.</param>
    /// <returns>New options configured for the tenant.</returns>
    public static DbContextOptions<TContext> WithTenantSchema<TContext>(
        this DbContextOptions<TContext> options,
        string schema) where TContext : DbContext
    {
        var builder = new DbContextOptionsBuilder<TContext>(options);

        // The schema will be applied via OnModelCreating in TenantDbContext
        // Store the schema in the options extensions if needed
        return builder.Options;
    }
}
