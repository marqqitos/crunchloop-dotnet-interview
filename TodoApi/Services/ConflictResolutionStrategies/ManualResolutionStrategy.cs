using TodoApi.Common;

namespace TodoApi.Services.ConflictResolutionStrategies;

/// <summary>
/// Strategy that requires manual resolution of conflicts
/// </summary>
/// <typeparam name="TLocal">The local entity type</typeparam>
/// <typeparam name="TExternal">The external entity type</typeparam>
public class ManualResolutionStrategy<TLocal, TExternal> : IConflictResolutionStrategy<TLocal, TExternal>
    where TLocal : class
    where TExternal : class
{
    private readonly ILogger _logger;

    public ManualResolutionStrategy(ILogger logger)
    {
        _logger = logger;
    }

    public ConflictResolutionStrategy StrategyType => ConflictResolutionStrategy.ManualResolution;

    public string GetResolutionReason(TLocal localEntity, TExternal externalEntity, ConflictInfo conflictInfo)
    {
        var resolutionReason = $"Manual resolution required. Local changes made at {conflictInfo.LocalLastModified:yyyy-MM-dd HH:mm:ss} " +
                              $"conflict with external changes made at {conflictInfo.ExternalLastModified:yyyy-MM-dd HH:mm:ss}. " +
                              "Human intervention needed to determine which changes to keep.";

        _logger.LogError("CONFLICT DETECTED: {EntityType} {LocalId} (External: {ExternalId}) - " +
                        "Both local and external modified since last sync. Resolution: Manual Required. " +
                        "Fields in conflict: {Fields}",
                        conflictInfo.EntityType, conflictInfo.EntityId, conflictInfo.ExternalEntityId,
                        string.Join(", ", conflictInfo.ModifiedFields));

        return resolutionReason;
    }

	public bool ShouldApplyExternalChanges(TLocal localEntity, TExternal externalEntity, ConflictInfo conflictInfo)
		=> false; // Never automatically apply external changes when manual resolution is required
}
