using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using TenantCore.EntityFramework.Abstractions;
using TenantCore.EntityFramework.Configuration;
using TenantCore.EntityFramework.ControlDb;

namespace TenantCore.EntityFramework.Strategies;

/// <summary>
/// Implements schema-per-tenant isolation strategy where each tenant has its own database schema.
/// </summary>
/// <typeparam name="TKey">The type of the tenant identifier.</typeparam>
public class SchemaPerTenantStrategy<TKey> : ITenantStrategy<TKey> where TKey : notnull
{
    private readonly SchemaPerTenantOptions _options;
    private readonly ISchemaManager _schemaManager;
    private readonly ITenantStore? _tenantStore;
    private readonly ILogger<SchemaPerTenantStrategy<TKey>> _logger;

    /// <inheritdoc />
    public string Name => "SchemaPerTenant";

    /// <summary>
    /// Initializes a new instance of the <see cref="SchemaPerTenantStrategy{TKey}"/> class.
    /// </summary>
    /// <param name="options">The tenant configuration options.</param>
    /// <param name="schemaManager">The schema manager.</param>
    /// <param name="logger">The logger instance.</param>
    /// <param name="tenantStore">The optional tenant store for control database integration.</param>
    public SchemaPerTenantStrategy(
        TenantCoreOptions options,
        ISchemaManager schemaManager,
        ILogger<SchemaPerTenantStrategy<TKey>> logger,
        ITenantStore? tenantStore = null)
    {
        _options = options.SchemaPerTenant;
        _schemaManager = schemaManager;
        _tenantStore = tenantStore;
        _logger = logger;
    }

    /// <inheritdoc />
    public void ConfigureDbContext(DbContextOptionsBuilder optionsBuilder, TKey tenantId)
    {
        // Schema configuration is handled via OnModelCreating
        // Connection-level search_path is handled by ISchemaManager.SetCurrentSchemaAsync
    }

    /// <inheritdoc />
    public void OnModelCreating(ModelBuilder modelBuilder, TKey tenantId)
    {
        var schemaName = _options.GenerateSchemaName(tenantId);
        modelBuilder.HasDefaultSchema(schemaName);
        _logger.LogDebug("Configured model with schema {Schema} for tenant {TenantId}", schemaName, tenantId);
    }

    /// <inheritdoc />
    public async Task ProvisionTenantAsync(DbContext context, TKey tenantId, CancellationToken cancellationToken = default)
    {
        var schemaName = _options.GenerateSchemaName(tenantId);

        _logger.LogInformation("Provisioning schema {Schema} for tenant {TenantId}", schemaName, tenantId);

        if (await _schemaManager.SchemaExistsAsync(context, schemaName, cancellationToken))
        {
            throw new TenantAlreadyExistsException(tenantId);
        }

        await _schemaManager.CreateSchemaAsync(context, schemaName, cancellationToken);

        _logger.LogInformation("Successfully provisioned schema {Schema} for tenant {TenantId}", schemaName, tenantId);
    }

    /// <inheritdoc />
    public async Task DeleteTenantAsync(DbContext context, TKey tenantId, bool hardDelete = false, CancellationToken cancellationToken = default)
    {
        var schemaName = _options.GenerateSchemaName(tenantId);

        if (!await _schemaManager.SchemaExistsAsync(context, schemaName, cancellationToken))
        {
            throw new TenantNotFoundException(tenantId);
        }

        if (hardDelete)
        {
            _logger.LogWarning("Hard deleting schema {Schema} for tenant {TenantId}", schemaName, tenantId);
            await _schemaManager.DropSchemaAsync(context, schemaName, cascade: true, cancellationToken);
            _logger.LogInformation("Successfully deleted schema {Schema} for tenant {TenantId}", schemaName, tenantId);
        }
        else
        {
            // Soft delete by renaming the schema
            var archivedName = $"{_options.ArchivedSchemaPrefix}{schemaName}_{DateTime.UtcNow:yyyyMMddHHmmss}";
            _logger.LogInformation("Soft deleting tenant {TenantId} by renaming schema {Schema} to {ArchivedSchema}",
                tenantId, schemaName, archivedName);
            await _schemaManager.RenameSchemaAsync(context, schemaName, archivedName, cancellationToken);
        }
    }

    /// <inheritdoc />
    public async Task<bool> TenantExistsAsync(DbContext context, TKey tenantId, CancellationToken cancellationToken = default)
    {
        var schemaName = _options.GenerateSchemaName(tenantId);
        return await _schemaManager.SchemaExistsAsync(context, schemaName, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<IEnumerable<string>> GetTenantsAsync(DbContext context, CancellationToken cancellationToken = default)
    {
        // If tenant store is available, delegate to it
        if (_tenantStore != null)
        {
            var tenants = await _tenantStore.GetTenantsAsync(null, cancellationToken);
            _logger.LogDebug("Using control database to enumerate {Count} tenants", tenants.Count);
            return tenants.Select(t => t.TenantId.ToString());
        }

        // Fall back to schema discovery
        var schemas = await _schemaManager.GetSchemasAsync(context, _options.SchemaPrefix, cancellationToken);
        return schemas.Select(s => _options.ExtractTenantId(s));
    }

    /// <inheritdoc />
    public async Task ArchiveTenantAsync(DbContext context, TKey tenantId, CancellationToken cancellationToken = default)
    {
        var schemaName = _options.GenerateSchemaName(tenantId);

        if (!await _schemaManager.SchemaExistsAsync(context, schemaName, cancellationToken))
        {
            throw new TenantNotFoundException(tenantId);
        }

        var archivedName = $"{_options.ArchivedSchemaPrefix}{schemaName}";
        _logger.LogInformation("Archiving tenant {TenantId} by renaming schema {Schema} to {ArchivedSchema}",
            tenantId, schemaName, archivedName);

        await _schemaManager.RenameSchemaAsync(context, schemaName, archivedName, cancellationToken);
    }

    /// <inheritdoc />
    public async Task RestoreTenantAsync(DbContext context, TKey tenantId, CancellationToken cancellationToken = default)
    {
        var schemaName = _options.GenerateSchemaName(tenantId);
        var archivedName = $"{_options.ArchivedSchemaPrefix}{schemaName}";

        if (!await _schemaManager.SchemaExistsAsync(context, archivedName, cancellationToken))
        {
            throw new TenantNotFoundException(tenantId);
        }

        _logger.LogInformation("Restoring tenant {TenantId} by renaming schema {ArchivedSchema} to {Schema}",
            tenantId, archivedName, schemaName);

        await _schemaManager.RenameSchemaAsync(context, archivedName, schemaName, cancellationToken);
    }
}
