using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using TenantCore.EntityFramework.Abstractions;
using TenantCore.EntityFramework.Configuration;
using TenantCore.EntityFramework.Context;

namespace TenantCore.Sample.WebApi;

/// <summary>
/// Design-time factory for creating ApplicationDbContext for EF Core migrations.
/// </summary>
public class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<ApplicationDbContext>
{
    public ApplicationDbContext CreateDbContext(string[] args)
    {
        var optionsBuilder = new DbContextOptionsBuilder<ApplicationDbContext>();

        // Get connection string from environment variable for design-time operations
        //var connectionString = Environment.GetEnvironmentVariable("ConnectionStrings__DefaultConnection")
        // 
        //?? throw new InvalidOperationException(
        //       "Set ConnectionStrings__DefaultConnection environment variable for EF Core migrations");

        optionsBuilder.UseNpgsql();

        // Create a mock tenant context accessor for design time
        var tenantAccessor = new DesignTimeTenantContextAccessor();
        var tenantOptions = new TenantCoreOptions();

        return new ApplicationDbContext(optionsBuilder.Options, tenantAccessor, tenantOptions);
    }

    private class DesignTimeTenantContextAccessor : ITenantContextAccessor<string>
    {
        public TenantContext<string>? TenantContext => null;

        public void SetTenantContext(TenantContext<string>? context) { }
    }
}

/// <summary>
/// Design-time factory for creating InventoryDbContext for EF Core migrations.
/// </summary>
public class DesignTimeInventoryDbContextFactory : IDesignTimeDbContextFactory<InventoryDbContext>
{
    public InventoryDbContext CreateDbContext(string[] args)
    {
        var optionsBuilder = new DbContextOptionsBuilder<InventoryDbContext>();

        optionsBuilder.UseNpgsql();

        var tenantAccessor = new DesignTimeTenantContextAccessor();
        var tenantOptions = new TenantCoreOptions();

        return new InventoryDbContext(optionsBuilder.Options, tenantAccessor, tenantOptions);
    }

    private class DesignTimeTenantContextAccessor : ITenantContextAccessor<string>
    {
        public TenantContext<string>? TenantContext => null;

        public void SetTenantContext(TenantContext<string>? context) { }
    }
}
