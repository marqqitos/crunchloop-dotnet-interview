using Microsoft.EntityFrameworkCore;
using TodoApi.Dtos.External;
using TodoApi.Models;

namespace TodoApi.Services;

public class TodoSyncService : ISyncService
{
    private readonly TodoContext _context;
    private readonly IExternalTodoApiClient _externalApiClient;
    private readonly ILogger<TodoSyncService> _logger;

    public TodoSyncService(
        TodoContext context,
        IExternalTodoApiClient externalApiClient,
        ILogger<TodoSyncService> logger)
    {
        _context = context;
        _externalApiClient = externalApiClient;
        _logger = logger;
    }

    public async Task SyncTodoListsToExternalAsync()
    {
        _logger.LogInformation("Starting one-way sync of TodoLists to external API");

        try
        {
            // Find TodoLists that haven't been synced yet (no ExternalId)
            var unsyncedTodoLists = await _context.TodoList
                .Where(tl => tl.ExternalId == null)
                .Include(tl => tl.Items) // Include items for complete sync
                .ToListAsync();

            if (!unsyncedTodoLists.Any())
            {
                _logger.LogInformation("No unsynced TodoLists found");
                return;
            }

            _logger.LogInformation("Found {Count} unsynced TodoLists to sync", unsyncedTodoLists.Count);

            var syncedCount = 0;
            var failedCount = 0;

            foreach (var todoList in unsyncedTodoLists)
            {
                try
                {
                    await SyncSingleTodoListAsync(todoList);
                    syncedCount++;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to sync TodoList {TodoListId} '{Name}'", 
                        todoList.Id, todoList.Name);
                    failedCount++;
                }
            }

            _logger.LogInformation("Sync completed. Success: {SyncedCount}, Failed: {FailedCount}", 
                syncedCount, failedCount);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to sync TodoLists to external API");
            throw;
        }
    }

    private async Task SyncSingleTodoListAsync(TodoList todoList)
    {
        _logger.LogDebug("Syncing TodoList {TodoListId} '{Name}' to external API", 
            todoList.Id, todoList.Name);

        // Create the external DTO
        var createDto = new CreateExternalTodoList
        {
            SourceId = _externalApiClient.SourceId,
            Name = todoList.Name,
            Items = todoList.Items.Select(item => new CreateExternalTodoItem
            {
                SourceId = _externalApiClient.SourceId,
                Description = item.Description,
                Completed = item.IsCompleted
            }).ToList()
        };

        // Create in external API
        var externalTodoList = await _externalApiClient.CreateTodoListAsync(createDto);

        // Update local record with external ID
        todoList.ExternalId = externalTodoList.Id;
        todoList.LastModified = DateTime.UtcNow;

        // Update TodoItems with their external IDs
        foreach (var externalItem in externalTodoList.Items)
        {
            var localItem = todoList.Items.FirstOrDefault(item => item.Description == externalItem.Description);
            if (localItem != null)
            {
                localItem.ExternalId = externalItem.Id;
                localItem.LastModified = DateTime.UtcNow;
            }
        }

        // Save changes to database
        await _context.SaveChangesAsync();

        _logger.LogInformation("Successfully synced TodoList {TodoListId} '{Name}' with external ID '{ExternalId}'",
            todoList.Id, todoList.Name, externalTodoList.Id);
    }
}