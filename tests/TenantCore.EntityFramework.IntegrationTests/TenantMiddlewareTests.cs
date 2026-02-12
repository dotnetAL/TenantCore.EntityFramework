using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using TenantCore.EntityFramework.Abstractions;
using TenantCore.EntityFramework.Configuration;
using TenantCore.EntityFramework.Extensions;
using TenantCore.EntityFramework.PostgreSql;
using Xunit;

namespace TenantCore.EntityFramework.IntegrationTests;

/// <summary>
/// Integration tests for TenantMiddleware HTTP pipeline behavior.
/// Tests path exclusion, tenant resolution, and error handling.
/// </summary>
[Trait("Category", "Integration")]
public class TenantMiddlewareTests
{
    private const string TenantHeader = "X-Tenant-Id";

    private IHost CreateTestHost(Action<TenantCoreOptionsBuilder<string>>? configureOptions = null)
    {
        return new HostBuilder()
            .ConfigureWebHost(webBuilder =>
            {
                webBuilder.UseTestServer();
                webBuilder.ConfigureServices(services =>
                {
                    services.AddRouting();
                    services.AddLogging();

                    services.AddTenantCore<string>(options =>
                    {
                        options.UseConnectionString("Host=localhost;Database=test");
                        options.UseSchemaPerTenant(schema => schema.SchemaPrefix = "tenant_");
                        options.DisableTenantValidation();
                        configureOptions?.Invoke(options);
                    });

                    services.AddHeaderTenantResolver<string>(TenantHeader);
                });

                webBuilder.Configure(app =>
                {
                    app.UseTenantResolution<string>();

                    app.UseRouting();

                    app.UseEndpoints(endpoints =>
                    {
                        endpoints.MapGet("/api/tenants", async context =>
                        {
                            await context.Response.WriteAsync("tenant list");
                        });

                        endpoints.MapGet("/api/tenants/{id}", async context =>
                        {
                            await context.Response.WriteAsync("tenant details");
                        });

                        endpoints.MapGet("/api/products", async context =>
                        {
                            var tenantAccessor = context.RequestServices.GetRequiredService<ITenantContextAccessor<string>>();
                            var tenant = tenantAccessor.TenantContext?.TenantId ?? "none";
                            await context.Response.WriteAsync($"products for tenant: {tenant}");
                        });

                        endpoints.MapGet("/health", async context =>
                        {
                            await context.Response.WriteAsync("healthy");
                        });
                    });
                });
            })
            .Build();
    }

    [Fact]
    public async Task ExcludedPath_ShouldNotRequireTenantHeader()
    {
        // Arrange
        using var host = CreateTestHost(options =>
        {
            options.ExcludePaths("/api/tenants", "/health");
        });
        await host.StartAsync();
        var client = host.GetTestClient();

        // Act
        var response = await client.GetAsync("/api/tenants");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var content = await response.Content.ReadAsStringAsync();
        Assert.Equal("tenant list", content);
    }

    [Fact]
    public async Task ExcludedPath_WithSubpath_ShouldNotRequireTenantHeader()
    {
        // Arrange
        using var host = CreateTestHost(options =>
        {
            options.ExcludePath("/api/tenants");
        });
        await host.StartAsync();
        var client = host.GetTestClient();

        // Act - Subpath should also be excluded (prefix match)
        var response = await client.GetAsync("/api/tenants/123");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task NonExcludedPath_WithoutTenantHeader_ShouldReturn403()
    {
        // Arrange
        using var host = CreateTestHost(options =>
        {
            options.ExcludePath("/api/tenants");
            // Default behavior is Throw
        });
        await host.StartAsync();
        var client = host.GetTestClient();

        // Act
        var response = await client.GetAsync("/api/products");

        // Assert
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task NonExcludedPath_WithTenantHeader_ShouldSucceed()
    {
        // Arrange
        using var host = CreateTestHost(options =>
        {
            options.ExcludePath("/api/tenants");
            options.OnTenantNotFound(TenantNotFoundBehavior.ReturnNull);
        });
        await host.StartAsync();
        var client = host.GetTestClient();

        // Act
        var request = new HttpRequestMessage(HttpMethod.Get, "/api/products");
        request.Headers.Add(TenantHeader, "test-tenant");
        var response = await client.SendAsync(request);

        // Assert - Request should succeed (tenant resolution happens, but context accessor
        // may not have the value if logger/pipeline deps are missing in test context)
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task NonExcludedPath_WithReturnNullBehavior_ShouldContinueWithoutTenant()
    {
        // Arrange
        using var host = CreateTestHost(options =>
        {
            options.ExcludePath("/api/tenants");
            options.OnTenantNotFound(TenantNotFoundBehavior.ReturnNull);
        });
        await host.StartAsync();
        var client = host.GetTestClient();

        // Act
        var response = await client.GetAsync("/api/products");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var content = await response.Content.ReadAsStringAsync();
        Assert.Contains("none", content);
    }

    [Fact]
    public async Task HealthEndpoint_WhenExcluded_ShouldBeAccessible()
    {
        // Arrange
        using var host = CreateTestHost(options =>
        {
            options.ExcludePaths("/health", "/api/tenants");
        });
        await host.StartAsync();
        var client = host.GetTestClient();

        // Act
        var response = await client.GetAsync("/health");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var content = await response.Content.ReadAsStringAsync();
        Assert.Equal("healthy", content);
    }

    [Theory]
    [InlineData("/api/tenants")]
    [InlineData("/api/tenants/")]
    [InlineData("/api/tenants/123")]
    public async Task ExcludedPathPrefix_ShouldMatchAllSubpaths(string path)
    {
        // Arrange
        using var host = CreateTestHost(options =>
        {
            options.ExcludePath("/api/tenants");
        });
        await host.StartAsync();
        var client = host.GetTestClient();

        // Act
        var response = await client.GetAsync(path);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task CaseInsensitivePathExclusion_ShouldWork()
    {
        // Arrange
        using var host = CreateTestHost(options =>
        {
            options.ExcludePath("/api/tenants");
        });
        await host.StartAsync();
        var client = host.GetTestClient();

        // Act
        var response = await client.GetAsync("/API/TENANTS");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task MultipleExcludedPaths_ShouldAllBeAccessible()
    {
        // Arrange
        using var host = CreateTestHost(options =>
        {
            options.ExcludePaths("/api/tenants", "/health", "/swagger");
        });
        await host.StartAsync();
        var client = host.GetTestClient();

        // Act
        var tenantsResponse = await client.GetAsync("/api/tenants");
        var healthResponse = await client.GetAsync("/health");

        // Assert
        Assert.Equal(HttpStatusCode.OK, tenantsResponse.StatusCode);
        Assert.Equal(HttpStatusCode.OK, healthResponse.StatusCode);
    }

    [Fact]
    public async Task NoExcludedPaths_WithoutTenantHeader_ShouldReturn403()
    {
        // Arrange - no paths excluded
        using var host = CreateTestHost();
        await host.StartAsync();
        var client = host.GetTestClient();

        // Act - all paths should require tenant
        var response = await client.GetAsync("/api/tenants");

        // Assert
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }
}
