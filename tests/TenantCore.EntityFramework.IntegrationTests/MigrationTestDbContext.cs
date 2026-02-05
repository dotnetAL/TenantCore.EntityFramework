using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TenantCore.EntityFramework.Abstractions;
using TenantCore.EntityFramework.Configuration;
using TenantCore.EntityFramework.Context;

namespace TenantCore.EntityFramework.IntegrationTests;

/// <summary>
/// A DbContext specifically designed for migration testing.
/// This context has EF Core migrations that can be applied to tenant schemas.
/// </summary>
public class MigrationTestDbContext : TenantDbContext<string>
{
    public DbSet<Product> Products => Set<Product>();
    public DbSet<Category> Categories => Set<Category>();

    public MigrationTestDbContext(DbContextOptions<MigrationTestDbContext> options) : base(options)
    {
    }

    [ActivatorUtilitiesConstructor]
    public MigrationTestDbContext(
        DbContextOptions<MigrationTestDbContext> options,
        ITenantContextAccessor<string> tenantContextAccessor,
        TenantCoreOptions tenantOptions)
        : base(options, tenantContextAccessor, tenantOptions)
    {
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<Product>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Name).HasMaxLength(200).IsRequired();
            entity.Property(e => e.Price).HasPrecision(18, 2);
            entity.Property(e => e.Sku).HasMaxLength(50);
            entity.HasOne(e => e.Category)
                .WithMany(c => c.Products)
                .HasForeignKey(e => e.CategoryId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<Category>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Name).HasMaxLength(100).IsRequired();
            entity.Property(e => e.Description).HasMaxLength(500);
        });
    }
}

public class Product
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public decimal Price { get; set; }
    public string? Sku { get; set; }
    public int? CategoryId { get; set; }
    public Category? Category { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public class Category
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public ICollection<Product> Products { get; set; } = new List<Product>();
}
