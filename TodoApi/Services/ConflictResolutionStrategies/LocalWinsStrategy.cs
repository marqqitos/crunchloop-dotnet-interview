using TodoApi.Common;

namespace TodoApi.Services.ConflictResolutionStrategies;

/// <summary>
/// Strategy that always gives precedence to local changes
/// </summary>
/// <typeparam name="TLocal">The local entity type</typeparam>
/// <typeparam name="TExternal">The external entity type</typeparam>
public class LocalWinsStrategy<TLocal, TExternal> : IConflictResolutionStrategy<TLocal, TExternal>
    where TLocal : class
    where TExternal : class
{
    private readonly ILogger _logger;

    public LocalWinsStrategy(ILogger logger)
    {
        _logger = logger;
    }

    public ConflictResolutionStrategy StrategyType => ConflictResolutionStrategy.LocalWins;

    public string GetResolutionReason(TLocal localEntity, TExternal externalEntity, ConflictInfo conflictInfo)
    {
        var resolutionReason = $"Local changes take precedence. External changes made at {conflictInfo.ExternalLastModified:yyyy-MM-dd HH:mm:ss} " +
                              $"will be ignored in favor of local changes made at {conflictInfo.LocalLastModified:yyyy-MM-dd HH:mm:ss}.";

        _logger.LogWarning("CONFLICT DETECTED: {EntityType} {LocalId} (External: {ExternalId}) - " +
                          "Both local and external modified since last sync. Resolution: Local Wins. " +
                          "Fields in conflict: {Fields}",
                          conflictInfo.EntityType, conflictInfo.EntityId, conflictInfo.ExternalEntityId,
                          string.Join(", ", conflictInfo.ModifiedFields));

        return resolutionReason;
    }

	public bool ShouldApplyExternalChanges(TLocal localEntity, TExternal externalEntity, ConflictInfo conflictInfo)
		=> false; // Never apply external changes when using LocalWins strategy
}
