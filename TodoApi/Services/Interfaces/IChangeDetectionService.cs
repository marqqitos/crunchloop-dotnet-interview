namespace TodoApi.Services;

/// <summary>
/// Service for detecting changes and determining when sync is needed
/// </summary>
public interface IChangeDetectionService
{
    /// <summary>
    /// Checks if there are any pending changes that need to be synced
    /// </summary>
    /// <returns>True if there are pending changes, false otherwise</returns>
    Task<bool> HasPendingChangesAsync();

    /// <summary>
    /// Marks a TodoList as having pending changes
    /// </summary>
    /// <param name="todoListId">The ID of the TodoList</param>
    Task MarkTodoListAsPendingAsync(long todoListId);

    /// <summary>
    /// Marks a TodoItem as having pending changes
    /// </summary>
    /// <param name="todoItemId">The ID of the TodoItem</param>
    Task MarkTodoItemAsPendingAsync(long todoItemId);

    /// <summary>
    /// Clears the pending flag for a TodoList after successful sync
    /// </summary>
    /// <param name="todoListId">The ID of the TodoList</param>
    Task ClearTodoListPendingFlagAsync(long todoListId);

    /// <summary>
    /// Clears the pending flag for a TodoItem after successful sync
    /// </summary>
    /// <param name="todoItemId">The ID of the TodoItem</param>
    Task ClearTodoItemPendingFlagAsync(long todoItemId);

    /// <summary>
    /// Gets the count of pending changes
    /// </summary>
    /// <returns>The number of items with pending changes</returns>
    Task<int> GetPendingChangesCountAsync();
} 