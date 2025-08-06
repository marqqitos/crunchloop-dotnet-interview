using TodoApi.Common;
using TodoApi.Dtos.External;
using TodoApi.Models;

namespace TodoApi.Services;

/// <summary>
/// Implementation of conflict resolution for synchronization operations
/// </summary>
public class ConflictResolver : IConflictResolver
{
	private readonly ILogger<ConflictResolver> _logger;

	public ConflictResolver(ILogger<ConflictResolver> logger)
	{
		_logger = logger;
	}

	public ConflictInfo ResolveTodoListConflict(
		TodoList localEntity,
		ExternalTodoList externalEntity,
		ConflictResolutionStrategy strategy = ConflictResolutionStrategy.ExternalWins)
	{
		var conflictInfo = new ConflictInfo
		{
			EntityType = nameof(TodoList),
			EntityId = localEntity.Id.ToString(),
			LocalLastModified = localEntity.LastModified,
			ExternalLastModified = externalEntity.UpdatedAt,
			LastSyncedAt = localEntity.LastSyncedAt,
			Resolution = strategy
		};

		var resolutionReason = string.Empty;

		if (localEntity.Name != externalEntity.Name)
			conflictInfo.ModifiedFields.Add(nameof(TodoList.Name));

		// Detect if there's a conflict
		if (conflictInfo.HasConflict)
		{
			// Apply resolution strategy
			switch (strategy)
			{
				case ConflictResolutionStrategy.ExternalWins:
					resolutionReason =
						$"External API changes take precedence. Local changes made at {localEntity.LastModified:yyyy-MM-dd HH:mm:ss} " +
						$"will be overwritten by external changes made at {externalEntity.UpdatedAt:yyyy-MM-dd HH:mm:ss}.";

					_logger.LogWarning("CONFLICT DETECTED: TodoList {LocalId} (External: {ExternalId}) - " +
						"Both local and external modified since last sync. Resolution: External Wins. " +
						"Fields in conflict: {Fields}",
						localEntity.Id, externalEntity.Id, string.Join(", ", conflictInfo.ModifiedFields));
					break;

				case ConflictResolutionStrategy.LocalWins:
					resolutionReason =
						$"Local changes take precedence. External changes made at {externalEntity.UpdatedAt:yyyy-MM-dd HH:mm:ss} " +
						$"will be ignored in favor of local changes made at {localEntity.LastModified:yyyy-MM-dd HH:mm:ss}.";
					break;

				case ConflictResolutionStrategy.Manual:
					resolutionReason = "Manual resolution required - conflict detected but no automatic resolution applied.";
					throw new InvalidOperationException(
						$"Manual conflict resolution required for TodoList {localEntity.Id}. " +
						$"Local: {localEntity.LastModified:yyyy-MM-dd HH:mm:ss}, " +
						$"External: {externalEntity.UpdatedAt:yyyy-MM-dd HH:mm:ss}, " +
						$"Last Sync: {localEntity.LastSyncedAt:yyyy-MM-dd HH:mm:ss}");
			}
		}
		else
		{
			// No conflict - determine which is newer
			resolutionReason = externalEntity.UpdatedAt > localEntity.LastModified
				? "External entity is newer - applying external changes."
				: "Local entity is newer or same - no changes needed.";
		}

		conflictInfo.ResolutionReason = resolutionReason;
		return conflictInfo;
	}

	public ConflictInfo ResolveTodoItemConflict(
		TodoItem localEntity,
		ExternalTodoItem externalEntity,
		ConflictResolutionStrategy strategy = ConflictResolutionStrategy.ExternalWins)
	{
		var conflictInfo = new ConflictInfo
		{
			EntityType = nameof(TodoItem),
			EntityId = localEntity.Id.ToString(),
			LocalLastModified = localEntity.LastModified,
			ExternalLastModified = externalEntity.UpdatedAt,
			LastSyncedAt = localEntity.LastSyncedAt,
			Resolution = strategy
		};

		var resolutionReason = string.Empty;

		if (localEntity.Description != externalEntity.Description)
			conflictInfo.ModifiedFields.Add(nameof(TodoItem.Description));

		if (localEntity.IsCompleted != externalEntity.Completed)
			conflictInfo.ModifiedFields.Add(nameof(TodoItem.IsCompleted));

		// Detect if there's a conflict
		if (conflictInfo.HasConflict)
		{
			// Apply resolution strategy
			switch (strategy)
			{
				case ConflictResolutionStrategy.ExternalWins:
					resolutionReason =
						$"External API changes take precedence. Local changes made at {localEntity.LastModified:yyyy-MM-dd HH:mm:ss} " +
						$"will be overwritten by external changes made at {externalEntity.UpdatedAt:yyyy-MM-dd HH:mm:ss}.";

					_logger.LogWarning("CONFLICT DETECTED: TodoItem {LocalId} (External: {ExternalId}) - " +
						"Both local and external modified since last sync. Resolution: External Wins. " +
						"Fields in conflict: {Fields}",
						localEntity.Id, externalEntity.Id, string.Join(", ", conflictInfo.ModifiedFields));
					break;

				case ConflictResolutionStrategy.LocalWins:
					resolutionReason =
						$"Local changes take precedence. External changes made at {externalEntity.UpdatedAt:yyyy-MM-dd HH:mm:ss} " +
						$"will be ignored in favor of local changes made at {localEntity.LastModified:yyyy-MM-dd HH:mm:ss}.";
					break;

				case ConflictResolutionStrategy.Manual:
					resolutionReason = "Manual resolution required - conflict detected but no automatic resolution applied.";
					throw new InvalidOperationException(
						$"Manual conflict resolution required for TodoItem {localEntity.Id}. " +
						$"Local: {localEntity.LastModified:yyyy-MM-dd HH:mm:ss}, " +
						$"External: {externalEntity.UpdatedAt:yyyy-MM-dd HH:mm:ss}, " +
						$"Last Sync: {localEntity.LastSyncedAt:yyyy-MM-dd HH:mm:ss}");
			}
		}
		else
		{
			// No conflict - determine which is newer
			if (externalEntity.UpdatedAt > localEntity.LastModified)
			{
				resolutionReason = "External entity is newer - applying external changes.";
			}
			else
			{
				resolutionReason = "Local entity is newer or same - no changes needed.";
			}
		}

		conflictInfo.ResolutionReason = resolutionReason;
		return conflictInfo;
	}

	public void ApplyResolution(TodoList localEntity, ExternalTodoList externalEntity, ConflictInfo conflictInfo)
	{
		if (conflictInfo.Resolution == ConflictResolutionStrategy.LocalWins && conflictInfo.HasConflict)
		{
			// Local wins - don't apply external changes
			_logger.LogInformation("Conflict resolved: Local wins for TodoList {LocalId} - keeping local changes",
				localEntity.Id);
			return;
		}

		// Apply external changes (default for ExternalWins or no conflict with external newer)
		if (conflictInfo.HasConflict || externalEntity.UpdatedAt > localEntity.LastModified)
		{
			localEntity.Name = externalEntity.Name;
			localEntity.LastModified = externalEntity.UpdatedAt;
			localEntity.LastSyncedAt = DateTime.UtcNow;

			if (conflictInfo.HasConflict)
			{
				_logger.LogInformation("Conflict resolved: External wins for TodoList {LocalId} - applied external changes. Reason: {Reason}",
					localEntity.Id, conflictInfo.ResolutionReason);
			}
			else
			{
				_logger.LogDebug("No conflict: Applied external changes to TodoList {LocalId}", localEntity.Id);
			}
		}
		else
		{
			// Just update sync timestamp
			localEntity.LastSyncedAt = DateTime.UtcNow;
		}
	}

	public void ApplyResolution(TodoItem localEntity, ExternalTodoItem externalEntity, ConflictInfo conflictInfo)
	{
		if (conflictInfo.Resolution == ConflictResolutionStrategy.LocalWins && conflictInfo.HasConflict)
		{
			// Local wins - don't apply external changes
			_logger.LogInformation("Conflict resolved: Local wins for TodoItem {LocalId} - keeping local changes",
				localEntity.Id);
			return;
		}

		// Apply external changes (default for ExternalWins or no conflict with external newer)
		if (conflictInfo.HasConflict || externalEntity.UpdatedAt > localEntity.LastModified)
		{
			localEntity.Description = externalEntity.Description;
			localEntity.IsCompleted = externalEntity.Completed;
			localEntity.LastModified = externalEntity.UpdatedAt;
			localEntity.LastSyncedAt = DateTime.UtcNow;

			if (conflictInfo.HasConflict)
			{
				_logger.LogInformation("Conflict resolved: External wins for TodoItem {LocalId} - applied external changes. Reason: {Reason}",
					localEntity.Id, conflictInfo.ResolutionReason);
			}
			else
			{
				_logger.LogDebug("No conflict: Applied external changes to TodoItem {LocalId}", localEntity.Id);
			}
		}
		else
		{
			// Just update sync timestamp
			localEntity.LastSyncedAt = DateTime.UtcNow;
		}
	}
}
