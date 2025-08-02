namespace TodoApi.Services;

public interface ISyncService
{
    /// <summary>
    /// Sync local TodoLists to external API (one-way: Local → External)
    /// </summary>
    Task SyncTodoListsToExternalAsync();
}