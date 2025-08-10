using TodoApi.Common;
using TodoApi.Dtos.External;
using TodoApi.Models;
using TodoApi.Services.Factories.ConflictResolutionFactory;

namespace TodoApi.Services.ConflictResolver;

/// <summary>
/// Conflict resolver specifically for TodoList entities
/// </summary>
public class TodoListConflictResolver : ConflictResolverBase<TodoList, ExternalTodoList>
{
    public TodoListConflictResolver(ILogger<TodoListConflictResolver> logger, IConflictResolutionStrategyFactory<TodoList, ExternalTodoList> strategyFactory)
        : base(logger, strategyFactory)
    {
    }

    protected override ConflictInfo CreateConflictInfo(TodoList localEntity, ExternalTodoList externalEntity, ConflictResolutionStrategy strategy)
    {
        return new ConflictInfo
        {
            EntityType = nameof(TodoList),
            EntityId = localEntity.Id.ToString(),
            ExternalEntityId = externalEntity.Id.ToString(),
            LocalLastModified = localEntity.LastModified,
            ExternalLastModified = externalEntity.UpdatedAt,
            LastSyncedAt = localEntity.LastSyncedAt,
            Resolution = strategy
        };
    }

    protected override void DetectModifiedFields(TodoList localEntity, ExternalTodoList externalEntity, ConflictInfo conflictInfo)
    {
        if (localEntity.Name != externalEntity.Name)
            conflictInfo.ModifiedFields.Add(nameof(TodoList.Name));
    }

    protected override DateTime GetLocalLastModified(TodoList localEntity)
    {
        return localEntity.LastModified;
    }

    protected override DateTime GetExternalLastModified(ExternalTodoList externalEntity)
    {
        return externalEntity.UpdatedAt;
    }

    protected override string GetEntityId(TodoList localEntity)
    {
        return localEntity.Id.ToString();
    }

    protected override string GetExternalEntityId(ExternalTodoList externalEntity)
    {
        return externalEntity.Id.ToString();
    }

    protected override void ApplyExternalChanges(TodoList localEntity, ExternalTodoList externalEntity)
    {
        localEntity.Name = externalEntity.Name;
        localEntity.LastModified = externalEntity.UpdatedAt;
        localEntity.LastSyncedAt = DateTime.UtcNow;
    }

    protected override void UpdateSyncTimestamp(TodoList localEntity)
    {
        localEntity.LastSyncedAt = DateTime.UtcNow;
    }

    protected override void UpdateLastModified(TodoList localEntity, DateTime newLastModified)
    {
        localEntity.LastModified = newLastModified;
    }
}
