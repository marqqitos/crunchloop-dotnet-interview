namespace TodoApi.Services.SyncStateService;

/// <summary>
/// Service for managing sync state and tracking last sync timestamps
/// </summary>
public interface ISyncStateService
{
    /// <summary>
    /// Get the last sync timestamp for delta sync
    /// </summary>
    Task<DateTime?> GetLastSyncTimestampAsync();

    /// <summary>
    /// Update the last sync timestamp after a successful sync
    /// </summary>
    Task UpdateLastSyncTimestampAsync(DateTime syncTimestamp);

    /// <summary>
    /// Check if delta sync is available (we have a previous sync timestamp)
    /// </summary>
    Task<bool> IsDeltaSyncAvailableAsync();

    /// <summary>
    /// Get the earliest last modified timestamp from local entities for fallback
    /// </summary>
    Task<DateTime?> GetEarliestLastModifiedAsync();
}
