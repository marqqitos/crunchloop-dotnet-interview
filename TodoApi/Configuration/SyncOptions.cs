namespace TodoApi.Configuration;

/// <summary>
/// Configuration options for sync background service
/// </summary>
public class SyncOptions
{
    public const string SectionName = "Sync";

    /// <summary>
    /// Interval between sync operations in minutes
    /// </summary>
    public int SyncIntervalSeconds { get; set; } = 300;

    /// <summary>
    /// Whether to enable the background sync service
    /// </summary>
    public bool EnableBackgroundSync { get; set; } = true;

    /// <summary>
    /// Maximum duration for a single sync operation in minutes
    /// </summary>
    public int MaxSyncDurationMinutes { get; set; } = 10;

    /// <summary>
    /// Whether to perform sync on application startup
    /// </summary>
    public bool SyncOnStartup { get; set; } = false;
}
