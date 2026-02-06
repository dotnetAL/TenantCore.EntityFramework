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
builder.Services.AddSwaggerGen();

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
.WithOpenApi();

app.MapGet("/api/tenants", async (ITenantManager<string> tenantManager) =>
{
    var tenants = await tenantManager.GetTenantsAsync();
    return Results.Ok(tenants);
})
.WithName("ListTenants")
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
.WithOpenApi();

// Product endpoints (tenant-scoped)
app.MapGet("/api/products", async (ApplicationDbContext db) =>
{
    var products = await db.Products.ToListAsync();
    return Results.Ok(products);
})
.WithName("GetProducts")
.WithOpenApi();

app.MapGet("/api/products/{id:int}", async (int id, ApplicationDbContext db) =>
{
    var product = await db.Products.FindAsync(id);
    return product != null ? Results.Ok(product) : Results.NotFound();
})
.WithName("GetProduct")
.WithOpenApi();

app.MapPost("/api/products", async (CreateProductRequest request, ApplicationDbContext db) =>
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
.WithOpenApi();

app.MapPut("/api/products/{id:int}", async (int id, UpdateProductRequest request, ApplicationDbContext db) =>
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
.WithOpenApi();

app.MapDelete("/api/products/{id:int}", async (int id, ApplicationDbContext db) =>
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
.WithOpenApi();

app.Run();

// Request/Response models
public record CreateProductRequest(string Name, string? Description, decimal Price);
public record UpdateProductRequest(string Name, string? Description, decimal Price);
