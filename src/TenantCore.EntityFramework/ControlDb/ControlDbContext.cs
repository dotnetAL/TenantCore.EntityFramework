using Microsoft.EntityFrameworkCore;
using TenantCore.EntityFramework.ControlDb.Entities;

namespace TenantCore.EntityFramework.ControlDb;

/// <summary>
/// DbContext for the tenant control database.
/// </summary>
public class ControlDbContext : DbContext
{
    private readonly string _schema;

    /// <summary>
    /// Gets or sets the tenants DbSet.
    /// </summary>
    public DbSet<TenantEntity> Tenants => Set<TenantEntity>();

    /// <summary>
    /// Initializes a new instance of the <see cref="ControlDbContext"/> class.
    /// </summary>
    /// <param name="options">The DbContext options.</param>
    public ControlDbContext(DbContextOptions<ControlDbContext> options)
        : this(options, "tenant_control")
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="ControlDbContext"/> class with a custom schema.
    /// </summary>
    /// <param name="options">The DbContext options.</param>
    /// <param name="schema">The schema name for the control database tables.</param>
    public ControlDbContext(DbContextOptions<ControlDbContext> options, string schema)
        : base(options)
    {
        _schema = schema;
    }

    /// <inheritdoc />
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.HasDefaultSchema(_schema);

        modelBuilder.Entity<TenantEntity>(entity =>
        {
            entity.ToTable("Tenants");

            entity.HasKey(e => e.TenantId);

            entity.Property(e => e.TenantId)
                .ValueGeneratedNever();

            entity.Property(e => e.TenantSlug)
                .IsRequired()
                .HasMaxLength(100);

            entity.Property(e => e.Status)
                .IsRequired();

            entity.Property(e => e.TenantSchema)
                .IsRequired()
                .HasMaxLength(63); // PostgreSQL max identifier length

            entity.Property(e => e.TenantDatabase)
                .HasMaxLength(100);

            entity.Property(e => e.TenantDbServer)
                .HasMaxLength(255);

            entity.Property(e => e.TenantDbUser)
                .HasMaxLength(100);

            entity.Property(e => e.TenantDbPasswordEncrypted)
                .HasMaxLength(1024);

            entity.Property(e => e.TenantApiKeyHash)
                .HasMaxLength(64)
                .IsFixedLength();

            entity.Property(e => e.CreatedAt)
                .IsRequired();

            entity.Property(e => e.UpdatedAt)
                .IsRequired();

            // Unique indexes
            entity.HasIndex(e => e.TenantSlug)
                .IsUnique();

            entity.HasIndex(e => e.TenantSchema)
                .IsUnique();

            // Non-unique index for API key lookup
            entity.HasIndex(e => e.TenantApiKeyHash);

            // Index on status for filtering
            entity.HasIndex(e => e.Status);
        });
    }
}
