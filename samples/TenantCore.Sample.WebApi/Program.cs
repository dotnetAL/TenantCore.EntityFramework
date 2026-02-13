using Microsoft.EntityFrameworkCore;
using TenantCore.EntityFramework.Abstractions;
using TenantCore.EntityFramework.Context;
using TenantCore.EntityFramework.ControlDb;
using TenantCore.EntityFramework.Extensions;
using TenantCore.EntityFramework.Migrations;
using TenantCore.EntityFramework.PostgreSql;
using TenantCore.Sample.WebApi;

var builder = WebApplication.CreateBuilder(args);

// Get connection string from configuration (required - no default)
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection")
    ?? throw new InvalidOperationException("ConnectionStrings:DefaultConnection is required. Set via environment variable or appsettings.json");

// Optional: Control database connection string (can be same or different database)
var controlDbConnectionString = builder.Configuration.GetConnectionString("ControlDatabase")
    ?? connectionString;

// Check if control database feature is enabled
var useControlDb = builder.Configuration.GetValue<bool>("TenantCore:UseControlDatabase");

// Add TenantCore services
builder.Services.AddTenantCore<string>(options =>
{
    options.UsePostgreSql(connectionString);
    options.UseSchemaPerTenant(schema =>
    {
        schema.SchemaPrefix = "tenant_";
        schema.SharedSchema = "public";
    });
    options.ConfigureMigrations(migrations =>
    {
        migrations.ApplyOnStartup = true;
        migrations.ParallelMigrations = 2;
    });
    // Exclude tenant management endpoints from tenant resolution
    options.ExcludePaths("/api/tenants", "/health", "/swagger");
});

// Add PostgreSQL-specific services
builder.Services.AddTenantCorePostgreSql();

// Add HTTP tenant resolver (from X-Tenant-Id header)
builder.Services.AddHeaderTenantResolver<string>();

// Validate that the tenant exists and is active before allowing access
builder.Services.AddActiveTenantExistsValidator<ApplicationDbContext, string>();

// Optional: Add control database for centralized tenant metadata
// Enable by setting TenantCore:UseControlDatabase=true in configuration
if (useControlDb)
{
    builder.Services.AddTenantControlDatabase(
        dbOptions => dbOptions.UseNpgsql(controlDbConnectionString, npgsql =>
        {
            // Control database migrations are in TenantCore.EntityFramework.PostgreSql
            npgsql.MigrationsAssembly("TenantCore.EntityFramework.PostgreSql");
        }),
        options =>
        {
            options.Schema = "tenant_control";
            options.EnableCaching = true;
            options.ApplyMigrationsOnStartup = true;
        });

    // Add API key resolver (requires control database)
    builder.Services.AddApiKeyTenantResolver<string>("X-Api-Key");
}

// Add tenant-aware DbContexts with per-context migration history tables
builder.Services.AddTenantDbContextPostgreSql<ApplicationDbContext, string>(
    connectionString,
    migrationsAssembly: "TenantCore.Sample.WebApi",
    migrationHistoryTable: "__ProductMigrations");

builder.Services.AddTenantDbContextPostgreSql<InventoryDbContext, string>(
    connectionString,
    migrationsAssembly: "TenantCore.Sample.WebApi",
    migrationHistoryTable: "__InventoryMigrations");

// Register startup migration hosted services for both contexts
// When ApplyOnStartup is true, these apply pending migrations to all existing tenants at startup
builder.Services.AddTenantMigrationHostedService<ApplicationDbContext, string>();
builder.Services.AddTenantMigrationHostedService<InventoryDbContext, string>();

// Add health checks
builder.Services.AddTenantHealthChecks<ApplicationDbContext, string>("tenants");

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new Microsoft.OpenApi.Models.OpenApiInfo
    {
        Title = "TenantCore Sample API",
        Version = "v1",
        Description = "Sample API demonstrating multi-tenant functionality with TenantCore"
    });

    // Add X-Tenant-Id header parameter for tenant-scoped endpoints
    options.AddSecurityDefinition("TenantId", new Microsoft.OpenApi.Models.OpenApiSecurityScheme
    {
        Type = Microsoft.OpenApi.Models.SecuritySchemeType.ApiKey,
        In = Microsoft.OpenApi.Models.ParameterLocation.Header,
        Name = "X-Tenant-Id",
        Description = "Tenant identifier for multi-tenant operations"
    });

    // Add X-Api-Key header for API key authentication (when control database is enabled)
    options.AddSecurityDefinition("ApiKey", new Microsoft.OpenApi.Models.OpenApiSecurityScheme
    {
        Type = Microsoft.OpenApi.Models.SecuritySchemeType.ApiKey,
        In = Microsoft.OpenApi.Models.ParameterLocation.Header,
        Name = "X-Api-Key",
        Description = "API key for tenant authentication (alternative to X-Tenant-Id)"
    });
});

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

// Use tenant resolution middleware
app.UseTenantResolution<string>();

app.MapHealthChecks("/health");

// Tenant management endpoints
app.MapPost("/api/tenants/{tenantId}", async (
    string tenantId,
    ITenantManager<string> tenantManager,
    TenantMigrationRunner<InventoryDbContext, string> inventoryMigrationRunner) =>
{
    try
    {
        // Provision creates the schema and applies ApplicationDbContext migrations via TenantMigrationRunner
        await tenantManager.ProvisionTenantAsync(tenantId);

        // Also apply InventoryDbContext migrations to the same tenant schema via TenantMigrationRunner
        await inventoryMigrationRunner.MigrateTenantAsync(tenantId);

        return Results.Created($"/api/tenants/{tenantId}", new { tenantId, message = "Tenant created successfully" });
    }
    catch (TenantAlreadyExistsException)
    {
        return Results.Conflict(new { error = $"Tenant '{tenantId}' already exists" });
    }
})
.WithName("CreateTenant")
.WithTags("Tenant Management")
.WithDescription("Create a new tenant and provision its database schema")
.WithOpenApi();

app.MapGet("/api/tenants", async (ITenantManager<string> tenantManager) =>
{
    var tenants = await tenantManager.GetTenantsAsync();
    return Results.Ok(tenants);
})
.WithName("ListTenants")
.WithTags("Tenant Management")
.WithDescription("List all provisioned tenants")
.WithOpenApi();

app.MapDelete("/api/tenants/{tenantId}", async (
    string tenantId,
    bool? hardDelete,
    ITenantManager<string> tenantManager) =>
{
    try
    {
        await tenantManager.DeleteTenantAsync(tenantId, hardDelete ?? false);
        return Results.NoContent();
    }
    catch (TenantNotFoundException)
    {
        return Results.NotFound(new { error = $"Tenant '{tenantId}' not found" });
    }
})
.WithName("DeleteTenant")
.WithTags("Tenant Management")
.WithDescription("Delete a tenant. Use hardDelete=true to permanently remove all data.")
.WithOpenApi();

// Tenant info endpoint - demonstrates ICurrentTenantRecordAccessor (requires control database)
app.MapGet("/api/tenant-info", async (ICurrentTenantRecordAccessor recordAccessor) =>
{
    var record = await recordAccessor.GetCurrentTenantRecordAsync();
    return record != null ? Results.Ok(record) : Results.NotFound();
})
.WithName("GetTenantInfo")
.WithTags("Tenant Info (Tenant-Scoped)")
.WithDescription("Get the current tenant's record from the control database. Requires X-Tenant-Id header and control database enabled.")
.WithOpenApi(operation =>
{
    operation.Security.Add(new Microsoft.OpenApi.Models.OpenApiSecurityRequirement
    {
        {
            new Microsoft.OpenApi.Models.OpenApiSecurityScheme
            {
                Reference = new Microsoft.OpenApi.Models.OpenApiReference
                {
                    Type = Microsoft.OpenApi.Models.ReferenceType.SecurityScheme,
                    Id = "TenantId"
                }
            },
            Array.Empty<string>()
        }
    });
    return operation;
});

// Product endpoints (tenant-scoped) - require X-Tenant-Id header
var productEndpoints = app.MapGroup("/api/products")
    .WithTags("Products (Tenant-Scoped)")
    .WithOpenApi(operation =>
    {
        operation.Security.Add(new Microsoft.OpenApi.Models.OpenApiSecurityRequirement
        {
            {
                new Microsoft.OpenApi.Models.OpenApiSecurityScheme
                {
                    Reference = new Microsoft.OpenApi.Models.OpenApiReference
                    {
                        Type = Microsoft.OpenApi.Models.ReferenceType.SecurityScheme,
                        Id = "TenantId"
                    }
                },
                Array.Empty<string>()
            }
        });
        return operation;
    });

productEndpoints.MapGet("", async (ApplicationDbContext db) =>
{
    var products = await db.Products.ToListAsync();
    return Results.Ok(products);
})
.WithName("GetProducts")
.WithDescription("Get all products for the current tenant. Requires X-Tenant-Id header.");

productEndpoints.MapGet("/{id:int}", async (int id, ApplicationDbContext db) =>
{
    var product = await db.Products.FindAsync(id);
    return product != null ? Results.Ok(product) : Results.NotFound();
})
.WithName("GetProduct")
.WithDescription("Get a specific product by ID. Requires X-Tenant-Id header.");

productEndpoints.MapPost("", async (CreateProductRequest request, ApplicationDbContext db) =>
{
    var product = new Product
    {
        Name = request.Name,
        Description = request.Description,
        Price = request.Price
    };

    db.Products.Add(product);
    await db.SaveChangesAsync();

    return Results.Created($"/api/products/{product.Id}", product);
})
.WithName("CreateProduct")
.WithDescription("Create a new product for the current tenant. Requires X-Tenant-Id header.");

productEndpoints.MapPut("/{id:int}", async (int id, UpdateProductRequest request, ApplicationDbContext db) =>
{
    var product = await db.Products.FindAsync(id);
    if (product == null)
    {
        return Results.NotFound();
    }

    product.Name = request.Name;
    product.Description = request.Description;
    product.Price = request.Price;

    await db.SaveChangesAsync();
    return Results.Ok(product);
})
.WithName("UpdateProduct")
.WithDescription("Update an existing product. Requires X-Tenant-Id header.");

productEndpoints.MapDelete("/{id:int}", async (int id, ApplicationDbContext db) =>
{
    var product = await db.Products.FindAsync(id);
    if (product == null)
    {
        return Results.NotFound();
    }

    db.Products.Remove(product);
    await db.SaveChangesAsync();
    return Results.NoContent();
})
.WithName("DeleteProduct")
.WithDescription("Delete a product. Requires X-Tenant-Id header.");

// On-demand per-tenant migration endpoint (migrates both contexts)
app.MapPost("/api/tenants/{tenantId}/migrate", async (
    string tenantId,
    TenantMigrationRunner<ApplicationDbContext, string> appMigrationRunner,
    TenantMigrationRunner<InventoryDbContext, string> inventoryMigrationRunner) =>
{
    try
    {
        await appMigrationRunner.MigrateTenantAsync(tenantId);
        await inventoryMigrationRunner.MigrateTenantAsync(tenantId);
        return Results.Ok(new { tenantId, message = "Migrations applied for both contexts" });
    }
    catch (Exception ex)
    {
        return Results.Problem(detail: ex.Message, title: "Migration failed");
    }
})
.WithName("MigrateTenant")
.WithTags("Tenant Management")
.WithDescription("Apply pending migrations for a specific tenant (both Product and Inventory contexts)")
.WithOpenApi();

// Admin endpoint to get products for a specific tenant
app.MapGet("/api/tenants/{tenantId}/products", async (
    string tenantId,
    ITenantContextAccessor<string> accessor,
    IServiceProvider sp) =>
{
    using var scope = new TenantScope<string>(
        accessor,
        tenantId,
        $"tenant_{tenantId}");

    await using var context = await accessor.GetTenantDbContextAsync<ApplicationDbContext, string>(sp);
    var products = await context.Products.ToListAsync();
    return Results.Ok(products);
})
.WithName("GetTenantProducts")
.WithTags("Tenant Management")
.WithDescription("Get all products for a specific tenant (admin endpoint)")
.WithOpenApi();

// Order endpoints (tenant-scoped) - require X-Tenant-Id header
var orderEndpoints = app.MapGroup("/api/orders")
    .WithTags("Orders (Tenant-Scoped)")
    .WithOpenApi(operation =>
    {
        operation.Security.Add(new Microsoft.OpenApi.Models.OpenApiSecurityRequirement
        {
            {
                new Microsoft.OpenApi.Models.OpenApiSecurityScheme
                {
                    Reference = new Microsoft.OpenApi.Models.OpenApiReference
                    {
                        Type = Microsoft.OpenApi.Models.ReferenceType.SecurityScheme,
                        Id = "TenantId"
                    }
                },
                Array.Empty<string>()
            }
        });
        return operation;
    });

orderEndpoints.MapGet("", async (InventoryDbContext db) =>
{
    var orders = await db.Orders.Include(o => o.Items).ToListAsync();
    return Results.Ok(orders);
})
.WithName("GetOrders")
.WithDescription("Get all orders for the current tenant. Requires X-Tenant-Id header.");

orderEndpoints.MapGet("/{id:int}", async (int id, InventoryDbContext db) =>
{
    var order = await db.Orders.Include(o => o.Items).FirstOrDefaultAsync(o => o.Id == id);
    return order != null ? Results.Ok(order) : Results.NotFound();
})
.WithName("GetOrder")
.WithDescription("Get a specific order by ID. Requires X-Tenant-Id header.");

orderEndpoints.MapPost("", async (CreateOrderRequest request, InventoryDbContext db) =>
{
    var order = new Order
    {
        CustomerName = request.CustomerName,
        TotalAmount = request.Items.Sum(i => i.Quantity * i.UnitPrice),
        Items = request.Items.Select(i => new OrderItem
        {
            ProductName = i.ProductName,
            Quantity = i.Quantity,
            UnitPrice = i.UnitPrice
        }).ToList()
    };

    db.Orders.Add(order);
    await db.SaveChangesAsync();

    return Results.Created($"/api/orders/{order.Id}", order);
})
.WithName("CreateOrder")
.WithDescription("Create a new order for the current tenant. Requires X-Tenant-Id header.");

orderEndpoints.MapDelete("/{id:int}", async (int id, InventoryDbContext db) =>
{
    var order = await db.Orders.FindAsync(id);
    if (order == null)
    {
        return Results.NotFound();
    }

    db.Orders.Remove(order);
    await db.SaveChangesAsync();
    return Results.NoContent();
})
.WithName("DeleteOrder")
.WithDescription("Delete an order. Requires X-Tenant-Id header.");

app.Run();

// Request/Response models
public record CreateProductRequest(string Name, string? Description, decimal Price);
public record UpdateProductRequest(string Name, string? Description, decimal Price);
public record CreateOrderItemRequest(string ProductName, int Quantity, decimal UnitPrice);
public record CreateOrderRequest(string CustomerName, List<CreateOrderItemRequest> Items);

// Make Program accessible for WebApplicationFactory in integration tests
public partial class Program { }
