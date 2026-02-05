using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace TenantCore.EntityFramework.IntegrationTests;

/// <summary>
/// Design-time factory for TestDbContext migrations.
/// Used by 'dotnet ef migrations' commands.
/// </summary>
public class TestDbContextFactory : IDesignTimeDbContextFactory<TestDbContext>
{
    public TestDbContext CreateDbContext(string[] args)
    {
        var optionsBuilder = new DbContextOptionsBuilder<TestDbContext>();

        // Use a placeholder connection string for design-time
        // Actual connection will be provided at runtime
        optionsBuilder.UseNpgsql("Host=localhost;Database=design_time;");

        return new TestDbContext(optionsBuilder.Options);
    }
}

/// <summary>
/// Design-time factory for MigrationTestDbContext migrations.
/// Used by 'dotnet ef migrations' commands.
/// </summary>
public class MigrationTestDbContextFactory : IDesignTimeDbContextFactory<MigrationTestDbContext>
{
    public MigrationTestDbContext CreateDbContext(string[] args)
    {
        var optionsBuilder = new DbContextOptionsBuilder<MigrationTestDbContext>();

        // Use a placeholder connection string for design-time
        // Actual connection will be provided at runtime
        optionsBuilder.UseNpgsql("Host=localhost;Database=design_time;");

        return new MigrationTestDbContext(optionsBuilder.Options);
    }
}
