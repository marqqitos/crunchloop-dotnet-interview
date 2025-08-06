namespace TodoApi.Common;

/// <summary>
/// Strategies for resolving synchronization conflicts
/// </summary>
public enum ConflictResolutionStrategy
{
    /// <summary>
    /// External API changes always take precedence over local changes
    /// </summary>
    ExternalWins,

    /// <summary>
    /// Local changes take precedence over external changes
    /// </summary>
    LocalWins,

    /// <summary>
    /// Manual resolution required - throw exception or queue for review
    /// </summary>
    Manual
}

/// <summary>
/// Information about a detected conflict during synchronization
/// </summary>
public class ConflictInfo
{
    public string EntityType { get; set; } = string.Empty;
    public string EntityId { get; set; } = string.Empty;
    public DateTime LocalLastModified { get; set; }
    public DateTime ExternalLastModified { get; set; }
    public DateTime? LastSyncedAt { get; set; }
    public List<string> ModifiedFields { get; set; } = new List<string>();
    public ConflictResolutionStrategy Resolution { get; set; }
    public string ResolutionReason { get; set; } = string.Empty;

    public bool HasConflict => ModifiedFields.Count > 0 &&
							   LocalLastModified != default &&
                               ExternalLastModified != default &&
                               LastSyncedAt.HasValue &&
                               (LocalLastModified > LastSyncedAt.Value ||
                               ExternalLastModified > LastSyncedAt.Value);

    public bool ConflictResolved => HasConflict && !string.IsNullOrEmpty(ResolutionReason);
}
