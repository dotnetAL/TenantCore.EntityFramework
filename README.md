# TenantCore.EntityFramework

A robust, extensible multi-tenancy solution for Entity Framework Core with schema-per-tenant isolation on PostgreSQL.

[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](https://opensource.org/licenses/MIT)
[![NuGet](https://img.shields.io/nuget/v/TenantCore.EntityFramework.svg)](https://www.nuget.org/packages/TenantCore.EntityFramework)
[![.NET](https://img.shields.io/badge/.NET-8.0%20%7C%2010.0-512BD4)](https://dotnet.microsoft.com/)

## Features

- **Schema-Per-Tenant Isolation**: Complete data separation at the database level
- **Pluggable Tenant Resolution**: Multiple built-in resolvers (header, claims, subdomain, path, route, query string, API key)
- **Control Database**: Optional centralized tenant metadata storage with status tracking, encrypted credentials, and API key authentication
- **Multiple DbContext Support**: Register multiple tenant-aware contexts with per-context migration history tables
- **Automatic Migration Management**: Apply migrations across all tenant schemas
- **Full Tenant Lifecycle**: Provision, archive, restore, and delete tenants
- **Event System**: Subscribe to tenant lifecycle events
- **Health Checks**: Monitor tenant database health
- **Tenant Validation**: Built-in schema-exists validator rejects unknown tenant IDs with a 403 before they hit the database
- **Extensible Architecture**: Add custom strategies, resolvers, and seeders

## Quick Start

### Prerequisites

- .NET 8.0+ SDK
- PostgreSQL 12+ (running and accessible)
- A PostgreSQL connection string (e.g., `Host=localhost;Database=myapp;Username=postgres;Password=secret`)

### Installation

```bash
dotnet add package TenantCore.EntityFramework
dotnet add package TenantCore.EntityFramework.PostgreSql
```

### 1. Create a Tenant-Aware DbContext

The `TKey` type parameter represents your tenant identifier type. Supported types are `string` and `Guid`. When using the Control Database feature, `TKey` must be convertible to `Guid`.

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

Each tenant gets its own PostgreSQL schema. For example, a tenant with ID `"acme"` and a prefix of `"tenant_"` will have all its tables created under the `tenant_acme` schema.

### 2. Register Services

```csharp
// Program.cs
var builder = WebApplication.CreateBuilder(args);

var connectionString = builder.Configuration.GetConnectionString("DefaultConnection")
    ?? throw new InvalidOperationException("ConnectionStrings:DefaultConnection is required.");

// Configure TenantCore
builder.Services.AddTenantCore<string>(options =>
{
    options.UsePostgreSql(connectionString);
    options.UseSchemaPerTenant(schema =>
    {
        schema.SchemaPrefix = "tenant_";
    });

    // Exclude paths that must work without a tenant context
    options.ExcludePaths("/api/tenants", "/health", "/swagger");
});

// Register tenant resolution (by HTTP header in this example)
builder.Services.AddHeaderTenantResolver<string>();

// Validate that the tenant exists and is active before allowing access
builder.Services.AddActiveTenantExistsValidator<AppDbContext, string>();

// Register the tenant-aware DbContext
builder.Services.AddTenantDbContextPostgreSql<AppDbContext, string>(connectionString);

var app = builder.Build();

// Add tenant resolution middleware
// Place after UseAuthentication() if using claims-based resolution,
// but before any endpoints that require tenant context.
app.UseTenantResolution<string>();
```

> **Important:** `ExcludePaths` is required for any endpoints that operate outside a tenant context (e.g., tenant provisioning, health checks). Without it, those endpoints will fail because the middleware cannot resolve a tenant.

### 3. Add Endpoints

```csharp
// Provision a new tenant (no X-Tenant-Id header needed -- path is excluded)
app.MapPost("/api/tenants/{tenantId}", async (
    string tenantId,
    ITenantManager<string> tenantManager) =>
{
    await tenantManager.ProvisionTenantAsync(tenantId);
    return Results.Created($"/api/tenants/{tenantId}", new { tenantId });
});

// Tenant-scoped endpoint (X-Tenant-Id header required)
app.MapGet("/api/products", async (AppDbContext db) =>
{
    var products = await db.Products.ToListAsync();
    return Results.Ok(products);
});

// Admin endpoint -- access a specific tenant without middleware
app.MapGet("/api/tenants/{tenantId}/products", async (
    string tenantId,
    IServiceProvider sp) =>
{
    await using var scope = await sp.GetTenantDbContextAsync<AppDbContext, string>(tenantId);
    var products = await scope.Context.Products.ToListAsync();
    return Results.Ok(products);
});

app.Run();
```

### 4. Create EF Core Migrations

Because `TenantDbContext<TKey>` requires `ITenantContextAccessor` and `TenantCoreOptions` in its constructor, you need a design-time factory so that `dotnet ef migrations` commands work without the full DI container:

```csharp
public class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    public AppDbContext CreateDbContext(string[] args)
    {
        var optionsBuilder = new DbContextOptionsBuilder<AppDbContext>();
        optionsBuilder.UseNpgsql();

        return new AppDbContext(
            optionsBuilder.Options,
            new DesignTimeTenantContextAccessor(),
            new TenantCoreOptions());
    }
}

// Minimal accessor that returns no tenant context at design time
public class DesignTimeTenantContextAccessor : ITenantContextAccessor<string>
{
    public TenantContext<string>? TenantContext => null;
    public void SetTenantContext(TenantContext<string>? context) { }
}
```

Then generate and apply migrations:

```bash
dotnet ef migrations add Initial --context AppDbContext
```

You do not need to run `dotnet ef database update` manually. Migrations are applied per-tenant schema when you call `ProvisionTenantAsync` or use the startup migration feature.

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

## Tenant Validation

By default, any string passed in the `X-Tenant-Id` header is accepted as a tenant ID. If the corresponding schema doesn't exist, the request fails with a raw database error. To prevent this, register the built-in active-tenant validator:

```csharp
builder.Services.AddActiveTenantExistsValidator<AppDbContext, string>();
```

This checks whether the tenant's schema actually exists in the database before the request reaches your endpoints. When a control database is also configured, it additionally verifies the tenant's status is `Active`.

Invalid or unknown tenant IDs receive an HTTP **403 Forbidden** response. Validated results are cached (default: 5 minutes) to avoid repeated database lookups.

You can also implement a custom validator:

```csharp
public class MyTenantValidator : ITenantValidator<string>
{
    public Task<bool> ValidateTenantAsync(string tenantId, CancellationToken ct = default)
    {
        // Your custom validation logic
        return Task.FromResult(true);
    }
}

builder.Services.AddScoped<ITenantValidator<string>, MyTenantValidator>();
```

## Shared Entities

Entities that should live in a shared schema (e.g., `public`) rather than per-tenant schemas can be configured by overriding `ConfigureSharedEntities` in your DbContext:

```csharp
public class AppDbContext : TenantDbContext<string>
{
    public DbSet<Product> Products => Set<Product>();
    public DbSet<GlobalConfiguration> GlobalConfigurations => Set<GlobalConfiguration>();

    // ... constructor

    protected override void ConfigureSharedEntities(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<GlobalConfiguration>()
            .ToTable("GlobalConfiguration", "public");
    }
}
```

Tables configured this way are placed in the shared schema and are accessible to all tenants.

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

## Multiple DbContexts

When your application has distinct bounded contexts (e.g., products and inventory), you can register multiple tenant-aware DbContexts against the same database. Each context manages its own entities and can be migrated independently.

Without per-context migration history tables, EF Core would see the other context's migrations as "unknown" in the shared `__EFMigrationsHistory` table and refuse to operate correctly. Per-context history tables solve this by giving each context its own isolated migration tracking.

### Define a Second DbContext

```csharp
public class InventoryDbContext : TenantDbContext<string>
{
    public DbSet<Order> Orders => Set<Order>();

    public InventoryDbContext(
        DbContextOptions<InventoryDbContext> options,
        ITenantContextAccessor<string> tenantContextAccessor,
        TenantCoreOptions tenantOptions)
        : base(options, tenantContextAccessor, tenantOptions)
    {
    }
}
```

### Register with Per-Context Migration History Tables

Each context gets its own migration history table within each tenant schema:

```csharp
// Each context tracks migrations in its own history table
builder.Services.AddTenantDbContextPostgreSql<AppDbContext, string>(
    connectionString,
    migrationsAssembly: "MyApp",
    migrationHistoryTable: "__ProductMigrations");

builder.Services.AddTenantDbContextPostgreSql<InventoryDbContext, string>(
    connectionString,
    migrationsAssembly: "MyApp",
    migrationHistoryTable: "__InventoryMigrations");

// Register a migration hosted service for each context
builder.Services.AddTenantMigrationHostedService<AppDbContext, string>();
builder.Services.AddTenantMigrationHostedService<InventoryDbContext, string>();
```

This produces the following structure per tenant schema:

```
tenant_acme/
  Products              (AppDbContext)
  Orders                (InventoryDbContext)
  __ProductMigrations   (AppDbContext history)
  __InventoryMigrations (InventoryDbContext history)
```

### Creating Migrations for Multiple Contexts

Each context needs its own `IDesignTimeDbContextFactory` and its own migration output directory:

```bash
# AppDbContext migrations (default output directory)
dotnet ef migrations add Initial --context AppDbContext

# InventoryDbContext migrations (separate output directory)
dotnet ef migrations add Initial --context InventoryDbContext --output-dir Migrations/Inventory
```

### Migrating Multiple Contexts

Each context has its own `TenantMigrationRunner` that can be resolved from DI:

```csharp
app.MapPost("/api/tenants/{tenantId}/migrate", async (
    string tenantId,
    TenantMigrationRunner<AppDbContext, string> appRunner,
    TenantMigrationRunner<InventoryDbContext, string> inventoryRunner) =>
{
    await appRunner.MigrateTenantAsync(tenantId);
    await inventoryRunner.MigrateTenantAsync(tenantId);
    return Results.Ok();
});
```

> **Important:** `ITenantManager.ProvisionTenantAsync` only applies migrations for the primary context (the first one registered). For additional contexts, you must explicitly call their `TenantMigrationRunner<TContext, TKey>.MigrateTenantAsync` during provisioning.

> **Note:** If you only have a single DbContext, you don't need to specify `migrationHistoryTable`. The default `__EFMigrationsHistory` table will be used.

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

### One-liner: `IServiceProvider.GetTenantDbContextAsync`

The simplest way to get a tenant-scoped DbContext — ideal for admin endpoints, background services, and cross-tenant operations:

```csharp
await using var scope = await sp.GetTenantDbContextAsync<AppDbContext, string>(tenantId);
var products = await scope.Context.Products.ToListAsync();
// Disposing the scope disposes the DbContext and restores the previous tenant context.
```

The returned `TenantDbContextScope<TContext>` is `IAsyncDisposable`. It sets up the tenant context, computes the schema name from `TenantCoreOptions`, and creates the DbContext via the registered factory. On dispose it cleans up both the DbContext and the tenant context automatically.

### Advanced: `ITenantScopeFactory` + `IDbContextFactory`

For scenarios where you need to control the scope and factory independently (e.g., creating multiple DbContexts within the same tenant scope), use `ITenantScopeFactory` with `IDbContextFactory<TContext>`:

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

    // Also implement: OnTenantDeletedAsync, OnTenantArchivedAsync,
    // OnTenantRestoredAsync, OnMigrationAppliedAsync, OnTenantResolvedAsync
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

    // Exclude paths from tenant resolution
    options.ExcludePaths("/api/tenants", "/health", "/swagger");
});
```

### Separate Migrations Assembly

When your migrations are in a separate assembly (common in clean architecture):

```csharp
builder.Services.AddTenantDbContextPostgreSql<AppDbContext, string>(
    connectionString,
    migrationsAssembly: "MyApp.Infrastructure");
```

## Supported Databases

| Database   | Package                              | Status |
|------------|--------------------------------------|--------|
| PostgreSQL | TenantCore.EntityFramework.PostgreSql | Supported |
| SQL Server | TenantCore.EntityFramework.SqlServer | Planned |
| MySQL      | TenantCore.EntityFramework.MySql     | Planned |

## Sample Project

A complete sample Web API is included in the `samples/TenantCore.Sample.WebApi` directory, demonstrating:

- Tenant provisioning and management endpoints
- Tenant-scoped CRUD operations
- Multiple DbContexts with per-context migration history tables (`ApplicationDbContext` + `InventoryDbContext`)
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

## Troubleshooting

**Migrations fail at design time** (`Unable to create an instance of 'AppDbContext'`): You need an `IDesignTimeDbContextFactory<TContext>` for each DbContext. See [Creating EF Core Migrations](#4-create-ef-core-migrations) above.

**Tenant provisioning endpoint returns a tenant-not-found error**: Add the provisioning path to `ExcludePaths` so it bypasses tenant resolution. See the `options.ExcludePaths(...)` call in the Quick Start.

**Unknown tenant ID causes a raw database error (e.g., `42P01: relation "tenant_unknown.Products" does not exist`)**: Register the active-tenant validator with `AddActiveTenantExistsValidator<TContext, TKey>()`. This rejects unknown tenants with a 403 before any database query runs.

**Second DbContext migrations are not applied when provisioning**: `ProvisionTenantAsync` only migrates the primary (first-registered) context. Call `TenantMigrationRunner<TContext, TKey>.MigrateTenantAsync` explicitly for each additional context.

## License

This project is licensed under the MIT License - see the [LICENSE](LICENSE) file for details.

## Contributing

Contributions are welcome! Please open an issue or submit a pull request on [GitHub](https://github.com/dotnetAL/TenantCore.EntityFramework).
