using TodoApi.Common;
using TodoApi.Services.ConflictResolutionStrategies;

namespace TodoApi.Services.Factories.ConflictResolutionFactory;

/// <summary>
/// Factory for creating conflict resolution strategies
/// </summary>
/// <typeparam name="TLocal">The local entity type</typeparam>
/// <typeparam name="TExternal">The external entity type</typeparam>
public interface IConflictResolutionStrategyFactory<TLocal, TExternal>
{
    /// <summary>
    /// Gets the strategy for the specified resolution type
    /// </summary>
    /// <param name="strategy">The conflict resolution strategy</param>
    /// <returns>The appropriate strategy implementation</returns>
    IConflictResolutionStrategy<TLocal, TExternal> GetStrategy(ConflictResolutionStrategy strategy);
}
