using TodoApi.Common;
using TodoApi.Dtos.External;
using TodoApi.Models;

namespace TodoApi.Services.ConflictResolver;

/// <summary>
/// Conflict resolver specifically for TodoItem entities
/// </summary>
public class TodoItemConflictResolver : ConflictResolverBase<TodoItem, ExternalTodoItem>
{
    public TodoItemConflictResolver(ILogger<TodoItemConflictResolver> logger) : base(logger)
    {
    }

    protected override ConflictInfo CreateConflictInfo(TodoItem localEntity, ExternalTodoItem externalEntity, ConflictResolutionStrategy strategy)
    {
        return new ConflictInfo
        {
            EntityType = nameof(TodoItem),
            EntityId = localEntity.Id.ToString(),
            LocalLastModified = localEntity.LastModified,
            ExternalLastModified = externalEntity.UpdatedAt,
            LastSyncedAt = localEntity.LastSyncedAt,
            Resolution = strategy
        };
    }

    protected override void DetectModifiedFields(TodoItem localEntity, ExternalTodoItem externalEntity, ConflictInfo conflictInfo)
    {
        if (localEntity.Description != externalEntity.Description)
            conflictInfo.ModifiedFields.Add(nameof(TodoItem.Description));

        if (localEntity.IsCompleted != externalEntity.Completed)
            conflictInfo.ModifiedFields.Add(nameof(TodoItem.IsCompleted));
    }

	protected override DateTime GetLocalLastModified(TodoItem localEntity) => localEntity.LastModified;

	protected override DateTime GetExternalLastModified(ExternalTodoItem externalEntity) => externalEntity.UpdatedAt;

	protected override string GetEntityId(TodoItem localEntity) => localEntity.Id.ToString();

	protected override string GetExternalEntityId(ExternalTodoItem externalEntity) => externalEntity.Id.ToString();

	protected override void UpdateSyncTimestamp(TodoItem localEntity) => localEntity.LastSyncedAt = DateTime.UtcNow;

    protected override void ApplyExternalChanges(TodoItem localEntity, ExternalTodoItem externalEntity)
    {
        localEntity.Description = externalEntity.Description;
        localEntity.IsCompleted = externalEntity.Completed;
        localEntity.LastModified = externalEntity.UpdatedAt;
        localEntity.LastSyncedAt = DateTime.UtcNow;
    }

}
