using TodoApi.Common;

namespace TodoApi.Services.ConflictResolver;

/// <summary>
/// Abstract base class providing common conflict resolution functionality
/// </summary>
/// <typeparam name="TLocal">The local entity type</typeparam>
/// <typeparam name="TExternal">The external entity type</typeparam>
public abstract class ConflictResolverBase<TLocal, TExternal> : IConflictResolver<TLocal, TExternal>
{
    protected readonly ILogger _logger;

    protected ConflictResolverBase(ILogger logger)
    {
        _logger = logger;
    }

    public ConflictInfo ResolveConflict(
        TLocal localEntity,
        TExternal externalEntity,
        ConflictResolutionStrategy strategy = ConflictResolutionStrategy.ExternalWins)
    {
        var conflictInfo = CreateConflictInfo(localEntity, externalEntity, strategy);
        DetectModifiedFields(localEntity, externalEntity, conflictInfo);

        var resolutionReason = string.Empty;

        // Detect if there's a conflict
        if (conflictInfo.HasConflict)
        {
            resolutionReason = ResolveConflictStrategy(localEntity, externalEntity, conflictInfo, strategy);
        }
        else
		{
			// No conflict - determine which is newer
			resolutionReason = ExternalIsNewer(localEntity, externalEntity)
				? "External entity is newer - applying external changes."
				: "Local entity is newer or same - no changes needed.";
		}

		conflictInfo.ResolutionReason = resolutionReason;
        return conflictInfo;
    }

	public void ApplyResolution(TLocal localEntity, TExternal externalEntity, ConflictInfo conflictInfo)
    {
        if (conflictInfo.Resolution == ConflictResolutionStrategy.LocalWins && conflictInfo.HasConflict)
        {
            // Local wins - don't apply external changes
            _logger.LogInformation("Conflict resolved: Local wins for {EntityType} {EntityId} - keeping local changes",
                conflictInfo.EntityType, conflictInfo.EntityId);
            UpdateSyncTimestamp(localEntity);
            return;
        }

        // Apply external changes (default for ExternalWins or no conflict with external newer)
        if (conflictInfo.HasConflict || ExternalIsNewer(localEntity, externalEntity))
        {
            ApplyExternalChanges(localEntity, externalEntity);

            if (conflictInfo.HasConflict)
            {
                _logger.LogInformation("Conflict resolved: External wins for {EntityType} {EntityId} - applied external changes. Reason: {Reason}",
                    conflictInfo.EntityType, conflictInfo.EntityId, conflictInfo.ResolutionReason);
            }
            else
            {
                _logger.LogDebug("No conflict: Applied external changes to {EntityType} {EntityId}",
                    conflictInfo.EntityType, conflictInfo.EntityId);
            }
        }
        else
        {
            // Just update sync timestamp
            UpdateSyncTimestamp(localEntity);
        }
    }

    /// <summary>
    /// Creates the initial conflict info object for the entities
    /// </summary>
    protected abstract ConflictInfo CreateConflictInfo(TLocal localEntity, TExternal externalEntity, ConflictResolutionStrategy strategy);

    /// <summary>
    /// Detects which fields have been modified between local and external entities
    /// </summary>
    protected abstract void DetectModifiedFields(TLocal localEntity, TExternal externalEntity, ConflictInfo conflictInfo);

    /// <summary>
    /// Gets the last modified timestamp from the local entity
    /// </summary>
    protected abstract DateTime GetLocalLastModified(TLocal localEntity);

    /// <summary>
    /// Gets the last modified timestamp from the external entity
    /// </summary>
    protected abstract DateTime GetExternalLastModified(TExternal externalEntity);

    /// <summary>
    /// Gets the entity ID as a string from the local entity
    /// </summary>
    protected abstract string GetEntityId(TLocal localEntity);

    /// <summary>
    /// Gets the entity ID as a string from the external entity
    /// </summary>
    protected abstract string GetExternalEntityId(TExternal externalEntity);

    /// <summary>
    /// Applies external changes to the local entity
    /// </summary>
    protected abstract void ApplyExternalChanges(TLocal localEntity, TExternal externalEntity);

    /// <summary>
    /// Updates only the sync timestamp on the local entity
    /// </summary>
    protected abstract void UpdateSyncTimestamp(TLocal localEntity);

    /// <summary>
    /// Resolves conflict based on the specified strategy
    /// </summary>
    private string ResolveConflictStrategy(TLocal localEntity, TExternal externalEntity, ConflictInfo conflictInfo, ConflictResolutionStrategy strategy)
    {
        return strategy switch
        {
            ConflictResolutionStrategy.ExternalWins => HandleExternalWinsStrategy(localEntity, externalEntity, conflictInfo),
            ConflictResolutionStrategy.LocalWins => HandleLocalWinsStrategy(localEntity, externalEntity),
            ConflictResolutionStrategy.Manual => HandleManualStrategy(localEntity, externalEntity, conflictInfo),
            _ => throw new ArgumentOutOfRangeException(nameof(strategy), strategy, "Unknown conflict resolution strategy")
        };
    }

    private string HandleExternalWinsStrategy(TLocal localEntity, TExternal externalEntity, ConflictInfo conflictInfo)
    {
        var resolutionReason = $"External API changes take precedence. Local changes made at {GetLocalLastModified(localEntity):yyyy-MM-dd HH:mm:ss} " +
                              $"will be overwritten by external changes made at {GetExternalLastModified(externalEntity):yyyy-MM-dd HH:mm:ss}.";

        _logger.LogWarning("CONFLICT DETECTED: {EntityType} {LocalId} (External: {ExternalId}) - " +
                          "Both local and external modified since last sync. Resolution: External Wins. " +
                          "Fields in conflict: {Fields}",
                          conflictInfo.EntityType, GetEntityId(localEntity), GetExternalEntityId(externalEntity),
                          string.Join(", ", conflictInfo.ModifiedFields));

        return resolutionReason;
    }

    private string HandleLocalWinsStrategy(TLocal localEntity, TExternal externalEntity)
    {
        return $"Local changes take precedence. External changes made at {GetExternalLastModified(externalEntity):yyyy-MM-dd HH:mm:ss} " +
               $"will be ignored in favor of local changes made at {GetLocalLastModified(localEntity):yyyy-MM-dd HH:mm:ss}.";
    }

    private string HandleManualStrategy(TLocal localEntity, TExternal externalEntity, ConflictInfo conflictInfo)
    {
        throw new InvalidOperationException(
            $"Manual conflict resolution required for {conflictInfo.EntityType} {GetEntityId(localEntity)}. " +
            $"Local: {GetLocalLastModified(localEntity):yyyy-MM-dd HH:mm:ss}, " +
            $"External: {GetExternalLastModified(externalEntity):yyyy-MM-dd HH:mm:ss}, " +
            $"Last Sync: {conflictInfo.LastSyncedAt:yyyy-MM-dd HH:mm:ss}");
    }

	private bool ExternalIsNewer(TLocal localEntity, TExternal externalEntity)
		=> GetExternalLastModified(externalEntity) > GetLocalLastModified(localEntity);
}
