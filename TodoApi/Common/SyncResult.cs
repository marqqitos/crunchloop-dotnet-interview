namespace TodoApi.Common;

/// <summary>
/// Represents the result of a sync operation between local and external systems
/// </summary>
public class SyncResult
{
    /// <summary>
    /// Indicates if a new record was created during sync
    /// </summary>
    public bool IsCreated { get; set; }

    /// <summary>
    /// Indicates if an existing record was updated during sync
    /// </summary> 
    public bool IsUpdated { get; set; }

    /// <summary>
    /// Indicates if no changes were needed during sync
    /// </summary>
    public bool IsUnchanged { get; set; }
}
