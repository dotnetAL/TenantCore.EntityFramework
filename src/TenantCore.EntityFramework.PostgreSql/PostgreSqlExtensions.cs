using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using TenantCore.EntityFramework.Abstractions;
using TenantCore.EntityFramework.Configuration;
using TenantCore.EntityFramework.Context;

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
        services.AddTenantCorePostgreSql();

        services.AddDbContextFactory<TContext>((sp, options) =>
        {
            var tenantAccessor = sp.GetService<ITenantContextAccessor<TKey>>();
            var tenantOptions = sp.GetRequiredService<TenantCoreOptions>();

            // Determine schema for this context
            var schema = tenantAccessor?.TenantContext?.SchemaName;

            options.UseNpgsql(connectionString, npgsql =>
            {
                // Set default schema in connection if tenant is resolved
                if (!string.IsNullOrEmpty(schema))
                {
                    npgsql.MigrationsHistoryTable("__EFMigrationsHistory", schema);
                }

                npgsqlOptionsAction?.Invoke(npgsql);
            });
        });

        services.AddScoped<TContext>(sp =>
        {
            var factory = sp.GetRequiredService<IDbContextFactory<TContext>>();
            return factory.CreateDbContext();
        });

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
            if (!string.IsNullOrEmpty(schema))
            {
                npgsql.MigrationsHistoryTable("__EFMigrationsHistory", schema);
            }

            npgsqlOptionsAction?.Invoke(npgsql);
        });

        return optionsBuilder;
    }
}
