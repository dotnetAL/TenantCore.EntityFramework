using Microsoft.EntityFrameworkCore;

namespace TenantCore.EntityFramework.Configuration;

/// <summary>
/// Per-context options for tenant-aware DbContexts.
/// Allows overriding global settings (e.g., migration history table name) on a per-context basis.
/// </summary>
/// <typeparam name="TContext">The DbContext type.</typeparam>
public class TenantDbContextOptions<TContext> where TContext : DbContext
{
    /// <summary>
    /// Gets or sets the migration history table name for this context.
    /// When null, falls back to <see cref="MigrationOptions.MigrationHistoryTable"/>.
    /// </summary>
    public string? MigrationHistoryTable { get; set; }
}
