using Microsoft.EntityFrameworkCore;
using TenantCore.EntityFramework.ControlDb;

namespace TenantCore.EntityFramework.PostgreSql.ControlDb;

/// <summary>
/// PostgreSQL-specific design-time factory for creating ControlDbContext for EF Core migrations.
/// </summary>
public class DesignTimeControlDbContextFactory : DesignTimeControlDbContextFactoryBase
{
    /// <inheritdoc />
    protected override void ConfigureProvider(DbContextOptionsBuilder options, string connectionString, string schema)
    {
        options.UseNpgsql(connectionString, npgsql =>
        {
            npgsql.MigrationsHistoryTable("__EFMigrationsHistory", schema);
            npgsql.MigrationsAssembly("TenantCore.EntityFramework.PostgreSql");
        });
    }
}
