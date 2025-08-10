using TodoApi.Common;

namespace TodoApi.Services.ConflictResolutionStrategies;

/// <summary>
/// Abstract base class providing common conflict resolution functionality
/// </summary>
/// <typeparam name="TLocal">The local entity type</typeparam>
/// <typeparam name="TExternal">The external entity type</typeparam>
public abstract class ConflictResolverBase<TLocal, TExternal> : IConflictResolver<TLocal, TExternal>
    where TLocal : class
    where TExternal : class
{
    protected readonly ILogger _logger;
    private readonly IConflictResolutionStrategyFactory<TLocal, TExternal> _strategyFactory;

    protected ConflictResolverBase(ILogger logger, IConflictResolutionStrategyFactory<TLocal, TExternal> strategyFactory)
    {
        _logger = logger;
        _strategyFactory = strategyFactory;
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
            var strategyImpl = _strategyFactory.GetStrategy(strategy);
            resolutionReason = strategyImpl.GetResolutionReason(localEntity, externalEntity, conflictInfo);

			// Manual resolution cannot be handled automatically - throw exception
            if (strategy == ConflictResolutionStrategy.ManualResolution)
            {
                throw new InvalidOperationException(resolutionReason);
            }
        }
        else
        {
            // No conflict - determine which is newer
            resolutionReason = ExternalIsNewer(localEntity, externalEntity)
                ? "External entity is newer - applying external changes."
                : "Local entity is newer or same - no changes needed.";
        }

        conflictInfo.ResolutionReason = resolutionReason;
        // Ensure the Resolution property is set to the strategy used
        conflictInfo.Resolution = strategy;
        return conflictInfo;
    }

    public void ApplyResolution(TLocal localEntity, TExternal externalEntity, ConflictInfo conflictInfo)
    {
        // If there's no conflict, we don't need to use the strategy factory
        if (!conflictInfo.HasConflict)
        {
            // No conflict - determine which is newer
            if (ExternalIsNewer(localEntity, externalEntity))
            {
                // External is newer, apply changes
                ApplyExternalChanges(localEntity, externalEntity);
                UpdateLastModified(localEntity, GetExternalLastModified(externalEntity));
                UpdateSyncTimestamp(localEntity);

                _logger.LogDebug("No conflict: Applied external changes to {EntityType} {EntityId}",
                    conflictInfo.EntityType, GetEntityId(localEntity));
            }
            else
            {
                // Local is newer or same, just update sync timestamp
                UpdateSyncTimestamp(localEntity);

                _logger.LogDebug("No conflict: Local entity is newer or same for {EntityType} {EntityId} - only updating sync timestamp",
                    conflictInfo.EntityType, GetEntityId(localEntity));
            }
            return;
        }

        // There is a conflict, use the strategy factory
        var strategyImpl = _strategyFactory.GetStrategy(conflictInfo.Resolution);

        if (strategyImpl.ShouldApplyExternalChanges(localEntity, externalEntity, conflictInfo))
        {
            ApplyExternalChanges(localEntity, externalEntity);
            UpdateLastModified(localEntity, GetExternalLastModified(externalEntity));
            UpdateSyncTimestamp(localEntity);

            _logger.LogInformation("Conflict resolved: External wins for {EntityType} {EntityId} - applied external changes. Reason: {Reason}",
                conflictInfo.EntityType, GetEntityId(localEntity), conflictInfo.ResolutionReason);
        }
        else
        {
            _logger.LogInformation("Conflict resolved: Local wins for {EntityType} {EntityId} - keeping local changes",
                conflictInfo.EntityType, GetEntityId(localEntity));

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
    /// Updates the last modified timestamp on the local entity
    /// </summary>
    protected abstract void UpdateLastModified(TLocal localEntity, DateTime newLastModified);

    private bool ExternalIsNewer(TLocal localEntity, TExternal externalEntity)
        => GetExternalLastModified(externalEntity) > GetLocalLastModified(localEntity);
}
