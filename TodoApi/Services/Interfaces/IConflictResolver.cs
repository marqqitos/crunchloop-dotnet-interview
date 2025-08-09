using TodoApi.Common;
using TodoApi.Dtos.External;
using TodoApi.Models;

namespace TodoApi.Services;

/// <summary>
/// Service for resolving synchronization conflicts between local and external entities
/// </summary>
public interface IConflictResolver
{
    /// <summary>
    /// Resolves conflicts for TodoList entities using the configured strategy
    /// </summary>
    ConflictInfo ResolveTodoListConflict(
        TodoList localEntity,
        ExternalTodoList externalEntity,
        ConflictResolutionStrategy strategy = ConflictResolutionStrategy.ExternalWins);

    /// <summary>
    /// Resolves conflicts for TodoItem entities using the configured strategy
    /// </summary>
    ConflictInfo ResolveTodoItemConflict(
        TodoItem localEntity,
        ExternalTodoItem externalEntity,
        ConflictResolutionStrategy strategy = ConflictResolutionStrategy.ExternalWins);

    /// <summary>
    /// Applies the conflict resolution to the local entity
    /// </summary>
    void ApplyResolution(TodoList localEntity, ExternalTodoList externalEntity, ConflictInfo conflictInfo);

    /// <summary>
    /// Applies the conflict resolution to the local entity
    /// </summary>
    void ApplyResolution(TodoItem localEntity, ExternalTodoItem externalEntity, ConflictInfo conflictInfo);
}
