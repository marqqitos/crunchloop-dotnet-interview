using TodoApi.Common;

namespace TodoApi.Services.ConflictResolver;

/// <summary>
/// Generic service for resolving synchronization conflicts between local and external entities
/// </summary>
/// <typeparam name="TLocal">The local entity type</typeparam>
/// <typeparam name="TExternal">The external entity type</typeparam>
public interface IConflictResolver<TLocal, TExternal>
{
    /// <summary>
    /// Resolves conflicts between local and external entities using the configured strategy
    /// </summary>
    ConflictInfo ResolveConflict(
        TLocal localEntity,
        TExternal externalEntity,
        ConflictResolutionStrategy strategy = ConflictResolutionStrategy.ExternalWins);

    /// <summary>
    /// Applies the conflict resolution to the local entity
    /// </summary>
    void ApplyResolution(TLocal localEntity, TExternal externalEntity, ConflictInfo conflictInfo);
}
