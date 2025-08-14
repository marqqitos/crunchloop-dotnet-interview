using Microsoft.EntityFrameworkCore;

namespace TodoApi.Services.SyncStateService;

/// <summary>
/// Service for managing sync state and tracking last sync timestamps
/// </summary>
public class TodoListSyncStateService : ISyncStateService
{
    private readonly TodoContext _context;
    private readonly ILogger<TodoListSyncStateService> _logger;

    public TodoListSyncStateService(TodoContext context, ILogger<TodoListSyncStateService> logger)
    {
        _context = context;
        _logger = logger;
    }

    public async Task<DateTime?> GetLastSyncTimestamp()
    {
        // Get the most recent LastSyncedAt timestamp from all TodoLists and TodoItems
        var todoListLastSync = await _context.TodoList
            .Where(tl => tl.LastSyncedAt.HasValue)
            .MaxAsync(tl => tl.LastSyncedAt);

        var todoItemLastSync = await _context.TodoItem
            .Where(ti => ti.LastSyncedAt.HasValue)
            .MaxAsync(ti => ti.LastSyncedAt);

        // Return the most recent timestamp between TodoLists and TodoItems
        var lastSync = todoListLastSync.HasValue && todoItemLastSync.HasValue
            ? todoListLastSync.Value > todoItemLastSync.Value ? todoListLastSync.Value : todoItemLastSync.Value
            : todoListLastSync ?? todoItemLastSync;

        _logger.LogDebug("Last sync timestamp: {LastSync}", lastSync);
        return lastSync;
    }

    public async Task UpdateLastSyncTimestamp(DateTime syncTimestamp)
    {
        _logger.LogInformation("Updating last sync timestamp to {SyncTimestamp}", syncTimestamp);

        // Update all TodoLists that have been synced
        var syncedTodoLists = await _context.TodoList
            .Where(t => t.ExternalId != null)
            .ToListAsync();

        foreach (var todoList in syncedTodoLists)
        {
            todoList.LastSyncedAt = syncTimestamp;
        }

        // Update all TodoItems that have been synced
        var syncedTodoItems = await _context.TodoItem
            .Where(t => t.ExternalId != null)
            .ToListAsync();

        foreach (var todoItem in syncedTodoItems)
        {
            todoItem.LastSyncedAt = syncTimestamp;
        }

        await _context.SaveChangesAsync();

        _logger.LogInformation("Updated last sync timestamp for {TodoListCount} TodoLists and {TodoItemCount} TodoItems",
            syncedTodoLists.Count, syncedTodoItems.Count);
    }

    public async Task<bool> IsDeltaSyncAvailable()
    {
        var lastSync = await GetLastSyncTimestamp();
        var isAvailable = lastSync.HasValue;

        _logger.LogDebug("Delta sync available: {IsAvailable} (last sync: {LastSync})", isAvailable, lastSync);
        return isAvailable;
    }

    public async Task<DateTime?> GetEarliestLastModified()
    {
        // Get the earliest LastModified timestamp from all TodoLists and TodoItems
        // This can be used as a fallback for delta sync when no previous sync exists

        // Get all valid timestamps from TodoLists
        var todoListTimestamps = await _context.TodoList
            .Where(tl => tl.LastModified != default)
            .Select(tl => tl.LastModified)
            .ToListAsync();

        var todoItemTimestamps = await _context.TodoItem
            .Where(ti => ti.LastModified != default)
            .Select(ti => ti.LastModified)
            .ToListAsync();

        // Combine all timestamps and find the minimum
        var allTimestamps = todoListTimestamps.Concat(todoItemTimestamps);

        DateTime? earliest = null;
        if (allTimestamps.Any())
        {
            earliest = allTimestamps.Min();
        }

        _logger.LogDebug("Earliest last modified timestamp: {Earliest}", earliest);
        return earliest;
    }
}
