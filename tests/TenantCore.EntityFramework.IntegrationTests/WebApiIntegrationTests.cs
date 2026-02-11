using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.EntityFrameworkCore;
using Xunit;
using TenantCore.Sample.WebApi;

namespace TenantCore.EntityFramework.IntegrationTests;

/// <summary>
/// End-to-end integration tests that make real HTTP requests to the Sample WebAPI.
/// These tests verify tenant isolation through the actual HTTP pipeline, including:
/// - Middleware tenant resolution
/// - Connection pooling behavior
/// - Schema isolation for CRUD operations
///
/// This is the definitive test of whether multi-tenancy actually works.
/// </summary>
[Collection("PostgreSql")]
[Trait("Category", "Integration")]
[Trait("Category", "E2E")]
public class WebApiIntegrationTests : IAsyncLifetime
{
    private readonly PostgreSqlFixture _fixture;
    private WebApplicationFactory<Program> _factory = null!;
    private HttpClient _client = null!;
    private readonly List<string> _createdTenants = new();

    public WebApiIntegrationTests(PostgreSqlFixture fixture)
    {
        _fixture = fixture;
    }

    public async Task InitializeAsync()
    {
        // Set environment variable BEFORE creating the factory
        // This ensures the Sample reads the test connection string
        Environment.SetEnvironmentVariable("ConnectionStrings__DefaultConnection", _fixture.ConnectionString);
        Environment.SetEnvironmentVariable("ConnectionStrings__ControlDatabase", _fixture.ConnectionString);
        Environment.SetEnvironmentVariable("TenantCore__UseControlDatabase", "false");

        _factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.ConfigureAppConfiguration((context, config) =>
                {
                    // Clear existing sources and add environment variables with highest priority
                    config.AddEnvironmentVariables();

                    // Also add in-memory collection as fallback
                    config.AddInMemoryCollection(new Dictionary<string, string?>
                    {
                        ["ConnectionStrings:DefaultConnection"] = _fixture.ConnectionString,
                        ["ConnectionStrings:ControlDatabase"] = _fixture.ConnectionString,
                        ["TenantCore:UseControlDatabase"] = "false",
                        ["TenantCore:Migrations:ApplyOnStartup"] = "false"
                    });
                });

                builder.ConfigureServices(services =>
                {
                    // Remove hosted migration services to avoid discovering foreign
                    // tenant schemas from other test classes sharing the same database
                    services.RemoveAll<IHostedService>();
                });
            });

        _client = _factory.CreateClient();
        await Task.CompletedTask;
    }

    public async Task DisposeAsync()
    {
        // Clean up all created tenants
        foreach (var tenantId in _createdTenants)
        {
            try
            {
                await _client.DeleteAsync($"/api/tenants/{tenantId}?hardDelete=true");
            }
            catch
            {
                // Ignore cleanup errors
            }
        }

        _client.Dispose();
        await _factory.DisposeAsync();
    }

    private async Task<bool> CreateTenantAsync(string tenantId)
    {
        var response = await _client.PostAsync($"/api/tenants/{tenantId}", null);
        if (response.IsSuccessStatusCode)
        {
            _createdTenants.Add(tenantId);
            return true;
        }
        return response.StatusCode == HttpStatusCode.Conflict; // Already exists
    }

    private async Task<HttpResponseMessage> CreateProductAsync(string tenantId, string name, decimal price)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/products");
        request.Headers.Add("X-Tenant-Id", tenantId);
        request.Content = JsonContent.Create(new { name, description = $"Product for {tenantId}", price });
        return await _client.SendAsync(request);
    }

    private async Task<List<ProductResponse>> GetProductsAsync(string tenantId)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "/api/products");
        request.Headers.Add("X-Tenant-Id", tenantId);
        var response = await _client.SendAsync(request);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<List<ProductResponse>>() ?? new();
    }

    /// <summary>
    /// THE FUNDAMENTAL TEST: Create 10 tenants, add products to only 3 of them,
    /// verify via HTTP requests that only those 3 tenants see products.
    /// All other tenants must return empty results.
    /// </summary>
    [Fact]
    public async Task TenTenants_ProductsInThree_OthersShouldBeEmpty_ViaHttp()
    {
        // Arrange - Create 10 unique tenant IDs
        var testRunId = Guid.NewGuid().ToString("N")[..8];
        var tenants = Enumerable.Range(1, 10)
            .Select(i => $"http{i}_{testRunId}")
            .ToList();

        // Tenants that will have products (5, 6, 7 - indices 4, 5, 6)
        var tenantsWithProducts = new[] { tenants[4], tenants[5], tenants[6] };
        var tenantsWithoutProducts = tenants.Except(tenantsWithProducts).ToList();

        // ============================================================
        // STEP 1: Create all 10 tenants via HTTP POST
        // ============================================================
        foreach (var tenant in tenants)
        {
            var created = await CreateTenantAsync(tenant);
            Assert.True(created, $"Failed to create tenant {tenant}");
        }

        // Small delay to ensure migrations complete
        await Task.Delay(500);

        // ============================================================
        // STEP 2: Add products ONLY to tenants 5, 6, 7 via HTTP POST
        // ============================================================

        // Add 3 products to tenant 5
        for (int i = 1; i <= 3; i++)
        {
            var response = await CreateProductAsync(tenants[4], $"Tenant5_Product_{i}", 10.00m + i);
            Assert.True(response.IsSuccessStatusCode,
                $"Failed to create product in tenant 5: {response.StatusCode} - {await response.Content.ReadAsStringAsync()}");
        }

        // Add 5 products to tenant 6
        for (int i = 1; i <= 5; i++)
        {
            var response = await CreateProductAsync(tenants[5], $"Tenant6_Product_{i}", 20.00m + i);
            Assert.True(response.IsSuccessStatusCode,
                $"Failed to create product in tenant 6: {response.StatusCode} - {await response.Content.ReadAsStringAsync()}");
        }

        // Add 2 products to tenant 7
        for (int i = 1; i <= 2; i++)
        {
            var response = await CreateProductAsync(tenants[6], $"Tenant7_Product_{i}", 30.00m + i);
            Assert.True(response.IsSuccessStatusCode,
                $"Failed to create product in tenant 7: {response.StatusCode} - {await response.Content.ReadAsStringAsync()}");
        }

        // ============================================================
        // STEP 3: Verify tenants 5, 6, 7 see their products via HTTP GET
        // ============================================================

        // Tenant 5 should have exactly 3 products
        var tenant5Products = await GetProductsAsync(tenants[4]);
        Assert.Equal(3, tenant5Products.Count);
        Assert.All(tenant5Products, p => Assert.StartsWith("Tenant5_", p.Name));

        // Tenant 6 should have exactly 5 products
        var tenant6Products = await GetProductsAsync(tenants[5]);
        Assert.Equal(5, tenant6Products.Count);
        Assert.All(tenant6Products, p => Assert.StartsWith("Tenant6_", p.Name));

        // Tenant 7 should have exactly 2 products
        var tenant7Products = await GetProductsAsync(tenants[6]);
        Assert.Equal(2, tenant7Products.Count);
        Assert.All(tenant7Products, p => Assert.StartsWith("Tenant7_", p.Name));

        // ============================================================
        // STEP 4: Verify ALL OTHER tenants (1,2,3,4,8,9,10) return EMPTY
        // ============================================================

        foreach (var tenant in tenantsWithoutProducts)
        {
            var products = await GetProductsAsync(tenant);
            Assert.Empty(products);
        }
    }

    /// <summary>
    /// Tests rapid HTTP requests alternating between tenants to catch
    /// connection pool reuse issues.
    /// </summary>
    [Fact]
    public async Task RapidHttpRequests_AlternatingTenants_ShouldMaintainIsolation()
    {
        // Arrange
        var testRunId = Guid.NewGuid().ToString("N")[..8];
        var tenant1 = $"rapid1_{testRunId}";
        var tenant2 = $"rapid2_{testRunId}";

        await CreateTenantAsync(tenant1);
        await CreateTenantAsync(tenant2);
        await Task.Delay(300);

        // Add distinct product to each tenant
        var response1 = await CreateProductAsync(tenant1, "TENANT1_ONLY_PRODUCT", 100m);
        Assert.True(response1.IsSuccessStatusCode,
            $"Failed to create product in tenant1: {response1.StatusCode} - {await response1.Content.ReadAsStringAsync()}");

        var response2 = await CreateProductAsync(tenant2, "TENANT2_ONLY_PRODUCT", 200m);
        Assert.True(response2.IsSuccessStatusCode,
            $"Failed to create product in tenant2: {response2.StatusCode} - {await response2.Content.ReadAsStringAsync()}");

        // Act - Rapidly alternate between tenants 50 times
        var errors = new List<string>();

        for (int i = 0; i < 50; i++)
        {
            // Query tenant 1
            var t1Products = await GetProductsAsync(tenant1);
            if (t1Products.Count != 1)
                errors.Add($"Iteration {i}: Tenant1 expected 1 product, got {t1Products.Count}");
            else if (t1Products[0].Name != "TENANT1_ONLY_PRODUCT")
                errors.Add($"Iteration {i}: Tenant1 got wrong product: {t1Products[0].Name}");

            // Query tenant 2 immediately after
            var t2Products = await GetProductsAsync(tenant2);
            if (t2Products.Count != 1)
                errors.Add($"Iteration {i}: Tenant2 expected 1 product, got {t2Products.Count}");
            else if (t2Products[0].Name != "TENANT2_ONLY_PRODUCT")
                errors.Add($"Iteration {i}: Tenant2 got wrong product: {t2Products[0].Name}");
        }

        // Assert - No errors means isolation held across all iterations
        Assert.Empty(errors);
    }

    /// <summary>
    /// Tests parallel HTTP requests from different tenants simultaneously.
    /// This simulates real-world load where multiple tenants hit the API concurrently.
    /// </summary>
    [Fact]
    public async Task ParallelHttpRequests_DifferentTenants_ShouldMaintainIsolation()
    {
        // Arrange
        var testRunId = Guid.NewGuid().ToString("N")[..8];
        var tenants = Enumerable.Range(1, 5)
            .Select(i => $"parallel{i}_{testRunId}")
            .ToList();

        // Create all tenants
        foreach (var tenant in tenants)
        {
            await CreateTenantAsync(tenant);
        }
        await Task.Delay(500);

        // Add unique product to each tenant
        foreach (var tenant in tenants)
        {
            var response = await CreateProductAsync(tenant, $"PRODUCT_FOR_{tenant}", 50m);
            Assert.True(response.IsSuccessStatusCode);
        }

        // Act - Query all tenants in parallel
        var tasks = tenants.Select(async tenant =>
        {
            var products = await GetProductsAsync(tenant);
            return (Tenant: tenant, Products: products);
        });

        var results = await Task.WhenAll(tasks);

        // Assert - Each tenant should only see their own product
        foreach (var result in results)
        {
            Assert.Single(result.Products);
            Assert.Equal($"PRODUCT_FOR_{result.Tenant}", result.Products[0].Name);
        }
    }

    /// <summary>
    /// Tests that creating, updating, and deleting products affects only the correct tenant.
    /// </summary>
    [Fact]
    public async Task CrudOperations_ShouldOnlyAffectCurrentTenant()
    {
        // Arrange
        var testRunId = Guid.NewGuid().ToString("N")[..8];
        var tenant1 = $"crud1_{testRunId}";
        var tenant2 = $"crud2_{testRunId}";

        await CreateTenantAsync(tenant1);
        await CreateTenantAsync(tenant2);
        await Task.Delay(300);

        // Add same-named product to both tenants
        await CreateProductAsync(tenant1, "SharedProductName", 100m);
        await CreateProductAsync(tenant2, "SharedProductName", 200m);

        // Get product IDs
        var t1Products = await GetProductsAsync(tenant1);
        var t2Products = await GetProductsAsync(tenant2);

        Assert.Single(t1Products);
        Assert.Single(t2Products);

        var t1ProductId = t1Products[0].Id;
        var t2ProductId = t2Products[0].Id;

        // Act - Update product in tenant 1 only
        var updateRequest = new HttpRequestMessage(HttpMethod.Put, $"/api/products/{t1ProductId}");
        updateRequest.Headers.Add("X-Tenant-Id", tenant1);
        updateRequest.Content = JsonContent.Create(new { name = "UpdatedByTenant1", description = "Updated", price = 999m });
        var updateResponse = await _client.SendAsync(updateRequest);
        Assert.True(updateResponse.IsSuccessStatusCode);

        // Assert - Tenant 1 sees updated product
        t1Products = await GetProductsAsync(tenant1);
        Assert.Single(t1Products);
        Assert.Equal("UpdatedByTenant1", t1Products[0].Name);
        Assert.Equal(999m, t1Products[0].Price);

        // Assert - Tenant 2 product is UNCHANGED
        t2Products = await GetProductsAsync(tenant2);
        Assert.Single(t2Products);
        Assert.Equal("SharedProductName", t2Products[0].Name);
        Assert.Equal(200m, t2Products[0].Price);

        // Act - Delete product in tenant 1 only
        var deleteRequest = new HttpRequestMessage(HttpMethod.Delete, $"/api/products/{t1ProductId}");
        deleteRequest.Headers.Add("X-Tenant-Id", tenant1);
        var deleteResponse = await _client.SendAsync(deleteRequest);
        Assert.True(deleteResponse.IsSuccessStatusCode);

        // Assert - Tenant 1 has no products
        t1Products = await GetProductsAsync(tenant1);
        Assert.Empty(t1Products);

        // Assert - Tenant 2 still has its product
        t2Products = await GetProductsAsync(tenant2);
        Assert.Single(t2Products);
        Assert.Equal("SharedProductName", t2Products[0].Name);
    }

    /// <summary>
    /// Tests that requests without X-Tenant-Id header don't leak data from other tenants.
    /// </summary>
    [Fact]
    public async Task RequestWithoutTenantHeader_ShouldNotLeakData()
    {
        // Arrange
        var testRunId = Guid.NewGuid().ToString("N")[..8];
        var tenant = $"noleak_{testRunId}";

        await CreateTenantAsync(tenant);
        await Task.Delay(300);

        // Add product to tenant
        var createResponse = await CreateProductAsync(tenant, "SecretProduct", 100m);
        Assert.True(createResponse.IsSuccessStatusCode);

        // Act - Make request WITHOUT X-Tenant-Id header
        var response = await _client.GetAsync("/api/products");

        // Assert - Should fail or return empty (depending on middleware config)
        // The important thing is it should NOT return the tenant's data
        if (response.IsSuccessStatusCode)
        {
            var products = await response.Content.ReadFromJsonAsync<List<ProductResponse>>();
            Assert.True(products == null || products.Count == 0 ||
                       !products.Any(p => p.Name == "SecretProduct"),
                       "Request without tenant header returned tenant-specific data!");
        }
        // If it returns an error, that's also acceptable
    }

    private record ProductResponse(int Id, string Name, string? Description, decimal Price);
}
