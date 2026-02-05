# TenantCore.EntityFramework

A robust, extensible multi-tenancy solution for Entity Framework Core with initial support for schema-per-tenant isolation on PostgreSQL.

[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](https://opensource.org/licenses/MIT)

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

TenantCore includes several built-in tenant resolvers:

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
    options.UseConnectionString(connectionString);

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

## Requirements

- .NET 8.0 or .NET 10.0
- Entity Framework Core 8.x or 10.x
- PostgreSQL 12+ (for PostgreSQL provider)

## License

This project is licensed under the MIT License - see the [LICENSE](LICENSE) file for details.

## Contributing

Contributions are welcome! Please read our [Contributing Guide](CONTRIBUTING.md) for details on our code of conduct and the process for submitting pull requests.
