using Microsoft.EntityFrameworkCore;
using TodoApi.Common;
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

    public async Task SyncTodoListsFromExternalAsync()
    {
        _logger.LogInformation("Starting inbound sync of TodoLists from external API");

        try
        {
            // Get all TodoLists from external API
            var externalTodoLists = await _externalApiClient.GetTodoListsAsync();

            if (!externalTodoLists.Any())
            {
                _logger.LogInformation("No TodoLists found in external API");
                return;
            }

            _logger.LogInformation("Found {Count} TodoLists in external API", externalTodoLists.Count);

            var createdCount = 0;
            var updatedCount = 0;
            var failedCount = 0;

            foreach (var externalTodoList in externalTodoLists)
            {
                try
                {
                    var result = await SyncSingleTodoListFromExternalAsync(externalTodoList);
                    if (result.IsCreated)
                        createdCount++;
                    else if (result.IsUpdated)
                        updatedCount++;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to sync external TodoList '{ExternalId}' '{Name}'",
                        externalTodoList.Id, externalTodoList.Name);
                    failedCount++;
                }
            }

            _logger.LogInformation("Inbound sync completed. Created: {CreatedCount}, Updated: {UpdatedCount}, Failed: {FailedCount}",
                createdCount, updatedCount, failedCount);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to sync TodoLists from external API");
            throw;
        }
    }

    public async Task PerformFullSyncAsync()
    {
        _logger.LogInformation("Starting bidirectional sync (Local ↔ External)");

        try
        {
            // Phase 1: Push local changes to external API
            _logger.LogInformation("Phase 1: Syncing local changes to external API");
            await SyncTodoListsToExternalAsync();

            // Phase 2: Pull external changes to local database
            _logger.LogInformation("Phase 2: Syncing external changes to local database");
            await SyncTodoListsFromExternalAsync();

            _logger.LogInformation("Bidirectional sync completed successfully");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Bidirectional sync failed");
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

    private async Task<SyncResult> SyncSingleTodoListFromExternalAsync(ExternalTodoList externalTodoList)
    {
        _logger.LogDebug("Processing external TodoList '{ExternalId}' '{Name}'",
            externalTodoList.Id, externalTodoList.Name);

        // Check if we already have this TodoList locally
        var existingTodoList = await _context.TodoList
            .Include(tl => tl.Items)
            .FirstOrDefaultAsync(tl => tl.ExternalId == externalTodoList.Id);

        if (existingTodoList == null)
        {
            // Create new local TodoList from external data
            return await CreateLocalTodoListFromExternalAsync(externalTodoList);
        }
        else
        {
            // Update existing local TodoList if needed
            return await UpdateLocalTodoListFromExternalAsync(existingTodoList, externalTodoList);
        }
    }

    private async Task<SyncResult> CreateLocalTodoListFromExternalAsync(ExternalTodoList externalTodoList)
    {
        _logger.LogDebug("Creating new local TodoList from external '{ExternalId}' '{Name}'",
            externalTodoList.Id, externalTodoList.Name);

        var localTodoList = new TodoList
        {
            Name = externalTodoList.Name,
            ExternalId = externalTodoList.Id,
            LastModified = externalTodoList.UpdatedAt
        };

        // Create local TodoItems from external items
        foreach (var externalItem in externalTodoList.Items)
        {
            var localItem = new TodoItem
            {
                Description = externalItem.Description,
                IsCompleted = externalItem.Completed,
                ExternalId = externalItem.Id,
                LastModified = externalItem.UpdatedAt
            };
            localTodoList.Items.Add(localItem);
        }

        _context.TodoList.Add(localTodoList);
        await _context.SaveChangesAsync();

        _logger.LogInformation("Created local TodoList {LocalId} from external '{ExternalId}' '{Name}' with {ItemCount} items",
            localTodoList.Id, externalTodoList.Id, externalTodoList.Name, externalTodoList.Items.Count);

        return new SyncResult { IsCreated = true };
    }

    private async Task<SyncResult> UpdateLocalTodoListFromExternalAsync(TodoList localTodoList, ExternalTodoList externalTodoList)
    {
        var hasChanges = false;

        // Check if external TodoList has been updated since our last sync
        if (externalTodoList.UpdatedAt > localTodoList.LastModified)
        {
            _logger.LogDebug("External TodoList '{ExternalId}' has been updated, applying changes locally",
                externalTodoList.Id);

            localTodoList.Name = externalTodoList.Name;

            localTodoList.LastModified = externalTodoList.UpdatedAt;
            hasChanges = true;
        }

        // Sync TodoItems
        var itemChanges = await SyncTodoItemsFromExternalAsync(localTodoList, externalTodoList.Items);
        hasChanges = hasChanges || itemChanges;

        if (hasChanges)
        {
            await _context.SaveChangesAsync();
            _logger.LogInformation("Updated local TodoList {LocalId} from external changes", localTodoList.Id);
            return new SyncResult { IsUpdated = true };
        }

        return new SyncResult { IsUnchanged = true };
    }

    private Task<bool> SyncTodoItemsFromExternalAsync(TodoList localTodoList, IEnumerable<ExternalTodoItem> externalItems)
    {
        var hasChanges = false;

        foreach (var externalItem in externalItems)
        {
            // Find matching local item by ExternalId
            var localItem = localTodoList.Items.FirstOrDefault(item => item.ExternalId == externalItem.Id);

            if (localItem == null)
            {
                // Create new local TodoItem
                localItem = new TodoItem
                {
                    Description = externalItem.Description,
                    IsCompleted = externalItem.Completed,
                    ExternalId = externalItem.Id,
                    LastModified = externalItem.UpdatedAt,
                    TodoListId = localTodoList.Id
                };
                localTodoList.Items.Add(localItem);
                hasChanges = true;

                _logger.LogDebug("Created local TodoItem from external '{ExternalId}' '{Description}'",
                    externalItem.Id, externalItem.Description);
            }
            else if (externalItem.UpdatedAt > localItem.LastModified)
            {
                localItem.Description = externalItem.Description;
                localItem.IsCompleted = externalItem.Completed;
                localItem.LastModified = externalItem.UpdatedAt;
                hasChanges = true;

                _logger.LogDebug("Updated local TodoItem {LocalId} from external changes", localItem.Id);
            }
        }

        return Task.FromResult(hasChanges);
    }
}