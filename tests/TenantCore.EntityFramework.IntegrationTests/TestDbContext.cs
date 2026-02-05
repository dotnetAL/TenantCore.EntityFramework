using Microsoft.EntityFrameworkCore;
using TenantCore.EntityFramework.Abstractions;
using TenantCore.EntityFramework.Configuration;
using TenantCore.EntityFramework.Context;

namespace TenantCore.EntityFramework.IntegrationTests;

public class TestDbContext : TenantDbContext<string>
{
    public DbSet<TestEntity> TestEntities => Set<TestEntity>();

    public TestDbContext(DbContextOptions<TestDbContext> options) : base(options)
    {
    }

    public TestDbContext(
        DbContextOptions<TestDbContext> options,
        ITenantContextAccessor<string> tenantContextAccessor,
        TenantCoreOptions tenantOptions)
        : base(options, tenantContextAccessor, tenantOptions)
    {
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<TestEntity>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Name).HasMaxLength(200).IsRequired();
        });
    }
}

public class TestEntity
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
