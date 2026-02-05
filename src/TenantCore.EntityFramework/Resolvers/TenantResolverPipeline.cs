using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using TenantCore.EntityFramework.Abstractions;
using TenantCore.EntityFramework.Configuration;
using TenantCore.EntityFramework.Events;

namespace TenantCore.EntityFramework.Resolvers;

/// <summary>
/// Pipeline that chains multiple tenant resolvers with caching and validation support.
/// </summary>
/// <typeparam name="TKey">The type of the tenant identifier.</typeparam>
public class TenantResolverPipeline<TKey> : ITenantResolverPipeline<TKey> where TKey : notnull
{
    private readonly IEnumerable<ITenantResolver<TKey>> _resolvers;
    private readonly ITenantValidator<TKey>? _validator;
    private readonly ITenantContextAccessor<TKey> _contextAccessor;
    private readonly ITenantEventPublisher<TKey> _eventPublisher;
    private readonly TenantCoreOptions _options;
    private readonly IMemoryCache? _cache;
    private readonly ILogger<TenantResolverPipeline<TKey>> _logger;

    public TenantResolverPipeline(
        IEnumerable<ITenantResolver<TKey>> resolvers,
        ITenantContextAccessor<TKey> contextAccessor,
        ITenantEventPublisher<TKey> eventPublisher,
        TenantCoreOptions options,
        ILogger<TenantResolverPipeline<TKey>> logger,
        ITenantValidator<TKey>? validator = null,
        IMemoryCache? cache = null)
    {
        _resolvers = resolvers.OrderByDescending(r => r.Priority);
        _validator = validator;
        _contextAccessor = contextAccessor;
        _eventPublisher = eventPublisher;
        _options = options;
        _cache = cache;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<TKey?> ResolveAsync(CancellationToken cancellationToken = default)
    {
        foreach (var resolver in _resolvers)
        {
            var resolverName = resolver.GetType().Name;

            try
            {
                _logger.LogTrace("Attempting to resolve tenant using {ResolverName}", resolverName);

                var tenantId = await resolver.ResolveTenantAsync(cancellationToken);

                if (tenantId != null && !EqualityComparer<TKey>.Default.Equals(tenantId, default!))
                {
                    _logger.LogDebug("Resolved tenant {TenantId} using {ResolverName}", tenantId, resolverName);

                    // Validate if configured
                    if (_options.ValidateTenantOnResolution && _validator != null)
                    {
                        var isValid = await ValidateWithCacheAsync(tenantId, cancellationToken);
                        if (!isValid)
                        {
                            _logger.LogWarning("Tenant {TenantId} validation failed", tenantId);
                            continue;
                        }
                    }

                    // Set context
                    var schemaName = _options.SchemaPerTenant.GenerateSchemaName(tenantId);
                    _contextAccessor.SetTenantContext(new TenantContext<TKey>(tenantId, schemaName));

                    // Publish event
                    await _eventPublisher.PublishTenantResolvedAsync(tenantId, resolverName, cancellationToken);

                    return tenantId;
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error resolving tenant using {ResolverName}", resolverName);
            }
        }

        _logger.LogDebug("No tenant could be resolved");
        return HandleTenantNotFound();
    }

    private async Task<bool> ValidateWithCacheAsync(TKey tenantId, CancellationToken cancellationToken)
    {
        if (_validator == null)
            return true;

        var cacheKey = $"tenant_valid_{tenantId}";

        if (_options.EnableTenantCaching && _cache != null)
        {
            if (_cache.TryGetValue(cacheKey, out bool cachedResult))
            {
                return cachedResult;
            }
        }

        var isValid = await _validator.ValidateTenantAsync(tenantId, cancellationToken);

        if (_options.EnableTenantCaching && _cache != null)
        {
            _cache.Set(cacheKey, isValid, _options.TenantCacheDuration);
        }

        return isValid;
    }

    private TKey? HandleTenantNotFound()
    {
        return _options.TenantNotFoundBehavior switch
        {
            TenantNotFoundBehavior.Throw => throw new TenantNotFoundException("Unable to resolve tenant"),
            TenantNotFoundBehavior.ReturnNull => default,
            TenantNotFoundBehavior.UseDefault => default, // Caller should handle default tenant
            _ => default
        };
    }
}
