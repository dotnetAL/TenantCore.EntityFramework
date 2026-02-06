using Microsoft.EntityFrameworkCore;
using TenantCore.EntityFramework.Abstractions;
using TenantCore.EntityFramework.Extensions;
using TenantCore.EntityFramework.PostgreSql;
using TenantCore.Sample.WebApi;

var builder = WebApplication.CreateBuilder(args);

// Get connection string from configuration
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection")
    ?? "Host=localhost;Database=tenantcore_sample;Username=postgres;Password=postgres";

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

// Optional: Add control database for centralized tenant metadata
// Enable by setting TenantCore:UseControlDatabase=true in configuration
if (useControlDb)
{
    builder.Services.AddTenantControlDatabase(
        dbOptions => dbOptions.UseNpgsql(controlDbConnectionString),
        options =>
        {
            options.Schema = "tenant_control";
            options.EnableCaching = true;
            options.ApplyMigrationsOnStartup = true;
        });

    // Add API key resolver (requires control database)
    builder.Services.AddApiKeyTenantResolver<string>("X-Api-Key");
}

// Add tenant-aware DbContext
builder.Services.AddTenantDbContextPostgreSql<ApplicationDbContext, string>(connectionString);

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
    ITenantManager<string> tenantManager) =>
{
    try
    {
        await tenantManager.ProvisionTenantAsync(tenantId);
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

app.Run();

// Request/Response models
public record CreateProductRequest(string Name, string? Description, decimal Price);
public record UpdateProductRequest(string Name, string? Description, decimal Price);
