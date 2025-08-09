using Microsoft.EntityFrameworkCore;
using TodoApi.Common;
using TodoApi.Dtos.External;
using TodoApi.Models;

namespace TodoApi.Services;

public class TodoSyncService : ISyncService
{
    private readonly TodoContext _context;
    private readonly IExternalTodoApiClient _externalApiClient;
    private readonly IConflictResolver _conflictResolver;
    private readonly IRetryPolicyService _retryPolicyService;
    private readonly IChangeDetectionService _changeDetectionService;
    private readonly ISyncStateService _syncStateService;
    private readonly ILogger<TodoSyncService> _logger;

    public TodoSyncService(
        TodoContext context,
        IExternalTodoApiClient externalApiClient,
        IConflictResolver conflictResolver,
        IRetryPolicyService retryPolicyService,
        IChangeDetectionService changeDetectionService,
        ISyncStateService syncStateService,
        ILogger<TodoSyncService> logger)
    {
        _context = context;
        _externalApiClient = externalApiClient;
        _conflictResolver = conflictResolver;
        _retryPolicyService = retryPolicyService;
        _changeDetectionService = changeDetectionService;
        _syncStateService = syncStateService;
        _logger = logger;
    }

    public async Task SyncTodoListsToExternalAsync()
    {
        _logger.LogInformation("Starting one-way sync of TodoLists to external API");

        try
        {
            // Find TodoLists that have pending changes or haven't been synced yet
            var pendingTodoLists = await _context.TodoList
                .Where(tl => tl.IsSyncPending || tl.ExternalId == null)
                .Include(tl => tl.Items) // Include items for complete sync
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
            // Determine if we can use delta sync
            var isDeltaSyncAvailable = await _syncStateService.IsDeltaSyncAvailableAsync();
            DateTime? sinceTimestamp = null;
            List<ExternalTodoList> externalTodoLists;

            if (isDeltaSyncAvailable)
            {
                sinceTimestamp = await _syncStateService.GetLastSyncTimestampAsync();
                _logger.LogInformation("Using delta sync (client-side) - fetching all TodoLists and filtering by {SinceTimestamp}", sinceTimestamp);
            }

            // Always fetch all, then filter locally when delta is available
            var allExternalLists = await _externalApiClient.GetTodoListsAsync();
            externalTodoLists = isDeltaSyncAvailable && sinceTimestamp.HasValue
                ? allExternalLists.Where(tl => tl.UpdatedAt >= sinceTimestamp.Value).ToList()
                : allExternalLists;

            if (externalTodoLists == null || externalTodoLists.Count == 0)
            {
                _logger.LogInformation("No TodoLists found in external API");
                return;
            }

            _logger.LogInformation("Found {Count} TodoLists in external API (delta sync: {IsDeltaSync})",
                externalTodoLists.Count, isDeltaSyncAvailable);

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

            // Update sync timestamp after successful sync
            if (createdCount > 0 || updatedCount > 0)
            {
                var syncTimestamp = DateTime.UtcNow;
                await _syncStateService.UpdateLastSyncTimestampAsync(syncTimestamp);
                _logger.LogInformation("Updated sync timestamp to {SyncTimestamp} after processing {TotalCount} entities",
                    syncTimestamp, createdCount + updatedCount);
            }

            _logger.LogInformation("Inbound sync completed. Created: {CreatedCount}, Updated: {UpdatedCount}, Failed: {FailedCount} (delta sync: {IsDeltaSync})",
                createdCount, updatedCount, failedCount, isDeltaSyncAvailable);
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
            var hasPendingChanges = await _changeDetectionService.HasPendingChangesAsync();
            var pendingCount = await _changeDetectionService.GetPendingChangesCountAsync();

            // Also check for any unsynced TodoLists (no ExternalId)
            var hasUnsyncedTodoLists = await _context.TodoList.AnyAsync(tl => tl.ExternalId == null);

            _logger.LogInformation("Sync check: HasPendingChanges={HasPendingChanges}, PendingCount={PendingCount}, HasUnsynced={HasUnsynced}",
                hasPendingChanges, pendingCount, hasUnsyncedTodoLists);

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

        // Update local record with external ID and sync timestamp
        var syncTime = DateTime.UtcNow;
        todoList.ExternalId = externalTodoList.Id;
        todoList.LastModified = syncTime;
        todoList.LastSyncedAt = syncTime;
        todoList.IsSyncPending = false; // Clear pending flag after successful sync

        // Update TodoItems with their external IDs and sync timestamps
        foreach (var externalItem in externalTodoList.Items)
        {
            var localItem = todoList.Items.FirstOrDefault(item => item.Description == externalItem.Description);
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
        var conflictInfo = _conflictResolver.ResolveTodoListConflict(localTodoList, externalTodoList);

        // Apply the resolution
        _conflictResolver.ApplyResolution(localTodoList, externalTodoList, conflictInfo);

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

        foreach (var externalItem in externalItems)
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
                var conflictInfo = _conflictResolver.ResolveTodoItemConflict(localItem, externalItem);

                // Apply the resolution
                _conflictResolver.ApplyResolution(localItem, externalItem, conflictInfo);

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

        return Task.FromResult(hasChanges);
    }
}
