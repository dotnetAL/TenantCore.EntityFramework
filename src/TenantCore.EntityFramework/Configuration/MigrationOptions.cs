namespace TenantCore.EntityFramework.Configuration;

/// <summary>
/// Configuration options for tenant migration management.
/// </summary>
public class MigrationOptions
{
    /// <summary>
    /// Gets or sets whether to apply migrations automatically on application startup.
    /// </summary>
    public bool ApplyOnStartup { get; set; } = false;

    /// <summary>
    /// Gets or sets the maximum number of parallel tenant migrations.
    /// </summary>
    public int ParallelMigrations { get; set; } = 1;

    /// <summary>
    /// Gets or sets the behavior when a tenant migration fails.
    /// </summary>
    public MigrationFailureBehavior FailureBehavior { get; set; } = MigrationFailureBehavior.StopAll;

    /// <summary>
    /// Gets or sets the timeout for individual tenant migrations.
    /// </summary>
    public TimeSpan Timeout { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>
    /// Gets or sets whether to wrap each tenant migration in a transaction.
    /// </summary>
    public bool UseTransactions { get; set; } = true;

    /// <summary>
    /// Gets or sets whether to create the migrations history table in each tenant schema.
    /// </summary>
    public bool SeparateMigrationHistory { get; set; } = true;

    /// <summary>
    /// Gets or sets the name of the migrations history table.
    /// </summary>
    public string MigrationHistoryTable { get; set; } = "__EFMigrationsHistory";

    /// <summary>
    /// Gets or sets whether to retry failed migrations.
    /// </summary>
    public bool RetryOnFailure { get; set; } = false;

    /// <summary>
    /// Gets or sets the number of retry attempts for failed migrations.
    /// </summary>
    public int RetryCount { get; set; } = 3;

    /// <summary>
    /// Gets or sets the delay between retry attempts.
    /// </summary>
    public TimeSpan RetryDelay { get; set; } = TimeSpan.FromSeconds(5);
}

/// <summary>
/// Defines the behavior when a tenant migration fails.
/// </summary>
public enum MigrationFailureBehavior
{
    /// <summary>
    /// Stop all migrations when any tenant migration fails.
    /// </summary>
    StopAll,

    /// <summary>
    /// Continue migrating other tenants when one fails.
    /// </summary>
    ContinueOthers,

    /// <summary>
    /// Skip the failed tenant and continue with the rest.
    /// </summary>
    Skip
}
