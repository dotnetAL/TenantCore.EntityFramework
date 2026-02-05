# TenantCore.EntityFramework

A robust, extensible multi-tenancy solution for Entity Framework Core with schema-per-tenant isolation on PostgreSQL.

[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](https://opensource.org/licenses/MIT)
[![NuGet](https://img.shields.io/nuget/v/TenantCore.EntityFramework.svg)](https://www.nuget.org/packages/TenantCore.EntityFramework)
[![.NET](https://img.shields.io/badge/.NET-8.0%20%7C%2010.0-512BD4)](https://dotnet.microsoft.com/)

## Features

- **Schema-Per-Tenant Isolation**: Complete data separation at the database level
- **Pluggable Tenant Resolution**: Multiple built-in resolvers (header, claims, subdomain, route, query string)
- **Automatic Migration Management**: Apply migrations across all tenant schemas
- **Full Tenant Lifecycle**: Provision, archive, restore, and delete tenants
- **Event System**: Subscribe to tenant lifecycle events
- **Health Checks**: Monitor tenant database health
- **Extensible Architecture**: Add custom strategies, resolvers, and seeders

## Quick Start

### Installation

```bash
dotnet add package TenantCore.EntityFramework
dotnet add package TenantCore.EntityFramework.PostgreSql
```

### Basic Setup

```csharp
// Program.cs
var builder = WebApplication.CreateBuilder(args);

// Configure TenantCore
builder.Services.AddTenantCore<string>(options =>
{
    options.UsePostgreSql(connectionString);
    options.UseSchemaPerTenant(schema =>
    {
        schema.SchemaPrefix = "tenant_";
    });
});

builder.Services.AddTenantCorePostgreSql();
builder.Services.AddHeaderTenantResolver<string>();
builder.Services.AddTenantDbContextPostgreSql<AppDbContext, string>(connectionString);

var app = builder.Build();

// Add tenant resolution middleware
app.UseTenantResolution<string>();

app.Run();
```

### Create a Tenant-Aware DbContext

```csharp
public class AppDbContext : TenantDbContext<string>
{
    public DbSet<Product> Products => Set<Product>();

    public AppDbContext(
        DbContextOptions<AppDbContext> options,
        ITenantContextAccessor<string> tenantContextAccessor,
        TenantCoreOptions tenantOptions)
        : base(options, tenantContextAccessor, tenantOptions)
    {
    }
}
```

### Provision a New Tenant

```csharp
app.MapPost("/api/tenants/{tenantId}", async (
    string tenantId,
    ITenantManager<string> tenantManager) =>
{
    await tenantManager.ProvisionTenantAsync(tenantId);
    return Results.Created($"/api/tenants/{tenantId}", new { tenantId });
});
```

## Tenant Resolution

TenantCore includes several built-in tenant resolvers. Multiple resolvers can be registered and will be evaluated in priority order (lower priority values run first).

### Header-Based (Recommended for APIs)

```csharp
builder.Services.AddHeaderTenantResolver<string>("X-Tenant-Id");
```

### Claims-Based (JWT/Authentication)

```csharp
builder.Services.AddClaimsTenantResolver<string>("tenant_id");
```

### Subdomain-Based

```csharp
builder.Services.AddSubdomainTenantResolver<string>("example.com");
// tenant1.example.com -> tenant1
```

### Query String-Based

```csharp
builder.Services.AddScoped<ITenantResolver<string>>(sp =>
    new QueryStringTenantResolver<string>(
        sp.GetRequiredService<IHttpContextAccessor>(),
        "tenant"));
// /api/products?tenant=tenant1 -> tenant1
```

### Route Value-Based

```csharp
builder.Services.AddScoped<ITenantResolver<string>>(sp =>
    new RouteValueTenantResolver<string>(
        sp.GetRequiredService<IHttpContextAccessor>(),
        "tenantId"));
// /api/{tenantId}/products -> extracts from route
```

### Custom Resolver

```csharp
public class MyCustomResolver : ITenantResolver<string>
{
    public int Priority => 100;

    public Task<string?> ResolveTenantAsync(CancellationToken ct = default)
    {
        // Your custom logic here
        return Task.FromResult<string?>("tenant1");
    }
}

// Register
builder.Services.AddScoped<ITenantResolver<string>, MyCustomResolver>();
```

## Migration Management

### Automatic Migrations on Startup

```csharp
options.ConfigureMigrations(migrations =>
{
    migrations.ApplyOnStartup = true;
    migrations.ParallelMigrations = 4;
    migrations.Timeout = TimeSpan.FromMinutes(5);
});
```

### Manual Migration

```csharp
var tenantManager = serviceProvider.GetRequiredService<ITenantManager<string>>();

// Migrate specific tenant
await tenantManager.MigrateTenantAsync("tenant1");

// Migrate all tenants
await tenantManager.MigrateAllTenantsAsync();
```

## Tenant Lifecycle

### Provisioning

```csharp
await tenantManager.ProvisionTenantAsync("new-tenant");
```

### Archiving

```csharp
await tenantManager.ArchiveTenantAsync("tenant-to-archive");
```

### Restoring

```csharp
await tenantManager.RestoreTenantAsync("archived-tenant");
```

### Deletion

```csharp
// Soft delete (renames schema)
await tenantManager.DeleteTenantAsync("tenant-id", hardDelete: false);

// Hard delete (drops schema)
await tenantManager.DeleteTenantAsync("tenant-id", hardDelete: true);
```

## Tenant Scoping

Use `ITenantScopeFactory` to temporarily switch tenant context for background jobs or cross-tenant operations:

```csharp
public class CrossTenantService
{
    private readonly ITenantScopeFactory<string> _scopeFactory;
    private readonly IDbContextFactory<AppDbContext> _dbFactory;

    public CrossTenantService(
        ITenantScopeFactory<string> scopeFactory,
        IDbContextFactory<AppDbContext> dbFactory)
    {
        _scopeFactory = scopeFactory;
        _dbFactory = dbFactory;
    }

    public async Task ProcessAllTenantsAsync(IEnumerable<string> tenantIds)
    {
        foreach (var tenantId in tenantIds)
        {
            // Create a scope for the target tenant
            using var scope = _scopeFactory.CreateScope(tenantId);
            await using var db = await _dbFactory.CreateDbContextAsync();

            // All operations here use the scoped tenant's schema
            var products = await db.Products.ToListAsync();
            // ... process products
        }
    }
}
```

## Data Seeding

Seed initial data when provisioning new tenants:

```csharp
public class TenantDataSeeder : ITenantSeeder<AppDbContext, string>
{
    public int Order => 0; // Lower values run first

    public async Task SeedAsync(
        AppDbContext context,
        string tenantId,
        CancellationToken cancellationToken = default)
    {
        context.Products.Add(new Product
        {
            Name = "Welcome Product",
            Description = "Initial product for new tenants"
        });
        await context.SaveChangesAsync(cancellationToken);
    }

    // Required interface implementation
    Task ITenantSeeder<string>.SeedAsync(DbContext context, string tenantId, CancellationToken ct)
        => SeedAsync((AppDbContext)context, tenantId, ct);

    Task ITenantSeeder.SeedAsync(DbContext context, object tenantId, CancellationToken ct)
        => SeedAsync((AppDbContext)context, (string)tenantId, ct);
}

// Register in DI
builder.Services.AddScoped<ITenantSeeder<string>, TenantDataSeeder>();
```

## Events

Subscribe to tenant lifecycle events:

```csharp
public class TenantEventHandler : ITenantEventSubscriber<string>
{
    public Task OnTenantCreatedAsync(TenantCreatedEvent<string> @event, CancellationToken ct)
    {
        Console.WriteLine($"Tenant {@event.TenantId} was created");
        return Task.CompletedTask;
    }

    // ... other event handlers
}

builder.Services.AddTenantEventSubscriber<string, TenantEventHandler>();
```

## Health Checks

```csharp
builder.Services.AddTenantHealthChecks<AppDbContext, string>("tenants");
```

## Configuration Options

```csharp
builder.Services.AddTenantCore<string>(options =>
{
    // Connection
    options.UsePostgreSql(connectionString);

    // Schema isolation
    options.UseSchemaPerTenant(schema =>
    {
        schema.SchemaPrefix = "tenant_";
        schema.SharedSchema = "public";
        schema.ArchivedSchemaPrefix = "archived_";
    });

    // Migrations
    options.ConfigureMigrations(migrations =>
    {
        migrations.ApplyOnStartup = true;
        migrations.ParallelMigrations = 4;
        migrations.FailureBehavior = MigrationFailureBehavior.ContinueOthers;
        migrations.Timeout = TimeSpan.FromMinutes(5);
    });

    // Behavior
    options.OnTenantNotFound(TenantNotFoundBehavior.Throw);
    options.EnableCaching(TimeSpan.FromMinutes(5));
    options.DisableTenantValidation();
});
```

### Separate Migrations Assembly

When your migrations are in a separate assembly (common in clean architecture):

```csharp
builder.Services.AddTenantDbContextPostgreSql<AppDbContext, string>(
    connectionString,
    migrationsAssembly: "MyApp.Infrastructure");
```

## Shared Entities

Mark entities that should not be tenant-isolated:

```csharp
[SharedEntity]
public class GlobalConfiguration
{
    public int Id { get; set; }
    public string Key { get; set; }
    public string Value { get; set; }
}
```

Or use fluent configuration:

```csharp
protected override void ConfigureSharedEntities(ModelBuilder modelBuilder)
{
    modelBuilder.Entity<GlobalConfiguration>()
        .ToTable("GlobalConfiguration", "public");
}
```

## Supported Databases

| Database   | Package                              | Status |
|------------|--------------------------------------|--------|
| PostgreSQL | TenantCore.EntityFramework.PostgreSql | ✅ Supported |
| SQL Server | Coming in v2.0                       | 🔜 Planned |
| MySQL      | Coming in v2.0                       | 🔜 Planned |

## Sample Project

A complete sample Web API is included in the `samples/TenantCore.Sample.WebApi` directory, demonstrating:

- Tenant provisioning and management endpoints
- Tenant-scoped CRUD operations
- Health check configuration
- Swagger/OpenAPI integration

Run the sample:

```bash
cd samples/TenantCore.Sample.WebApi
dotnet run
```

## Requirements

- .NET 8.0 or .NET 10.0
- Entity Framework Core 8.x or 10.x
- PostgreSQL 12+ (for PostgreSQL provider)

## License

This project is licensed under the MIT License - see the [LICENSE](LICENSE) file for details.

## Contributing

Contributions are welcome! Please open an issue or submit a pull request on [GitHub](https://github.com/dotnetAL/TenantCore.EntityFramework).
