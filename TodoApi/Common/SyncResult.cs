namespace TodoApi.Common;

/// <summary>
/// Represents the result of a synchronization operation
/// </summary>
public class SyncResult
{
    public bool IsCreated { get; set; }
    public bool IsUpdated { get; set; }
    public bool IsUnchanged { get; set; }
    public bool ConflictResolved { get; set; }
    public string? ConflictReason { get; set; }
    
    public static SyncResult Created() => new() { IsCreated = true };
    public static SyncResult Updated() => new() { IsUpdated = true };
    public static SyncResult Unchanged() => new() { IsUnchanged = true };
    
    public static SyncResult WithConflictResolution(string reason) => new() 
    { 
        IsUpdated = true, 
        ConflictResolved = true, 
        ConflictReason = reason 
    };
}