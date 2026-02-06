namespace TenantCore.EntityFramework.ControlDb;

/// <summary>
/// Represents the status of a tenant in the control database.
/// </summary>
public enum TenantStatus
{
    /// <summary>
    /// Tenant is pending activation or provisioning.
    /// </summary>
    Pending = 0,

    /// <summary>
    /// Tenant is active and operational.
    /// </summary>
    Active = 1,

    /// <summary>
    /// Tenant is temporarily suspended.
    /// </summary>
    Suspended = 2,

    /// <summary>
    /// Tenant is disabled and cannot be accessed.
    /// </summary>
    Disabled = 3,

    /// <summary>
    /// Tenant is flagged for deletion.
    /// </summary>
    FlaggedForDelete = 4
}
