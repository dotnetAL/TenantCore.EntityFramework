# TenantCore.EntityFramework

A robust, extensible multi-tenancy solution for Entity Framework Core with schema-per-tenant isolation on PostgreSQL.

[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](https://opensource.org/licenses/MIT)
[![NuGet](https://img.shields.io/nuget/v/TenantCore.EntityFramework.svg)](https://www.nuget.org/packages/TenantCore.EntityFramework)
[![.NET](https://img.shields.io/badge/.NET-8.0%20%7C%2010.0-512BD4)](https://dotnet.microsoft.com/)

## Features

- **Schema-Per-Tenant Isolation**: Complete data separation at the database level
- **Pluggable Tenant Resolution**: Multiple built-in resolvers (header, claims, subdomain, path, route, query string, API key)
- **Control Database**: Optional centralized tenant metadata storage with status tracking, encrypted credentials, and API key authentication
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

TenantCore includes several built-in tenant resolvers. Multiple resolvers can be registered and will be evaluated in priority order (higher priority values run first).

| Resolver | Default Priority | Registration Helper |
|----------|-----------------|---------------------|
| Claims | 200 | `AddClaimsTenantResolver<TKey>()` |
| API Key | 175 | `AddApiKeyTenantResolver<TKey>()` |
| Route Value | 150 | Manual `AddScoped` |
| Path | 125 | `AddPathTenantResolver<TKey>()` |
| Header | 100 | `AddHeaderTenantResolver<TKey>()` |
| Subdomain | 50 | `AddSubdomainTenantResolver<TKey>()` |
| Query String | 25 | Manual `AddScoped` |

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

### Path-Based

```csharp
// By segment index (0-based)
builder.Services.AddPathTenantResolver<string>(segmentIndex: 0);
// /{tenant}/api/products -> tenant

// By path prefix
builder.Services.AddPathTenantResolverWithPrefix<string>("/api");
// /api/{tenant}/products -> tenant
```

### API Key-Based (Requires Control Database)

```csharp
builder.Services.AddApiKeyTenantResolver<Guid>("X-Api-Key");
// Verifies tenant API key using salted PBKDF2-SHA256 hashing in the control database
// Only returns Active tenants
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
public class TenantDataSeeder : ITenantSeeder<string>
{
    public int Order => 0; // Lower values run first

    public async Task SeedAsync(
        DbContext context,
        string tenantId,
        CancellationToken cancellationToken = default)
    {
        context.Set<Product>().Add(new Product
        {
            Name = "Welcome Product",
            Description = "Initial product for new tenants"
        });
        await context.SaveChangesAsync(cancellationToken);
    }
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

## Control Database (Optional)

The Control Database feature provides centralized tenant metadata storage with support for:
- Tenant status tracking (Pending, Active, Suspended, Disabled, FlaggedForDelete)
- Encrypted database credentials
- API key authentication (salted PBKDF2-SHA256 hashed)
- Caching for improved performance

### Setup

```csharp
// Add control database with PostgreSQL
builder.Services.AddTenantControlDatabase(
    dbOptions => dbOptions.UseNpgsql(controlDbConnectionString),
    options =>
    {
        options.Schema = "tenant_control";
        options.EnableCaching = true;
        options.CacheDuration = TimeSpan.FromMinutes(5);
        options.ApplyMigrationsOnStartup = true;
        options.MigratableStatuses = [TenantStatus.Pending, TenantStatus.Active];
    });
```

### Provisioning with Control Database

When the control database is configured, use the extended provisioning method:

```csharp
var tenantManager = app.Services.GetRequiredService<TenantManager<AppDbContext, Guid>>();

var request = new CreateTenantRequest(
    TenantSlug: "acme-corp",
    TenantSchema: "tenant_acme",
    TenantApiKey: "sk_live_abc123..."  // Will be hashed with salted PBKDF2-SHA256
);

var tenant = await tenantManager.ProvisionTenantAsync(Guid.NewGuid(), request);
// Creates control DB record (Pending) -> provisions schema -> sets status to Active
```

### Custom Tenant Store (BYO)

Implement your own tenant storage by implementing `ITenantStore`:

```csharp
public class MyTenantStore : ITenantStore
{
    // Implement all ITenantStore methods
}

// Register
builder.Services.AddTenantStore<MyTenantStore>(options =>
{
    options.EnableCaching = true;
});
```

### Tenant Record Fields

| Field | Description |
|-------|-------------|
| TenantId | Unique identifier (Guid) |
| TenantSlug | URL-friendly identifier |
| Status | Tenant status enum |
| TenantSchema | Database schema name |
| TenantDatabase | Optional separate database |
| TenantDbServer | Optional separate server |
| TenantDbUser | Optional database user |
| TenantDbPasswordEncrypted | Encrypted password (Data Protection API) |
| TenantApiKeyHash | Salted PBKDF2-SHA256 hash of API key |
| CreatedAt / UpdatedAt | Timestamps |

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
| SQL Server | TenantCore.EntityFramework.SqlServer | 🔜 Planned |
| MySQL      | TenantCore.EntityFramework.MySql     | 🔜 Planned |

## Sample Project

A complete sample Web API is included in the `samples/TenantCore.Sample.WebApi` directory, demonstrating:

- Tenant provisioning and management endpoints
- Tenant-scoped CRUD operations
- Health check configuration
- Swagger/OpenAPI integration
- Optional Control Database integration

Run the sample:

```bash
cd samples/TenantCore.Sample.WebApi
dotnet run
```

To enable the Control Database feature in the sample:

```bash
dotnet run -- --TenantCore:UseControlDatabase=true
```

## Requirements

- .NET 8.0 or .NET 10.0
- Entity Framework Core 8.x or 10.x
- PostgreSQL 12+ (for PostgreSQL provider)

## License

This project is licensed under the MIT License - see the [LICENSE](LICENSE) file for details.

## Contributing

Contributions are welcome! Please open an issue or submit a pull request on [GitHub](https://github.com/dotnetAL/TenantCore.EntityFramework).
