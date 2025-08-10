using TodoApi.Common;

namespace TodoApi.Services.ConflictResolutionStrategies;

/// <summary>
/// Strategy interface for resolving conflicts between local and external entities
/// </summary>
/// <typeparam name="TLocal">The local entity type</typeparam>
/// <typeparam name="TExternal">The external entity type</typeparam>
public interface IConflictResolutionStrategy<TLocal, TExternal>
{
    /// <summary>
    /// Gets the strategy type this implementation handles
    /// </summary>
    ConflictResolutionStrategy StrategyType { get; }

    /// <summary>
    /// Gets the resolution reason for a conflict using this strategy
    /// </summary>
    /// <param name="localEntity">The local entity</param>
    /// <param name="externalEntity">The external entity</param>
    /// <param name="conflictInfo">Information about the detected conflict</param>
    /// <returns>The reason for the resolution</returns>
    string GetResolutionReason(TLocal localEntity, TExternal externalEntity, ConflictInfo conflictInfo);

    /// <summary>
    /// Determines whether this strategy should apply external changes
    /// </summary>
    /// <param name="localEntity">The local entity</param>
    /// <param name="externalEntity">The external entity</param>
    /// <param name="conflictInfo">Information about the detected conflict</param>
    /// <returns>True if external changes should be applied, false otherwise</returns>
    bool ShouldApplyExternalChanges(TLocal localEntity, TExternal externalEntity, ConflictInfo conflictInfo);
}
