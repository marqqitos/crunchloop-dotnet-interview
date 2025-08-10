using TodoApi.Common;
using TodoApi.Services.ConflictResolutionStrategies;

namespace TodoApi.Services.Factories.ConflictResolutionFactory;

/// <summary>
/// Factory implementation for creating conflict resolution strategies
/// </summary>
/// <typeparam name="TLocal">The local entity type</typeparam>
/// <typeparam name="TExternal">The external entity type</typeparam>
public class ConflictResolutionStrategyFactory<TLocal, TExternal> : IConflictResolutionStrategyFactory<TLocal, TExternal>
    where TLocal : class
    where TExternal : class
{
    private readonly Dictionary<ConflictResolutionStrategy, IConflictResolutionStrategy<TLocal, TExternal>> _strategies;

    public ConflictResolutionStrategyFactory(ILogger logger)
    {
        _strategies = new Dictionary<ConflictResolutionStrategy, IConflictResolutionStrategy<TLocal, TExternal>>
        {
            { ConflictResolutionStrategy.ExternalWins, new ExternalWinsStrategy<TLocal, TExternal>(logger) },
            { ConflictResolutionStrategy.LocalWins, new LocalWinsStrategy<TLocal, TExternal>(logger) },
            { ConflictResolutionStrategy.ManualResolution, new ManualResolutionStrategy<TLocal, TExternal>(logger) }
        };
    }

    public IConflictResolutionStrategy<TLocal, TExternal> GetStrategy(ConflictResolutionStrategy strategy)
    {
        if (_strategies.TryGetValue(strategy, out var strategyImpl))
        {
            return strategyImpl;
        }

        throw new ArgumentOutOfRangeException(nameof(strategy), strategy, $"Unknown conflict resolution strategy: {strategy}");
    }
}
