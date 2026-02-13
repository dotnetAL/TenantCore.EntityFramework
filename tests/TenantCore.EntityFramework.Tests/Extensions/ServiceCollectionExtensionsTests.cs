using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using TenantCore.EntityFramework.Abstractions;
using TenantCore.EntityFramework.Context;
using TenantCore.EntityFramework.ControlDb;
using TenantCore.EntityFramework.Extensions;
using Xunit;

namespace TenantCore.EntityFramework.Tests.Extensions;

public class ServiceCollectionExtensionsTests
{
    [Fact]
    public void AddTenantCore_WithoutHttpContextAccessor_ShouldUseTenantContextAccessor()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act
        services.AddTenantCore<string>(options =>
        {
            options.UseSchemaPerTenant();
        });

        var provider = services.BuildServiceProvider();
        var accessor = provider.GetRequiredService<ITenantContextAccessor<string>>();

        // Assert
        accessor.Should().BeOfType<TenantContextAccessor<string>>();
    }

    [Fact]
    public void AddTenantCore_WithHttpContextAccessor_ShouldUseHttpContextTenantContextAccessor()
    {
        // Arrange
        var services = new ServiceCollection();

        // Register IHttpContextAccessor first (simulating ASP.NET Core)
        services.AddHttpContextAccessor();

        // Act
        services.AddTenantCore<string>(options =>
        {
            options.UseSchemaPerTenant();
        });

        var provider = services.BuildServiceProvider();
        var accessor = provider.GetRequiredService<ITenantContextAccessor<string>>();

        // Assert
        accessor.Should().BeOfType<HttpContextTenantContextAccessor<string>>();
    }

    [Fact]
    public void AddTenantCore_WithHttpContextAccessorAddedAfter_ShouldUseHttpContextTenantContextAccessor()
    {
        // Arrange
        var services = new ServiceCollection();

        // Add TenantCore first
        services.AddTenantCore<string>(options =>
        {
            options.UseSchemaPerTenant();
        });

        // Then add IHttpContextAccessor (registration order shouldn't matter)
        services.AddHttpContextAccessor();

        // Act
        var provider = services.BuildServiceProvider();
        var accessor = provider.GetRequiredService<ITenantContextAccessor<string>>();

        // Assert - should detect IHttpContextAccessor at resolution time
        accessor.Should().BeOfType<HttpContextTenantContextAccessor<string>>();
    }

    [Fact]
    public void AddHeaderTenantResolver_ShouldRegisterHttpContextAccessor()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddTenantCore<string>(options =>
        {
            options.UseSchemaPerTenant();
        });

        // Act
        services.AddHeaderTenantResolver<string>();

        var provider = services.BuildServiceProvider();
        var httpContextAccessor = provider.GetService<IHttpContextAccessor>();
        var tenantContextAccessor = provider.GetRequiredService<ITenantContextAccessor<string>>();

        // Assert
        httpContextAccessor.Should().NotBeNull();
        tenantContextAccessor.Should().BeOfType<HttpContextTenantContextAccessor<string>>();
    }

    [Fact]
    public void AddTenantCore_WithGuidKey_ShouldAutoDetectHttpContext()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddHttpContextAccessor();

        // Act
        services.AddTenantCore<Guid>(options =>
        {
            options.UseSchemaPerTenant();
        });

        var provider = services.BuildServiceProvider();
        var accessor = provider.GetRequiredService<ITenantContextAccessor<Guid>>();

        // Assert
        accessor.Should().BeOfType<HttpContextTenantContextAccessor<Guid>>();
    }

    [Fact]
    public void AddTenantCore_WithIntKey_ShouldAutoDetectHttpContext()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddHttpContextAccessor();

        // Act
        services.AddTenantCore<int>(options =>
        {
            options.UseSchemaPerTenant();
        });

        var provider = services.BuildServiceProvider();
        var accessor = provider.GetRequiredService<ITenantContextAccessor<int>>();

        // Assert
        accessor.Should().BeOfType<HttpContextTenantContextAccessor<int>>();
    }

    [Fact]
    public void TenantContextAccessor_ShouldBeSingleton()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddTenantCore<string>(options =>
        {
            options.UseSchemaPerTenant();
        });

        var provider = services.BuildServiceProvider();

        // Act
        var accessor1 = provider.GetRequiredService<ITenantContextAccessor<string>>();
        var accessor2 = provider.GetRequiredService<ITenantContextAccessor<string>>();

        // Assert
        accessor1.Should().BeSameAs(accessor2);
    }

    [Fact]
    public void AddTenantCore_RegistersCurrentTenantRecordAccessor()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddTenantCore<string>(options =>
        {
            options.UseSchemaPerTenant();
        });

        var provider = services.BuildServiceProvider();

        // Act
        using var scope = provider.CreateScope();
        var accessor = scope.ServiceProvider.GetService<ICurrentTenantRecordAccessor>();

        // Assert
        accessor.Should().NotBeNull();
        accessor.Should().BeOfType<CurrentTenantRecordAccessor<string>>();
    }

    [Fact]
    public void HttpContextTenantContextAccessor_ShouldBeSingleton()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddHttpContextAccessor();
        services.AddTenantCore<string>(options =>
        {
            options.UseSchemaPerTenant();
        });

        var provider = services.BuildServiceProvider();

        // Act
        var accessor1 = provider.GetRequiredService<ITenantContextAccessor<string>>();
        var accessor2 = provider.GetRequiredService<ITenantContextAccessor<string>>();

        // Assert
        accessor1.Should().BeSameAs(accessor2);
    }
}
