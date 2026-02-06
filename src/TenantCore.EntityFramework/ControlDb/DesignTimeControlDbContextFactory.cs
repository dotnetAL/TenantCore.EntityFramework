using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace TenantCore.EntityFramework.ControlDb;

/// <summary>
/// Abstract base class for design-time factory for creating ControlDbContext for EF Core migrations.
/// Database providers should create a concrete implementation that calls the appropriate UseXxx method.
/// </summary>
/// <remarks>
/// Example implementation for PostgreSQL (in TenantCore.EntityFramework.PostgreSql):
/// <code>
/// public class PostgreSqlControlDbContextFactory : DesignTimeControlDbContextFactoryBase
/// {
///     protected override void ConfigureProvider(DbContextOptionsBuilder options, string connectionString, string schema)
///     {
///         options.UseNpgsql(connectionString, npgsql => npgsql.MigrationsHistoryTable("__EFMigrationsHistory", schema));
///     }
/// }
/// </code>
/// </remarks>
public abstract class DesignTimeControlDbContextFactoryBase : IDesignTimeDbContextFactory<ControlDbContext>
{
    /// <summary>
    /// The default schema name for the control database.
    /// </summary>
    protected virtual string SchemaName => "tenant_control";

    /// <inheritdoc />
    public ControlDbContext CreateDbContext(string[] args)
    {
        var optionsBuilder = new DbContextOptionsBuilder<ControlDbContext>();

        // Get connection string from environment variable for design-time operations
        var connectionString = Environment.GetEnvironmentVariable("ConnectionStrings__ControlDatabase")
            ?? Environment.GetEnvironmentVariable("ConnectionStrings__DefaultConnection")
            ?? throw new InvalidOperationException(
                "Set ConnectionStrings__ControlDatabase or ConnectionStrings__DefaultConnection environment variable for EF Core migrations");

        ConfigureProvider(optionsBuilder, connectionString, SchemaName);

        return new ControlDbContext(optionsBuilder.Options, SchemaName);
    }

    /// <summary>
    /// Configures the database provider. Override this method to call the appropriate UseXxx method.
    /// </summary>
    /// <param name="options">The options builder to configure.</param>
    /// <param name="connectionString">The connection string.</param>
    /// <param name="schema">The schema name for the migrations history table.</param>
    protected abstract void ConfigureProvider(DbContextOptionsBuilder options, string connectionString, string schema);
}
