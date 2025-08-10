using TodoApi.Common;

namespace TodoApi.Services.ConflictResolutionStrategies;

/// <summary>
/// Strategy that always gives precedence to external API changes
/// </summary>
/// <typeparam name="TLocal">The local entity type</typeparam>
/// <typeparam name="TExternal">The external entity type</typeparam>
public class ExternalWinsStrategy<TLocal, TExternal> : IConflictResolutionStrategy<TLocal, TExternal>
    where TLocal : class
    where TExternal : class
{
    private readonly ILogger _logger;

    public ExternalWinsStrategy(ILogger logger)
    {
        _logger = logger;
    }

    public ConflictResolutionStrategy StrategyType => ConflictResolutionStrategy.ExternalWins;

    public string GetResolutionReason(TLocal localEntity, TExternal externalEntity, ConflictInfo conflictInfo)
    {
        var resolutionReason = $"External API changes take precedence. Local changes made at {conflictInfo.LocalLastModified:yyyy-MM-dd HH:mm:ss} " +
                              $"will be overwritten by external changes made at {conflictInfo.ExternalLastModified:yyyy-MM-dd HH:mm:ss}.";

        _logger.LogWarning("CONFLICT DETECTED: {EntityType} {LocalId} (External: {ExternalId}) - " +
                          "Both local and external modified since last sync. Resolution: External Wins. " +
                          "Fields in conflict: {Fields}",
                          conflictInfo.EntityType, conflictInfo.EntityId, conflictInfo.ExternalEntityId,
                          string.Join(", ", conflictInfo.ModifiedFields));

        return resolutionReason;
    }

	public bool ShouldApplyExternalChanges(TLocal localEntity, TExternal externalEntity, ConflictInfo conflictInfo)
		=> true; // Always apply external changes when using ExternalWins strategy
}
