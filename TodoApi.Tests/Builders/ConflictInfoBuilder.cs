using TodoApi.Common;

namespace TodoApi.Tests.Builders;

public class ConflictInfoBuilder
{
    private ConflictInfo _conflictInfo = new ConflictInfo();

    public ConflictInfoBuilder WithLocalLastModified(DateTime localLastModified)
    {
        _conflictInfo.LocalLastModified = localLastModified;
        return this;
    }

    public ConflictInfoBuilder WithExternalLastModified(DateTime externalLastModified)
    {
        _conflictInfo.ExternalLastModified = externalLastModified;
        return this;
    }

    public ConflictInfoBuilder WithLastSyncedAt(DateTime lastSyncedAt)
    {
        _conflictInfo.LastSyncedAt = lastSyncedAt;
        return this;
    }
    public ConflictInfoBuilder WithResolution(ConflictResolutionStrategy resolution)
    {
        _conflictInfo.Resolution = resolution;
        return this;
    }

    public ConflictInfoBuilder WithResolutionReason(string resolutionReason)
    {
        _conflictInfo.ResolutionReason = resolutionReason;
        return this;
    }

    public ConflictInfoBuilder WithModifiedFields(List<string> modifiedFields)
    {
        _conflictInfo.ModifiedFields = modifiedFields;
        return this;
    }

    public ConflictInfo Build() => _conflictInfo;

    public static ConflictInfoBuilder Create() => new ConflictInfoBuilder();
}
