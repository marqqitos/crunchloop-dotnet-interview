using TodoApi.Dtos.External;
using TodoApi.Models;

namespace TodoApi.Services.SyncService;

public interface ISyncService
{
    /// <summary>
    /// Sync local TodoLists to external API (one-way: Local → External)
    /// </summary>
    Task SyncAllPendingTodoListsToExternal();

    /// <summary>
    /// Sync specific TodoLists to external API (one-way: Local → External)
    /// </summary>
    Task SyncTodoListsToExternal(IEnumerable<TodoList> todoListsToSync);

    /// <summary>
    /// Sync TodoLists from external API to local database (one-way: External → Local)
    /// </summary>
    Task SyncAllPendingTodoListsFromExternal();

    /// <summary>
    /// Sync specific TodoLists from external API to local database (one-way: External → Local)
    /// </summary>
    Task SyncTodoListsFromExternal(IEnumerable<ExternalTodoList> externalTodoListsToSync);

    /// <summary>
    /// Performs complete bidirectional synchronization (Local ↔ External)
    /// </summary>
    Task PerformFullSync();

    /// <summary>
    /// Detects and handles external deletions
    /// </summary>
    Task DetectAndHandleExternalDeletions();
}
