using TenantCore.EntityFramework.Abstractions;
using TenantCore.EntityFramework.ControlDb;

namespace TenantCore.EntityFramework.Configuration;

/// <summary>
/// Builder for configuring TenantCore options.
/// </summary>
/// <typeparam name="TKey">The type of the tenant identifier.</typeparam>
public class TenantCoreOptionsBuilder<TKey> where TKey : notnull
{
    private readonly TenantCoreOptions _options;

    internal TenantCoreOptionsBuilder(TenantCoreOptions options)
    {
        _options = options;
    }

    /// <summary>
    /// Configures the connection string for the database.
    /// </summary>
    /// <param name="connectionString">The connection string.</param>
    /// <returns>The builder for chaining.</returns>
    public TenantCoreOptionsBuilder<TKey> UseConnectionString(string connectionString)
    {
        _options.ConnectionString = connectionString;
        return this;
    }

    /// <summary>
    /// Configures schema-per-tenant isolation strategy.
    /// </summary>
    /// <param name="configure">Action to configure schema options.</param>
    /// <returns>The builder for chaining.</returns>
    public TenantCoreOptionsBuilder<TKey> UseSchemaPerTenant(Action<SchemaPerTenantOptions>? configure = null)
    {
        configure?.Invoke(_options.SchemaPerTenant);
        return this;
    }

    /// <summary>
    /// Registers a tenant resolver.
    /// </summary>
    /// <typeparam name="TResolver">The resolver type.</typeparam>
    /// <returns>The builder for chaining.</returns>
    public TenantCoreOptionsBuilder<TKey> UseTenantResolver<TResolver>()
        where TResolver : class, ITenantResolver<TKey>
    {
        _options.TenantResolverTypes.Add(typeof(TResolver));
        return this;
    }

    /// <summary>
    /// Registers a tenant validator.
    /// </summary>
    /// <typeparam name="TValidator">The validator type.</typeparam>
    /// <returns>The builder for chaining.</returns>
    public TenantCoreOptionsBuilder<TKey> UseTenantValidator<TValidator>()
        where TValidator : class, ITenantValidator<TKey>
    {
        _options.TenantValidatorType = typeof(TValidator);
        return this;
    }

    /// <summary>
    /// Registers a tenant seeder.
    /// </summary>
    /// <typeparam name="TSeeder">The seeder type.</typeparam>
    /// <returns>The builder for chaining.</returns>
    public TenantCoreOptionsBuilder<TKey> UseTenantSeeder<TSeeder>()
        where TSeeder : class, ITenantSeeder<TKey>
    {
        _options.TenantSeederTypes.Add(typeof(TSeeder));
        return this;
    }

    /// <summary>
    /// Configures migration options.
    /// </summary>
    /// <param name="configure">Action to configure migration options.</param>
    /// <returns>The builder for chaining.</returns>
    public TenantCoreOptionsBuilder<TKey> ConfigureMigrations(Action<MigrationOptions> configure)
    {
        configure(_options.Migrations);
        return this;
    }

    /// <summary>
    /// Sets the behavior when tenant resolution fails.
    /// </summary>
    /// <param name="behavior">The behavior to use.</param>
    /// <returns>The builder for chaining.</returns>
    public TenantCoreOptionsBuilder<TKey> OnTenantNotFound(TenantNotFoundBehavior behavior)
    {
        _options.TenantNotFoundBehavior = behavior;
        return this;
    }

    /// <summary>
    /// Enables tenant caching with the specified duration.
    /// </summary>
    /// <param name="duration">The cache duration.</param>
    /// <returns>The builder for chaining.</returns>
    public TenantCoreOptionsBuilder<TKey> EnableCaching(TimeSpan? duration = null)
    {
        _options.EnableTenantCaching = true;
        if (duration.HasValue)
        {
            _options.TenantCacheDuration = duration.Value;
        }
        return this;
    }

    /// <summary>
    /// Disables tenant caching.
    /// </summary>
    /// <returns>The builder for chaining.</returns>
    public TenantCoreOptionsBuilder<TKey> DisableCaching()
    {
        _options.EnableTenantCaching = false;
        return this;
    }

    /// <summary>
    /// Enables automatic tenant provisioning on first access.
    /// </summary>
    /// <returns>The builder for chaining.</returns>
    public TenantCoreOptionsBuilder<TKey> EnableAutoProvisioning()
    {
        _options.AutoProvisionTenant = true;
        return this;
    }

    /// <summary>
    /// Disables tenant validation after resolution.
    /// </summary>
    /// <returns>The builder for chaining.</returns>
    public TenantCoreOptionsBuilder<TKey> DisableTenantValidation()
    {
        _options.ValidateTenantOnResolution = false;
        return this;
    }

    /// <summary>
    /// Configures the control database for storing tenant metadata.
    /// This enables the built-in control database implementation.
    /// </summary>
    /// <param name="connectionString">The connection string for the control database.</param>
    /// <param name="configure">Optional action to configure control database options.</param>
    /// <returns>The builder for chaining.</returns>
    public TenantCoreOptionsBuilder<TKey> UseControlDatabase(
        string connectionString,
        Action<ControlDbOptions>? configure = null)
    {
        _options.ControlDb.Enabled = true;
        _options.ControlDb.ConnectionString = connectionString;
        configure?.Invoke(_options.ControlDb);
        return this;
    }

    /// <summary>
    /// Registers a custom tenant store implementation (BYO pattern).
    /// Use this when you want to provide your own tenant storage mechanism.
    /// </summary>
    /// <typeparam name="TStore">The custom tenant store type.</typeparam>
    /// <returns>The builder for chaining.</returns>
    public TenantCoreOptionsBuilder<TKey> UseTenantStore<TStore>()
        where TStore : class, ITenantStore
    {
        _options.ControlDb.Enabled = true;
        _options.ControlDb.CustomTenantStoreType = typeof(TStore);
        return this;
    }

    /// <summary>
    /// Configures the control database options.
    /// </summary>
    /// <param name="configure">Action to configure control database options.</param>
    /// <returns>The builder for chaining.</returns>
    public TenantCoreOptionsBuilder<TKey> ConfigureControlDatabase(Action<ControlDbOptions> configure)
    {
        configure(_options.ControlDb);
        return this;
    }
}
