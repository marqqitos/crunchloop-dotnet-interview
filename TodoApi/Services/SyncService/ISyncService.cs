namespace TodoApi.Services.SyncService;

public interface ISyncService
{
    /// <summary>
    /// Sync local TodoLists to external API (one-way: Local → External)
    /// </summary>
    Task SyncTodoListsToExternal();

    /// <summary>
    /// Sync TodoLists from external API to local database (one-way: External → Local)
    /// </summary>
    Task SyncTodoListsFromExternal();

    /// <summary>
    /// Performs complete bidirectional synchronization (Local ↔ External)
    /// </summary>
    Task PerformFullSync();

    /// <summary>
    /// Detects and handles external deletions
    /// </summary>
    Task DetectAndHandleExternalDeletions();
}
