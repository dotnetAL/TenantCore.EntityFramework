using Microsoft.EntityFrameworkCore;
using TenantCore.EntityFramework.ControlDb.Entities;

namespace TenantCore.EntityFramework.ControlDb;

/// <summary>
/// Default implementation of <see cref="ITenantStore"/> using <see cref="ControlDbContext"/>.
/// </summary>
public class ControlDbTenantStore : ITenantStore
{
    private readonly IDbContextFactory<ControlDbContext> _contextFactory;
    private readonly ITenantPasswordProtector _passwordProtector;

    /// <summary>
    /// Initializes a new instance of the <see cref="ControlDbTenantStore"/> class.
    /// </summary>
    /// <param name="contextFactory">The DbContext factory for creating ControlDbContext instances.</param>
    /// <param name="passwordProtector">The password protector for encrypting/decrypting passwords.</param>
    public ControlDbTenantStore(
        IDbContextFactory<ControlDbContext> contextFactory,
        ITenantPasswordProtector passwordProtector)
    {
        _contextFactory = contextFactory;
        _passwordProtector = passwordProtector;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<TenantRecord>> GetTenantsAsync(
        TenantStatus[]? statuses = null,
        CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

        IQueryable<TenantEntity> query = context.Tenants;

        if (statuses is { Length: > 0 })
        {
            query = query.Where(t => statuses.Contains(t.Status));
        }

        var entities = await query
            .OrderBy(t => t.TenantSlug)
            .ToListAsync(cancellationToken);

        return entities.Select(e => e.ToRecord()).ToList();
    }

    /// <inheritdoc />
    public async Task<TenantRecord?> GetTenantAsync(Guid tenantId, CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

        var entity = await context.Tenants
            .AsNoTracking()
            .FirstOrDefaultAsync(t => t.TenantId == tenantId, cancellationToken);

        return entity?.ToRecord();
    }

    /// <inheritdoc />
    public async Task<TenantRecord?> GetTenantBySlugAsync(string slug, CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

        var entity = await context.Tenants
            .AsNoTracking()
            .FirstOrDefaultAsync(t => t.TenantSlug == slug, cancellationToken);

        return entity?.ToRecord();
    }

    /// <inheritdoc />
    public async Task<TenantRecord?> GetTenantByApiKeyAsync(string apiKey, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            return null;
        }

        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

        // Get all tenants with API keys (only active ones for performance)
        // Note: This requires iterating through tenants since we can't query by hash
        // when using salted hashes. For large deployments, consider caching or
        // using a separate API key lookup table with the key prefix.
        var tenantsWithApiKeys = await context.Tenants
            .AsNoTracking()
            .Where(t => t.TenantApiKeyHash != null && t.Status == TenantStatus.Active)
            .ToListAsync(cancellationToken);

        // Verify the API key against each stored hash
        foreach (var tenant in tenantsWithApiKeys)
        {
            if (tenant.TenantApiKeyHash != null && ApiKeyHasher.VerifyHash(apiKey, tenant.TenantApiKeyHash))
            {
                return tenant.ToRecord();
            }
        }

        return null;
    }

    /// <inheritdoc />
    public async Task<TenantRecord> CreateTenantAsync(
        Guid tenantId,
        CreateTenantRequest request,
        CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

        var now = DateTime.UtcNow;
        var entity = new TenantEntity
        {
            TenantId = tenantId,
            TenantSlug = request.TenantSlug,
            Status = TenantStatus.Pending,
            TenantSchema = request.TenantSchema,
            TenantDatabase = request.TenantDatabase,
            TenantDbServer = request.TenantDbServer,
            TenantDbUser = request.TenantDbUser,
            TenantDbPasswordEncrypted = request.TenantDbPassword != null
                ? _passwordProtector.Protect(request.TenantDbPassword)
                : null,
            TenantApiKeyHash = request.TenantApiKey != null
                ? ApiKeyHasher.ComputeHash(request.TenantApiKey)
                : null,
            CreatedAt = now,
            UpdatedAt = now
        };

        context.Tenants.Add(entity);
        await context.SaveChangesAsync(cancellationToken);

        return entity.ToRecord();
    }

    /// <inheritdoc />
    public async Task<TenantRecord> UpdateTenantAsync(
        Guid tenantId,
        string? slug = null,
        string? database = null,
        string? dbServer = null,
        string? dbUser = null,
        string? dbPassword = null,
        string? apiKey = null,
        CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

        var entity = await context.Tenants.FindAsync([tenantId], cancellationToken)
            ?? throw new InvalidOperationException($"Tenant with ID '{tenantId}' not found.");

        if (slug != null)
        {
            entity.TenantSlug = slug;
        }

        if (database != null)
        {
            entity.TenantDatabase = database;
        }

        if (dbServer != null)
        {
            entity.TenantDbServer = dbServer;
        }

        if (dbUser != null)
        {
            entity.TenantDbUser = dbUser;
        }

        if (dbPassword != null)
        {
            entity.TenantDbPasswordEncrypted = _passwordProtector.Protect(dbPassword);
        }

        if (apiKey != null)
        {
            entity.TenantApiKeyHash = ApiKeyHasher.ComputeHash(apiKey);
        }

        entity.UpdatedAt = DateTime.UtcNow;

        await context.SaveChangesAsync(cancellationToken);

        return entity.ToRecord();
    }

    /// <inheritdoc />
    public async Task<TenantRecord> UpdateStatusAsync(
        Guid tenantId,
        TenantStatus status,
        CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

        var entity = await context.Tenants.FindAsync([tenantId], cancellationToken)
            ?? throw new InvalidOperationException($"Tenant with ID '{tenantId}' not found.");

        entity.Status = status;
        entity.UpdatedAt = DateTime.UtcNow;

        await context.SaveChangesAsync(cancellationToken);

        return entity.ToRecord();
    }

    /// <inheritdoc />
    public async Task<bool> DeleteTenantAsync(Guid tenantId, CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

        var entity = await context.Tenants.FindAsync([tenantId], cancellationToken);
        if (entity == null)
        {
            return false;
        }

        context.Tenants.Remove(entity);
        await context.SaveChangesAsync(cancellationToken);

        return true;
    }

    /// <inheritdoc />
    public async Task<string?> GetTenantPasswordAsync(Guid tenantId, CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

        var entity = await context.Tenants
            .AsNoTracking()
            .FirstOrDefaultAsync(t => t.TenantId == tenantId, cancellationToken);

        if (entity?.TenantDbPasswordEncrypted == null)
        {
            return null;
        }

        return _passwordProtector.Unprotect(entity.TenantDbPasswordEncrypted);
    }
}
