using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using TenantCore.EntityFramework.Abstractions;
using TenantCore.EntityFramework.Configuration;
using TenantCore.EntityFramework.Context;
using TenantCore.EntityFramework.Lifecycle;
using TenantCore.EntityFramework.Migrations;
using TenantCore.EntityFramework.Strategies;

namespace TenantCore.EntityFramework.PostgreSql;

/// <summary>
/// Extension methods for PostgreSQL integration.
/// </summary>
public static class PostgreSqlExtensions
{
    /// <summary>
    /// Configures TenantCore to use PostgreSQL as the database provider.
    /// </summary>
    /// <typeparam name="TKey">The type of the tenant identifier.</typeparam>
    /// <param name="builder">The options builder.</param>
    /// <param name="connectionString">The PostgreSQL connection string.</param>
    /// <returns>The builder for chaining.</returns>
    public static TenantCoreOptionsBuilder<TKey> UsePostgreSql<TKey>(
        this TenantCoreOptionsBuilder<TKey> builder,
        string connectionString) where TKey : notnull
    {
        return builder.UseConnectionString(connectionString);
    }

    /// <summary>
    /// Adds PostgreSQL-specific services for TenantCore.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddTenantCorePostgreSql(this IServiceCollection services)
    {
        services.TryAddSingleton<ISchemaManager, PostgreSqlSchemaManager>();
        return services;
    }

    /// <summary>
    /// Adds a tenant-aware DbContext configured for PostgreSQL.
    /// </summary>
    /// <typeparam name="TContext">The DbContext type.</typeparam>
    /// <typeparam name="TKey">The type of the tenant identifier.</typeparam>
    /// <param name="services">The service collection.</param>
    /// <param name="connectionString">The PostgreSQL connection string.</param>
    /// <param name="npgsqlOptionsAction">Optional action to configure Npgsql options.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddTenantDbContextPostgreSql<TContext, TKey>(
        this IServiceCollection services,
        string connectionString,
        Action<Npgsql.EntityFrameworkCore.PostgreSQL.Infrastructure.NpgsqlDbContextOptionsBuilder>? npgsqlOptionsAction = null)
        where TContext : TenantDbContext<TKey>
        where TKey : notnull
    {
        return services.AddTenantDbContextPostgreSql<TContext, TKey>(
            connectionString,
            migrationsAssembly: null,
            npgsqlOptionsAction);
    }

    /// <summary>
    /// Adds a tenant-aware DbContext configured for PostgreSQL with migrations from a separate assembly
    /// and a per-context migration history table name.
    /// </summary>
    /// <typeparam name="TContext">The DbContext type.</typeparam>
    /// <typeparam name="TKey">The type of the tenant identifier.</typeparam>
    /// <param name="services">The service collection.</param>
    /// <param name="connectionString">The PostgreSQL connection string.</param>
    /// <param name="migrationsAssembly">The name of the assembly containing migrations. If null, uses the DbContext's assembly.</param>
    /// <param name="migrationHistoryTable">Per-context migration history table name. If null, falls back to global <see cref="TenantCoreOptions.Migrations"/>.</param>
    /// <param name="npgsqlOptionsAction">Optional action to configure Npgsql options.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddTenantDbContextPostgreSql<TContext, TKey>(
        this IServiceCollection services,
        string connectionString,
        string? migrationsAssembly,
        string? migrationHistoryTable,
        Action<Npgsql.EntityFrameworkCore.PostgreSQL.Infrastructure.NpgsqlDbContextOptionsBuilder>? npgsqlOptionsAction = null)
        where TContext : TenantDbContext<TKey>
        where TKey : notnull
    {
        if (migrationHistoryTable != null)
        {
            services.AddSingleton(new TenantDbContextOptions<TContext>
            {
                MigrationHistoryTable = migrationHistoryTable
            });
        }

        return services.AddTenantDbContextPostgreSql<TContext, TKey>(
            connectionString, migrationsAssembly, npgsqlOptionsAction);
    }

    /// <summary>
    /// Adds a tenant-aware DbContext configured for PostgreSQL with migrations from a separate assembly.
    /// </summary>
    /// <typeparam name="TContext">The DbContext type.</typeparam>
    /// <typeparam name="TKey">The type of the tenant identifier.</typeparam>
    /// <param name="services">The service collection.</param>
    /// <param name="connectionString">The PostgreSQL connection string.</param>
    /// <param name="migrationsAssembly">The name of the assembly containing migrations. If null, uses the DbContext's assembly.</param>
    /// <param name="npgsqlOptionsAction">Optional action to configure Npgsql options.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddTenantDbContextPostgreSql<TContext, TKey>(
        this IServiceCollection services,
        string connectionString,
        string? migrationsAssembly,
        Action<Npgsql.EntityFrameworkCore.PostgreSQL.Infrastructure.NpgsqlDbContextOptionsBuilder>? npgsqlOptionsAction = null)
        where TContext : TenantDbContext<TKey>
        where TKey : notnull
    {
        services.AddTenantCorePostgreSql();

        // Register the connection interceptor as a singleton so it's shared across contexts
        services.TryAddSingleton<TenantSchemaConnectionInterceptor<TKey>>();

        services.AddDbContextFactory<TContext>((sp, options) =>
        {
            // Get the current tenant schema at context creation time
            var tenantAccessor = sp.GetService<ITenantContextAccessor<TKey>>();
            var currentSchema = tenantAccessor?.TenantContext?.SchemaName;

            // Read configured migration history table name and separation setting
            // Per-context options take precedence over global options
            var contextOptions = sp.GetService<TenantDbContextOptions<TContext>>();
            var tenantCoreOptions = sp.GetService<TenantCoreOptions>();
            var migrationHistoryTable = contextOptions?.MigrationHistoryTable
                ?? tenantCoreOptions?.Migrations.MigrationHistoryTable
                ?? "__EFMigrationsHistory";
            var separateMigrationHistory = tenantCoreOptions?.Migrations.SeparateMigrationHistory ?? true;

            options.UseNpgsql(connectionString, npgsql =>
            {
                // Set migrations assembly if specified
                if (!string.IsNullOrEmpty(migrationsAssembly))
                {
                    npgsql.MigrationsAssembly(migrationsAssembly);
                }

                // Set migrations history table to the current tenant's schema
                // This ensures the history table is created in the tenant schema
                if (separateMigrationHistory && !string.IsNullOrEmpty(currentSchema))
                {
                    npgsql.MigrationsHistoryTable(migrationHistoryTable, currentSchema);
                }

                npgsqlOptionsAction?.Invoke(npgsql);
            });

            // Register the tenant-aware model cache key factory to ensure
            // EF Core creates separate models for each tenant schema
            options.ReplaceService<IModelCacheKeyFactory, TenantModelCacheKeyFactory<TKey>>();

            // Add the connection interceptor to set search_path on every connection open
            // This ensures migrations and queries use the correct tenant schema
            var interceptor = sp.GetRequiredService<TenantSchemaConnectionInterceptor<TKey>>();
            options.AddInterceptors(interceptor);
        });

        services.AddScoped<TContext>(sp =>
        {
            var factory = sp.GetRequiredService<IDbContextFactory<TContext>>();
            return factory.CreateDbContext();
        });

        // Register strategy (scoped to allow ITenantStore dependency when control database is enabled)
        services.TryAddScoped<ITenantStrategy<TKey>, SchemaPerTenantStrategy<TKey>>();

        // Migration runner (scoped to allow ITenantStrategy dependency)
        services.TryAddScoped<TenantMigrationRunner<TContext, TKey>>();
        services.TryAddSingleton<MigrationTracker>();

        // Tenant manager
        services.TryAddScoped<ITenantManager<TKey>, TenantManager<TContext, TKey>>();

        return services;
    }

    /// <summary>
    /// Configures the DbContext options for PostgreSQL with tenant support.
    /// </summary>
    /// <param name="optionsBuilder">The options builder.</param>
    /// <param name="connectionString">The PostgreSQL connection string.</param>
    /// <param name="schema">The tenant schema name.</param>
    /// <param name="npgsqlOptionsAction">Optional action to configure Npgsql options.</param>
    /// <returns>The options builder for chaining.</returns>
    public static DbContextOptionsBuilder UseNpgsqlWithTenant(
        this DbContextOptionsBuilder optionsBuilder,
        string connectionString,
        string? schema = null,
        Action<Npgsql.EntityFrameworkCore.PostgreSQL.Infrastructure.NpgsqlDbContextOptionsBuilder>? npgsqlOptionsAction = null)
    {
        optionsBuilder.UseNpgsql(connectionString, npgsql =>
        {
            // Note: MigrationsHistoryTable is not set here. The caller should use
            // TenantSchemaConnectionInterceptor to set search_path, which will cause
            // __EFMigrationsHistory to be created in the current schema.

            npgsqlOptionsAction?.Invoke(npgsql);
        });

        return optionsBuilder;
    }
}
