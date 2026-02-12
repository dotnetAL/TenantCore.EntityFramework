using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using TenantCore.EntityFramework.Abstractions;
using TenantCore.EntityFramework.Configuration;
using TenantCore.EntityFramework.Context;
using TenantCore.EntityFramework.ControlDb;

namespace TenantCore.EntityFramework.Validators;

/// <summary>
/// Validates that a tenant exists and is active by checking that its schema exists
/// in the database. When a control database is available, also verifies the tenant
/// status is Active
/// by looking up the tenant record using <c>tenantId.ToString()</c> as the slug.
/// This assumes the slug matches the string representation of the tenant ID,
/// which is the default when tenants are provisioned via <c>ProvisionTenantAsync</c>.
/// </summary>
/// <typeparam name="TContext">The DbContext type used to access the database.</typeparam>
/// <typeparam name="TKey">The type of the tenant identifier.</typeparam>
public class ActiveTenantExistsValidator<TContext, TKey> : ITenantValidator<TKey>
    where TContext : TenantDbContext<TKey>
    where TKey : notnull
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ISchemaManager _schemaManager;
    private readonly TenantCoreOptions _options;
    private readonly ITenantStore? _tenantStore;
    private readonly ILogger<ActiveTenantExistsValidator<TContext, TKey>> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="ActiveTenantExistsValidator{TContext, TKey}"/> class.
    /// </summary>
    /// <param name="serviceProvider">The service provider for creating scoped services.</param>
    /// <param name="schemaManager">The schema manager for checking schema existence.</param>
    /// <param name="options">The tenant configuration options.</param>
    /// <param name="logger">The logger instance.</param>
    /// <param name="tenantStore">Optional tenant store for control database validation.</param>
    public ActiveTenantExistsValidator(
        IServiceProvider serviceProvider,
        ISchemaManager schemaManager,
        TenantCoreOptions options,
        ILogger<ActiveTenantExistsValidator<TContext, TKey>> logger,
        ITenantStore? tenantStore = null)
    {
        _serviceProvider = serviceProvider;
        _schemaManager = schemaManager;
        _options = options;
        _logger = logger;
        _tenantStore = tenantStore;
    }

    /// <inheritdoc />
    public async Task<bool> ValidateTenantAsync(TKey tenantId, CancellationToken cancellationToken = default)
    {
        var schemaName = _options.SchemaPerTenant.GenerateSchemaName(tenantId);

        _logger.LogDebug("Validating tenant {TenantId} schema {SchemaName}", tenantId, schemaName);

        using var scope = _serviceProvider.CreateScope();
        var contextFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<TContext>>();
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);

        var schemaExists = await _schemaManager.SchemaExistsAsync(context, schemaName, cancellationToken);
        if (!schemaExists)
        {
            _logger.LogDebug("Schema {SchemaName} does not exist for tenant {TenantId}", schemaName, tenantId);
            return false;
        }

        if (_tenantStore != null)
        {
            var tenantRecord = await _tenantStore.GetTenantBySlugAsync(tenantId.ToString()!, cancellationToken);
            if (tenantRecord == null)
            {
                _logger.LogDebug("Tenant {TenantId} not found in control database", tenantId);
                return false;
            }

            if (tenantRecord.Status != TenantStatus.Active)
            {
                _logger.LogDebug("Tenant {TenantId} has status {Status}, expected Active", tenantId, tenantRecord.Status);
                return false;
            }
        }

        return true;
    }
}
