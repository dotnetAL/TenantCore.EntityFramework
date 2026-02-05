using Microsoft.EntityFrameworkCore;
using TenantCore.EntityFramework.Abstractions;
using TenantCore.EntityFramework.Configuration;
using TenantCore.EntityFramework.Context;
using Xunit;

namespace TenantCore.EntityFramework.Tests.Context;

public class TenantDbContextModelTests
{
    [Fact]
    public void Model_WhenTenantContextSet_ShouldHaveCorrectDefaultSchema()
    {
        // Arrange
        var accessor = new TestTenantContextAccessor();
        accessor.SetTenantContext(new TenantContext<string>("test123", "tenant_test123"));
        var tenantOptions = new TenantCoreOptions();

        var options = new DbContextOptionsBuilder<TestModelContext>()
            .UseInMemoryDatabase("test_model_" + Guid.NewGuid())
            .Options;

        // Act
        using var context = new TestModelContext(options, accessor, tenantOptions);
        var model = context.Model;

        // Assert
        Assert.Equal("tenant_test123", model.GetDefaultSchema());
    }

    [Fact]
    public void Model_WhenNoTenantContext_ShouldHaveNoDefaultSchema()
    {
        // Arrange
        var accessor = new TestTenantContextAccessor();
        // Don't set tenant context
        var tenantOptions = new TenantCoreOptions();

        var options = new DbContextOptionsBuilder<TestModelContext>()
            .UseInMemoryDatabase("test_model_no_tenant_" + Guid.NewGuid())
            .ReplaceService<Microsoft.EntityFrameworkCore.Infrastructure.IModelCacheKeyFactory, TenantModelCacheKeyFactory<string>>()
            .Options;

        // Act
        using var context = new TestModelContext(options, accessor, tenantOptions);
        var model = context.Model;

        // Assert - Schema should be null or "public" when no tenant
        Assert.True(model.GetDefaultSchema() == null || model.GetDefaultSchema() == "public",
            $"Expected null or 'public' schema but got '{model.GetDefaultSchema()}'");
    }

    [Fact]
    public void Model_EntityTable_ShouldUseDefaultSchema()
    {
        // Arrange
        var accessor = new TestTenantContextAccessor();
        accessor.SetTenantContext(new TenantContext<string>("test123", "tenant_test123"));
        var tenantOptions = new TenantCoreOptions();

        var options = new DbContextOptionsBuilder<TestModelContext>()
            .UseInMemoryDatabase("test_model_" + Guid.NewGuid())
            .Options;

        // Act
        using var context = new TestModelContext(options, accessor, tenantOptions);
        var entityType = context.Model.FindEntityType(typeof(TestProduct));

        // Assert
        Assert.NotNull(entityType);
        Assert.Equal("tenant_test123", entityType.GetSchema());
        // Table name might be singular or plural depending on conventions
        Assert.NotNull(entityType.GetTableName());
    }

    [Fact]
    public void Model_DifferentTenants_ShouldHaveDifferentSchemas()
    {
        // Arrange
        var accessor1 = new TestTenantContextAccessor();
        accessor1.SetTenantContext(new TenantContext<string>("tenant1", "tenant_tenant1"));
        var tenantOptions = new TenantCoreOptions();

        var accessor2 = new TestTenantContextAccessor();
        accessor2.SetTenantContext(new TenantContext<string>("tenant2", "tenant_tenant2"));

        var options1 = new DbContextOptionsBuilder<TestModelContext>()
            .UseInMemoryDatabase("test_model_t1_" + Guid.NewGuid())
            .ReplaceService<Microsoft.EntityFrameworkCore.Infrastructure.IModelCacheKeyFactory, TenantModelCacheKeyFactory<string>>()
            .Options;

        var options2 = new DbContextOptionsBuilder<TestModelContext>()
            .UseInMemoryDatabase("test_model_t2_" + Guid.NewGuid())
            .ReplaceService<Microsoft.EntityFrameworkCore.Infrastructure.IModelCacheKeyFactory, TenantModelCacheKeyFactory<string>>()
            .Options;

        // Act
        using var context1 = new TestModelContext(options1, accessor1, tenantOptions);
        using var context2 = new TestModelContext(options2, accessor2, tenantOptions);

        // Assert
        Assert.Equal("tenant_tenant1", context1.Model.GetDefaultSchema());
        Assert.Equal("tenant_tenant2", context2.Model.GetDefaultSchema());
    }

    private class TestTenantContextAccessor : ITenantContextAccessor<string>
    {
        private TenantContext<string>? _context;

        public TenantContext<string>? TenantContext => _context;

        public void SetTenantContext(TenantContext<string>? context)
        {
            _context = context;
        }
    }

    private class TestModelContext : TenantDbContext<string>
    {
        public DbSet<TestProduct> TestProducts => Set<TestProduct>();

        public TestModelContext(
            DbContextOptions<TestModelContext> options,
            ITenantContextAccessor<string> accessor,
            TenantCoreOptions tenantOptions)
            : base(options, accessor, tenantOptions)
        {
        }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);
            modelBuilder.Entity<TestProduct>();
        }
    }

    private class TestProduct
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
    }
}
