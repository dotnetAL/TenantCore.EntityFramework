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

        // Use a placeholder connection string for design-time operations
        optionsBuilder.UseNpgsql("Host=localhost;Database=tenantcore_sample;Username=postgres;Password=postgres");

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
