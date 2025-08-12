using Microsoft.EntityFrameworkCore;
using TodoApi.Common;
using TodoApi.Dtos.External;
using TodoApi.Models;
using TodoApi.Services.ConflictResolver;
using TodoApi.Services.ExternalTodoApiClient;
using TodoApi.Services.RetryPolicyService;
using TodoApi.Services.SyncStateService;
using TodoApi.Services.TodoItemService;
using TodoApi.Services.TodoListService;

namespace TodoApi.Services.SyncService;

public class TodoListSyncService : ISyncService
{
    private readonly TodoContext _context;
    private readonly IExternalTodoApiClient _externalApiClient;
    private readonly IConflictResolver<TodoList, ExternalTodoList> _todoListConflictResolver;
    private readonly IConflictResolver<TodoItem, ExternalTodoItem> _todoItemConflictResolver;
    private readonly IRetryPolicyService _retryPolicyService;
    private readonly ITodoListService _todoListService;
	private readonly ITodoItemService _todoItemService;
    private readonly ISyncStateService _syncStateService;
    private readonly ILogger<TodoListSyncService> _logger;

    public TodoListSyncService(
        TodoContext context,
        IExternalTodoApiClient externalApiClient,
        IConflictResolver<TodoList, ExternalTodoList> todoListConflictResolver,
        IConflictResolver<TodoItem, ExternalTodoItem> todoItemConflictResolver,
        IRetryPolicyService retryPolicyService,
        ITodoListService todoListService,
		ITodoItemService todoItemService,
        ISyncStateService syncStateService,
        ILogger<TodoListSyncService> logger)
    {
        _context = context;
        _externalApiClient = externalApiClient;
        _todoListConflictResolver = todoListConflictResolver;
        _todoItemConflictResolver = todoItemConflictResolver;
        _retryPolicyService = retryPolicyService;
        _todoListService = todoListService;
		_todoItemService = todoItemService;
        _syncStateService = syncStateService;
        _logger = logger;
    }

    public async Task SyncTodoListsToExternalAsync()
    {
        _logger.LogInformation("Starting one-way sync of TodoLists to external API");

        try
        {
            // Find TodoLists that have pending changes or haven't been synced yet
            // Include both active and deleted items that need syncing
            var pendingTodoLists = await _context.TodoList
                .Where(tl => tl.IsSyncPending || tl.ExternalId == null)
                .Include(tl => tl.Items.Where(item => item.IsSyncPending)) // Include items that need syncing (including deleted)
                .ToListAsync();

            if (!pendingTodoLists.Any())
            {
                _logger.LogInformation("No unsynced TodoLists found");
                return;
            }

            _logger.LogInformation("Found {Count} TodoLists with pending changes to sync", pendingTodoLists.Count);

            var syncedCount = 0;
            var failedCount = 0;

            var syncRetryPolicy = _retryPolicyService.GetSyncRetryPolicy();

            foreach (var todoList in pendingTodoLists)
            {
                try
                {
                    await syncRetryPolicy.ExecuteAsync(async cancellationToken =>
                    {
                        await SyncSingleTodoListAsync(todoList);
                    });
                    syncedCount++;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to sync TodoList {TodoListId} '{Name}' after retries",
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
			var externalTodoLists = await _externalApiClient.GetTodoListsPendingSync();

			if (externalTodoLists == null || externalTodoLists.Count == 0)
			{
				_logger.LogInformation("No TodoLists found in external API");
				return;
			}

			_logger.LogInformation("Found {Count} TodoLists in external API",
				externalTodoLists.Count);

			var createdCount = 0;
			var updatedCount = 0;
			var deletedCount = 0;
			var failedCount = 0;

			// Process existing external lists
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

			// Detect and handle external deletions
			deletedCount = await DetectAndHandleExternalDeletionsAsync(externalTodoLists);

			// Update sync timestamp after successful sync
			if (createdCount > 0 || updatedCount > 0 || deletedCount > 0)
			{
				var syncTimestamp = DateTime.UtcNow;
				await _syncStateService.UpdateLastSyncTimestampAsync(syncTimestamp);
				_logger.LogInformation("Updated sync timestamp to {SyncTimestamp} after processing {TotalCount} entities",
					syncTimestamp, createdCount + updatedCount + deletedCount);
			}

			_logger.LogInformation("Inbound sync completed. Created: {CreatedCount}, Updated: {UpdatedCount}, Deleted: {DeletedCount}, Failed: {FailedCount}",
				createdCount, updatedCount, deletedCount, failedCount);
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
            // Check if there are any pending changes before starting sync
            var todoListPendingChangesCount = await _todoListService.GetPendingChangesCountAsync();
            var todoItemPendingChangesCount = await _todoItemService.GetPendingChangesCountAsync();

			var hasPendingChanges = todoListPendingChangesCount > 0 || todoItemPendingChangesCount > 0;

            // Also check for any unsynced TodoLists (no ExternalId) that aren't deleted
            var hasUnsyncedTodoLists = await _context.TodoList.AnyAsync(tl => tl.ExternalId == null && !tl.IsDeleted);

            _logger.LogInformation("Sync check: HasPendingChanges={HasPendingChanges}, PendingCount={PendingCount}, HasUnsynced={HasUnsynced}",
                hasPendingChanges, todoListPendingChangesCount, hasUnsyncedTodoLists);

            // Phase 1: Push local changes to external API when there are pending changes OR unsynced lists
            if (hasPendingChanges || hasUnsyncedTodoLists)
            {
                _logger.LogInformation("Phase 1: Syncing local changes/unsynced lists to external API");
                await SyncTodoListsToExternalAsync();
            }
            else
            {
                _logger.LogInformation("Phase 1: Skipping local sync - no pending changes or unsynced lists");
            }

            // Phase 2: Pull external changes to local database (always check for external changes)
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

        // Check if this is a deletion
        if (todoList.IsDeleted && !string.IsNullOrEmpty(todoList.ExternalId))
        {
            _logger.LogInformation("Deleting TodoList {TodoListId} '{Name}' from external API", todoList.Id, todoList.Name);
            
            try
            {
                await _externalApiClient.DeleteTodoListAsync(todoList.ExternalId);
                _logger.LogInformation("Successfully deleted TodoList {TodoListId} from external API", todoList.Id);
            }
            catch (HttpRequestException ex) when (ex.Message.Contains("404") || ex.Message.Contains("NotFound"))
            {
                _logger.LogInformation("TodoList {TodoListId} not found in external API (404) - treating as successful deletion", todoList.Id);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to delete TodoList {TodoListId} from external API", todoList.Id);
                throw; // Re-throw other exceptions to trigger retry
            }
            
            // Mark as synced and clean up
            todoList.IsSyncPending = false;
            todoList.LastSyncedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync();
            return;
        }

        // Handle new TodoList creation
        if (string.IsNullOrEmpty(todoList.ExternalId))
        {
            // Create the external DTO
            var createDto = new CreateExternalTodoList
            {
                SourceId = _externalApiClient.SourceId,
                Name = todoList.Name,
                Items = todoList.Items.Where(item => !item.IsDeleted).Select(item => new CreateExternalTodoItem
                {
                    SourceId = _externalApiClient.SourceId,
                    Description = item.Description,
                    Completed = item.IsCompleted
                }).ToList()
            };

            // Create in external API
            var externalTodoList = await _externalApiClient.CreateTodoListAsync(createDto);

            // Update local record with external ID and sync timestamp
            var syncTime = DateTime.UtcNow;
            todoList.ExternalId = externalTodoList.Id;
            todoList.LastModified = syncTime;
            todoList.LastSyncedAt = syncTime;
            todoList.IsSyncPending = false; // Clear pending flag after successful sync

            // Update TodoItems with their external IDs and sync timestamps
            foreach (var externalItem in externalTodoList.Items)
            {
                var localItem = todoList.Items.FirstOrDefault(item => item.Description == externalItem.Description && !item.IsDeleted);
                if (localItem != null)
                {
                    localItem.ExternalId = externalItem.Id;
                    localItem.LastModified = syncTime;
                    localItem.LastSyncedAt = syncTime;
                    localItem.IsSyncPending = false; // Clear pending flag after successful sync
                }
            }

            // Save changes to database with retry logic
            var databaseRetryPolicy = _retryPolicyService.GetDatabaseRetryPolicy();
            await databaseRetryPolicy.ExecuteAsync(async cancellationToken =>
            {
                await _context.SaveChangesAsync(cancellationToken);
            });

            _logger.LogInformation("Successfully synced TodoList {TodoListId} '{Name}' with external ID '{ExternalId}'",
                todoList.Id, todoList.Name, externalTodoList.Id);
            return;
        }

        // Handle existing TodoList updates and item-level changes
        if (!string.IsNullOrEmpty(todoList.ExternalId))
        {
            await SyncTodoListItemChangesAsync(todoList);
        }
    }

    private async Task SyncTodoListItemChangesAsync(TodoList todoList)
    {
        _logger.LogDebug("Syncing item changes for TodoList {TodoListId}", todoList.Id);

        // Handle individual item deletions first
        var deletedItems = todoList.Items.Where(item => item.IsDeleted && !string.IsNullOrEmpty(item.ExternalId)).ToList();
        foreach (var deletedItem in deletedItems)
        {
            _logger.LogInformation("Deleting TodoItem {TodoItemId} from external API", deletedItem.Id);
            
            try
            {
                await _externalApiClient.DeleteTodoItemAsync(todoList.ExternalId!, deletedItem.ExternalId!);
                _logger.LogInformation("Successfully deleted TodoItem {TodoItemId} from external API", deletedItem.Id);
            }
            catch (HttpRequestException ex) when (ex.Message.Contains("404") || ex.Message.Contains("NotFound"))
            {
                _logger.LogInformation("TodoItem {TodoItemId} not found in external API (404) - treating as successful deletion", deletedItem.Id);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to delete TodoItem {TodoItemId} from external API", deletedItem.Id);
                throw; // Re-throw other exceptions to trigger retry
            }
            
            // Mark as synced
            deletedItem.IsSyncPending = false;
            deletedItem.LastSyncedAt = DateTime.UtcNow;
        }

        // Handle item updates (for now, just mark them as synced - full item sync logic can be added later)
        var updatedItems = todoList.Items.Where(item => !item.IsDeleted && item.IsSyncPending && !string.IsNullOrEmpty(item.ExternalId)).ToList();
        foreach (var updatedItem in updatedItems)
        {
            // TODO: Implement individual item updates if needed
            // For now, just clear the pending flag as the full list sync will handle updates
            updatedItem.IsSyncPending = false;
            updatedItem.LastSyncedAt = DateTime.UtcNow;
        }

        // Clear TodoList pending flag if all items are synced
        if (todoList.Items.All(item => !item.IsSyncPending))
        {
            todoList.IsSyncPending = false;
            todoList.LastSyncedAt = DateTime.UtcNow;
        }

        await _context.SaveChangesAsync();
    }

    private async Task<SyncResult> SyncSingleTodoListFromExternalAsync(ExternalTodoList externalTodoList)
    {
        _logger.LogDebug("Processing external TodoList '{ExternalId}' '{Name}'",
            externalTodoList.Id, externalTodoList.Name);

        // Check if we already have this TodoList locally (including soft-deleted ones for potential restoration)
        var existingTodoList = await _context.TodoList
            .Include(tl => tl.Items)
            .FirstOrDefaultAsync(tl => tl.ExternalId == externalTodoList.Id);

        if (existingTodoList == null)
        {
            // Create new local TodoList from external data
            return await CreateLocalTodoListFromExternalAsync(externalTodoList);
        }
        else if (existingTodoList.IsDeleted)
        {
            // Restore soft-deleted TodoList
            _logger.LogInformation("Restoring soft-deleted TodoList {LocalId} '{Name}' from external data", 
                existingTodoList.Id, existingTodoList.Name);
            
            var now = DateTime.UtcNow;
            existingTodoList.IsDeleted = false;
            existingTodoList.DeletedAt = null;
            existingTodoList.Name = externalTodoList.Name;
            existingTodoList.LastModified = externalTodoList.UpdatedAt;
            existingTodoList.LastSyncedAt = now;

            // Also restore any items that exist in external
            foreach (var externalItem in externalTodoList.Items)
            {
                var localItem = existingTodoList.Items.FirstOrDefault(item => item.ExternalId == externalItem.Id);
                if (localItem != null && localItem.IsDeleted)
                {
                    localItem.IsDeleted = false;
                    localItem.DeletedAt = null;
                    localItem.Description = externalItem.Description;
                    localItem.IsCompleted = externalItem.Completed;
                    localItem.LastModified = externalItem.UpdatedAt;
                    localItem.LastSyncedAt = now;
                }
            }

            await _context.SaveChangesAsync();
            return SyncResult.Updated();
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

        var syncTime = DateTime.UtcNow;
        var localTodoList = new TodoList
        {
            Name = externalTodoList.Name,
            ExternalId = externalTodoList.Id,
            LastModified = externalTodoList.UpdatedAt,
            LastSyncedAt = syncTime
        };

        // Create local TodoItems from external items
        foreach (var externalItem in externalTodoList.Items)
        {
            var localItem = new TodoItem
            {
                Description = externalItem.Description,
                IsCompleted = externalItem.Completed,
                ExternalId = externalItem.Id,
                LastModified = externalItem.UpdatedAt,
                LastSyncedAt = syncTime
            };
            localTodoList.Items.Add(localItem);
        }

        _context.TodoList.Add(localTodoList);
        await _context.SaveChangesAsync();

        _logger.LogInformation("Created local TodoList {LocalId} from external '{ExternalId}' '{Name}' with {ItemCount} items",
            localTodoList.Id, externalTodoList.Id, externalTodoList.Name, externalTodoList.Items.Count);

        return SyncResult.Created();
    }

    private async Task<SyncResult> UpdateLocalTodoListFromExternalAsync(TodoList localTodoList, ExternalTodoList externalTodoList)
    {
        // Use conflict resolver to determine what to do
        var conflictInfo = _todoListConflictResolver.ResolveConflict(localTodoList, externalTodoList);

        // Apply the resolution
        _todoListConflictResolver.ApplyResolution(localTodoList, externalTodoList, conflictInfo);

        // Sync TodoItems with conflict resolution
        var itemChanges = await SyncTodoItemsFromExternalAsync(localTodoList, externalTodoList.Items);

        // Always save changes (even if just updating LastSyncedAt)
        await _context.SaveChangesAsync();

        if (conflictInfo.ConflictResolved)
        {
            _logger.LogInformation("Updated local TodoList {LocalId} from external changes - conflict resolved", localTodoList.Id);
            return SyncResult.WithConflictResolution(conflictInfo.ResolutionReason ?? "External wins conflict resolution");
        }
        else if (conflictInfo.HasConflict || externalTodoList.UpdatedAt > localTodoList.LastModified || itemChanges)
        {
            _logger.LogInformation("Updated local TodoList {LocalId} from external changes", localTodoList.Id);
            return SyncResult.Updated();
        }

        return SyncResult.Unchanged();
    }

    private Task<bool> SyncTodoItemsFromExternalAsync(TodoList localTodoList, IEnumerable<ExternalTodoItem> externalItems)
    {
        var hasChanges = false;
        var externalItemsList = externalItems.ToList();

        foreach (var externalItem in externalItemsList)
        {
            // Find matching local item by ExternalId
            var localItem = localTodoList.Items.FirstOrDefault(item => item.ExternalId == externalItem.Id);

            if (localItem == null)
            {
                // Create new local TodoItem
                var syncTime = DateTime.UtcNow;
                localItem = new TodoItem
                {
                    Description = externalItem.Description,
                    IsCompleted = externalItem.Completed,
                    ExternalId = externalItem.Id,
                    LastModified = externalItem.UpdatedAt,
                    LastSyncedAt = syncTime,
                    TodoListId = localTodoList.Id
                };
                localTodoList.Items.Add(localItem);
                hasChanges = true;

                _logger.LogDebug("Created local TodoItem from external '{ExternalId}' '{Description}'",
                    externalItem.Id, externalItem.Description);
            }
            else
            {
                // Use conflict resolver to determine what to do
                var conflictInfo = _todoItemConflictResolver.ResolveConflict(localItem, externalItem);

                // Apply the resolution
                _todoItemConflictResolver.ApplyResolution(localItem, externalItem, conflictInfo);

                if (conflictInfo.ConflictResolved || conflictInfo.HasConflict || externalItem.UpdatedAt > localItem.LastModified)
                {
                    hasChanges = true;

                    if (conflictInfo.ConflictResolved)
                    {
                        _logger.LogDebug("Conflict resolved for TodoItem {LocalId} - {Reason}",
                            localItem.Id, conflictInfo.ResolutionReason);
                    }
                    else
                    {
                        _logger.LogDebug("Updated local TodoItem {LocalId} from external changes", localItem.Id);
                    }
                }
            }
        }

        // Detect local items that are missing from external (deleted externally)
        var externalItemIds = externalItemsList.Select(ei => ei.Id).ToHashSet();
        var deletedLocalItems = localTodoList.Items
            .Where(item => !item.IsDeleted && !string.IsNullOrEmpty(item.ExternalId) && !externalItemIds.Contains(item.ExternalId))
            .ToList();

        foreach (var deletedItem in deletedLocalItems)
        {
            _logger.LogInformation("Detected external deletion of TodoItem {LocalId} '{Description}' (ExternalId: {ExternalId})",
                deletedItem.Id, deletedItem.Description, deletedItem.ExternalId);
            
            // Soft delete the locally orphaned item
            var now = DateTime.UtcNow;
            deletedItem.IsDeleted = true;
            deletedItem.DeletedAt = now;
            deletedItem.LastModified = now;
            deletedItem.LastSyncedAt = now;
            hasChanges = true;
        }

        return Task.FromResult(hasChanges);
    }

    private async Task<int> DetectAndHandleExternalDeletionsAsync(List<ExternalTodoList> externalTodoLists)
    {
        _logger.LogDebug("Detecting external deletions");

        var deletedCount = 0;
        var externalListIds = externalTodoLists.Select(etl => etl.Id).ToHashSet();

        // Find local TodoLists that have ExternalId but are missing from external response
        var orphanedLocalLists = await _context.TodoList
            .Where(tl => !tl.IsDeleted && !string.IsNullOrEmpty(tl.ExternalId) && !externalListIds.Contains(tl.ExternalId))
            .Include(tl => tl.Items.Where(item => !item.IsDeleted))
            .ToListAsync();

        foreach (var orphanedList in orphanedLocalLists)
        {
            _logger.LogInformation("Detected external deletion of TodoList {LocalId} '{Name}' (ExternalId: {ExternalId})",
                orphanedList.Id, orphanedList.Name, orphanedList.ExternalId);
            
            // Soft delete the locally orphaned list and all its items
            var now = DateTime.UtcNow;
            orphanedList.IsDeleted = true;
            orphanedList.DeletedAt = now;
            orphanedList.LastModified = now;
            orphanedList.LastSyncedAt = now;

            // Also soft delete all items in the list
            foreach (var item in orphanedList.Items.Where(i => !i.IsDeleted))
            {
                item.IsDeleted = true;
                item.DeletedAt = now;
                item.LastModified = now;
                item.LastSyncedAt = now;
            }

            deletedCount++;
        }

        if (deletedCount > 0)
        {
            await _context.SaveChangesAsync();
            _logger.LogInformation("Soft deleted {DeletedCount} TodoLists that were deleted externally", deletedCount);
        }

        return deletedCount;
    }
}
