using TodoApi.Common;
using TodoApi.Tests.Builders;
using TodoApi.Services.ConflictResolutionStrategies;
using TodoApi.Services.ConflictResolver;
using TodoApi.Services.Factories.ConflictResolutionFactory;

namespace TodoApi.Tests.Services.ConflictResolverTests;

public class TodoListConflictResolverTests
{
    private readonly Mock<ILogger<TodoListConflictResolver>> _mockTodoListLogger;    private readonly Mock<IConflictResolutionStrategyFactory<TodoList, ExternalTodoList>> _mockTodoListStrategyFactory;
    private readonly Mock<IConflictResolutionStrategyFactory<TodoItem, ExternalTodoItem>> _mockTodoItemStrategyFactory;
    private readonly Mock<IConflictResolutionStrategy<TodoList, ExternalTodoList>> _mockTodoListStrategy;
    private readonly Mock<IConflictResolutionStrategy<TodoItem, ExternalTodoItem>> _mockTodoItemStrategy;
    private readonly TodoListConflictResolver _todoListConflictResolver;

    public TodoListConflictResolverTests()
    {
        _mockTodoListLogger = new Mock<ILogger<TodoListConflictResolver>>();
        _mockTodoListStrategyFactory = new Mock<IConflictResolutionStrategyFactory<TodoList, ExternalTodoList>>();
        _mockTodoItemStrategyFactory = new Mock<IConflictResolutionStrategyFactory<TodoItem, ExternalTodoItem>>();
        _mockTodoListStrategy = new Mock<IConflictResolutionStrategy<TodoList, ExternalTodoList>>();
        _mockTodoItemStrategy = new Mock<IConflictResolutionStrategy<TodoItem, ExternalTodoItem>>();

        _todoListConflictResolver = new TodoListConflictResolver(_mockTodoListLogger.Object, _mockTodoListStrategyFactory.Object);
    }

    private void SetupStrategyForTest(ConflictResolutionStrategy strategy, bool shouldApplyExternalChanges, string resolutionReason)
    {
        var mockStrategy = new Mock<IConflictResolutionStrategy<TodoList, ExternalTodoList>>();
        mockStrategy.Setup(s => s.ShouldApplyExternalChanges(It.IsAny<TodoList>(), It.IsAny<ExternalTodoList>(), It.IsAny<ConflictInfo>()))
            .Returns(shouldApplyExternalChanges);
        mockStrategy.Setup(s => s.GetResolutionReason(It.IsAny<TodoList>(), It.IsAny<ExternalTodoList>(), It.IsAny<ConflictInfo>()))
            .Returns(resolutionReason);

        _mockTodoListStrategyFactory.Setup(f => f.GetStrategy(strategy))
            .Returns(mockStrategy.Object);
    }

    [Fact]
    public void ResolveTodoListConflict_WithNoConflict_ExternalNewer_ReturnsCorrectInfo()
    {
		// Arrange
		var syncTime = DateTime.UtcNow.AddHours(-3);

        var localTodoList = TodoListBuilder.Create()
            .WithId(1)
            .WithName("Updated External List")
            .WithLastModified(syncTime)
            .WithLastSyncedAt(syncTime)
            .Build();

        var externalTodoList = ExternalTodoListBuilder.Create()
            .WithId("ext-123")
            .WithName("Updated External List")
            .WithUpdatedAt(syncTime)
            .Build();

        // Act
        var result = _todoListConflictResolver.ResolveConflict(localTodoList, externalTodoList);

        // Assert
        Assert.False(result.HasConflict);
        Assert.False(result.ConflictResolved);
        Assert.Contains("Local entity is newer or same - no change", result.ResolutionReason);
    }

    [Fact]
    public void ResolveTodoListConflict_WithActualConflict_ExternalWins_ReturnsConflictInfo()
    {
        // Arrange
        SetupStrategyForTest(ConflictResolutionStrategy.ExternalWins, true, "External API changes take precedence");

        var baseTime = DateTime.UtcNow.AddHours(-3);
        var localTodoList = TodoListBuilder.Create()
            .WithId(1)
            .WithName("Local Changes")
            .WithLastModified(DateTime.UtcNow.AddHours(-1)) // Modified after last sync
            .WithLastSyncedAt(baseTime)
			.WithExternalId("ext-123")
            .Build();

        var externalTodoList = ExternalTodoListBuilder.Create()
			.WithId("ext-123")
			.WithName("External Changes")
			.WithUpdatedAt(DateTime.UtcNow.AddMinutes(-30)) // Also modified after last sync
            .Build();

        // Act
        var result = _todoListConflictResolver.ResolveConflict(localTodoList, externalTodoList);

        // Assert
        Assert.True(result.HasConflict);
        Assert.True(result.ConflictResolved);
        Assert.Equal(ConflictResolutionStrategy.ExternalWins, result.Resolution);
        Assert.Contains("External API changes take precedence", result.ResolutionReason);
        Assert.Single(result.ModifiedFields);
        Assert.Contains(nameof(TodoList.Name), result.ModifiedFields);
    }

    [Fact]
    public void ResolveTodoListConflict_WithConflict_LocalWins_ReturnsCorrectStrategy()
    {
        // Arrange
        SetupStrategyForTest(ConflictResolutionStrategy.LocalWins, false, "Local changes take precedence");

        var baseTime = DateTime.UtcNow.AddHours(-3);
        var localTodoList = TodoListBuilder.Create()
            .WithId(1)
            .WithName("Local Changes")
            .WithLastModified(DateTime.UtcNow.AddHours(-1))
            .WithLastSyncedAt(baseTime)
            .Build();

        var externalTodoList = ExternalTodoListBuilder.Create()
            .WithId("ext-123")
            .WithName("External Changes")
            .WithUpdatedAt(DateTime.UtcNow.AddMinutes(-30))
            .Build();

        // Act
        var result = _todoListConflictResolver.ResolveConflict(localTodoList, externalTodoList, ConflictResolutionStrategy.LocalWins);

        // Assert
        Assert.True(result.HasConflict);
        Assert.True(result.ConflictResolved);
        Assert.Equal(ConflictResolutionStrategy.LocalWins, result.Resolution);
        Assert.Contains("Local changes take precedence", result.ResolutionReason);
    }

    [Fact]
    public void ResolveTodoListConflict_WithConflict_ManualResolution_ThrowsException()
    {
        // Arrange
        SetupStrategyForTest(ConflictResolutionStrategy.ManualResolution, false, "Manual conflict resolution required");

        var baseTime = DateTime.UtcNow.AddHours(-3);
        var localTodoList = TodoListBuilder.Create()
            .WithId(1)
            .WithName("Local Changes")
            .WithLastModified(DateTime.UtcNow.AddHours(-1))
            .WithLastSyncedAt(baseTime)
            .Build();

        var externalTodoList = ExternalTodoListBuilder.Create()
            .WithId("ext-123")
            .WithName("External Changes")
            .WithUpdatedAt(DateTime.UtcNow.AddMinutes(-30))
            .Build();

        // Act & Assert
        var exception = Assert.Throws<InvalidOperationException>(() =>
            _todoListConflictResolver.ResolveConflict(localTodoList, externalTodoList, ConflictResolutionStrategy.ManualResolution));

        Assert.Contains("Manual conflict resolution required", exception.Message);
    }

    [Fact]
    public void ResolveTodoListConflict_WithConflict_DetectsAllFields()
    {
        // Arrange
        SetupStrategyForTest(ConflictResolutionStrategy.ExternalWins, true, "External wins");

        var baseTime = DateTime.UtcNow.AddHours(-3);
        var localTodoList = TodoListBuilder.Create()
            .WithId(1)
            .WithName("Local Description")
            .WithLastModified(DateTime.UtcNow.AddHours(-1))
            .WithLastSyncedAt(baseTime)
            .Build();

        var externalTodoList = ExternalTodoListBuilder.Create()
            .WithId("ext-item-123")
            .WithName("External Description")
            .WithUpdatedAt(DateTime.UtcNow.AddMinutes(-30))
            .Build();

        // Act
        var result = _todoListConflictResolver.ResolveConflict(localTodoList, externalTodoList);

        // Assert
        Assert.True(result.HasConflict);
        Assert.Single(result.ModifiedFields);
        Assert.Contains(nameof(TodoList.Name), result.ModifiedFields);
    }

    [Fact]
    public void ApplyResolution_ExternalWins_WithConflict_AppliesExternalChanges()
    {
        // Arrange
        SetupStrategyForTest(ConflictResolutionStrategy.ExternalWins, true, "Test conflict resolution");

        var localTodoList = TodoListBuilder.Create()
            .WithId(1)
            .WithName("Local Name")
            .WithLastModified(DateTime.UtcNow.AddHours(-1))
            .Build();

        var externalTodoList = ExternalTodoListBuilder.Create()
            .WithId("ext-123")
            .WithName("External Name")
            .WithUpdatedAt(DateTime.UtcNow.AddMinutes(-30))
            .Build();

        var conflictInfo = ConflictInfoBuilder.Create()
            .WithLocalLastModified(DateTime.UtcNow.AddHours(-1))
            .WithExternalLastModified(DateTime.UtcNow.AddMinutes(-30))
            .WithLastSyncedAt(DateTime.UtcNow.AddHours(-2)) // This will make HasConflict = true
            .WithResolution(ConflictResolutionStrategy.ExternalWins)
            .WithResolutionReason("Test conflict resolution")
            .Build();

        // Act
        _todoListConflictResolver.ApplyResolution(localTodoList, externalTodoList, conflictInfo);

        // Assert
        Assert.Equal("External Name", localTodoList.Name);
        Assert.Equal(externalTodoList.UpdatedAt, localTodoList.LastModified);
        Assert.NotNull(localTodoList.LastSyncedAt);
        Assert.True(localTodoList.LastSyncedAt > DateTime.UtcNow.AddMinutes(-1));
    }

    [Fact]
    public void ApplyResolution_LocalWins_WithConflict_KeepsLocalChanges()
    {
        // Arrange
        SetupStrategyForTest(ConflictResolutionStrategy.LocalWins, false, "Local wins strategy");

        var originalName = "Local Name";
        var originalModified = DateTime.UtcNow.AddHours(-1);

        var localTodoList = TodoListBuilder.Create()
            .WithId(1)
            .WithName(originalName)
            .WithLastModified(originalModified)
            .Build();

        var externalTodoList = ExternalTodoListBuilder.Create()
            .WithId("ext-123")
            .WithName("External Name")
            .WithUpdatedAt(DateTime.UtcNow.AddMinutes(-30))
            .Build();

        var conflictInfo = ConflictInfoBuilder.Create()
			.WithModifiedFields(new List<string> { nameof(TodoList.Name) })
            .WithLocalLastModified(DateTime.UtcNow.AddHours(-1))
            .WithExternalLastModified(DateTime.UtcNow.AddMinutes(-30))
            .WithLastSyncedAt(DateTime.UtcNow.AddHours(-2)) // This will make HasConflict = true
            .WithResolution(ConflictResolutionStrategy.LocalWins)
            .WithResolutionReason("Local wins strategy")
            .Build();

        // Act
        _todoListConflictResolver.ApplyResolution(localTodoList, externalTodoList, conflictInfo);

        // Assert
        Assert.Equal(originalName, localTodoList.Name); // Should remain unchanged
        Assert.Equal(originalModified, localTodoList.LastModified); // Should remain unchanged
    }

    [Fact]
    public void ApplyResolution_TodoList_ExternalWins_AppliesAllFields()
    {
        // Arrange
        SetupStrategyForTest(ConflictResolutionStrategy.ExternalWins, true, "External wins");

        var localTodoList = TodoListBuilder.Create()
            .WithId(1)
            .WithName("Local Description")
            .WithLastModified(DateTime.UtcNow.AddHours(-1))
            .Build();

        var externalTodoList = ExternalTodoListBuilder.Create()
            .WithId("ext-item-123")
            .WithName("External Description")
            .WithUpdatedAt(DateTime.UtcNow.AddMinutes(-30))
            .Build();

        var conflictInfo = ConflictInfoBuilder.Create()
            .WithLocalLastModified(DateTime.UtcNow.AddHours(-1))
            .WithExternalLastModified(DateTime.UtcNow.AddMinutes(-30))
            .WithLastSyncedAt(DateTime.UtcNow.AddHours(-2)) // This will make HasConflict = true
            .WithResolution(ConflictResolutionStrategy.ExternalWins)
            .WithResolutionReason("External wins")
            .Build();

        // Act
        _todoListConflictResolver.ApplyResolution(localTodoList, externalTodoList, conflictInfo);

        // Assert
        Assert.Equal("External Description", localTodoList.Name);
        Assert.Equal(externalTodoList.UpdatedAt, localTodoList.LastModified);
        Assert.NotNull(localTodoList.LastSyncedAt);
    }

    [Fact]
    public void ApplyResolution_NoConflict_ExternalNewer_AppliesChanges()
    {
        // Arrange
        SetupStrategyForTest(ConflictResolutionStrategy.ExternalWins, true, "External is newer");

        var localTodoList = TodoListBuilder.Create()
            .WithId(1)
            .WithName("Local Name")
            .WithLastModified(DateTime.UtcNow.AddHours(-2))
            .Build();

        var externalTodoList = ExternalTodoListBuilder.Create()
            .WithId("ext-123")
            .WithName("External Name")
            .WithUpdatedAt(DateTime.UtcNow.AddHours(-1)) // Newer than local
            .Build();

        var conflictInfo = ConflictInfoBuilder.Create()
            .WithLocalLastModified(DateTime.UtcNow.AddHours(-2))
            .WithExternalLastModified(DateTime.UtcNow.AddHours(-1))
            .WithLastSyncedAt(DateTime.UtcNow.AddHours(-3)) // External is newer, no conflict
            .WithResolutionReason("External is newer")
            .Build();

        // Act
        _todoListConflictResolver.ApplyResolution(localTodoList, externalTodoList, conflictInfo);

        // Assert
        Assert.Equal("External Name", localTodoList.Name);
        Assert.Equal(externalTodoList.UpdatedAt, localTodoList.LastModified);
        Assert.NotNull(localTodoList.LastSyncedAt);
    }

    [Fact]
    public void ApplyResolution_NoConflict_LocalNewer_OnlyUpdatesSyncTimestamp()
    {
        // Arrange
        SetupStrategyForTest(ConflictResolutionStrategy.LocalWins, false, "Local is newer");

        var originalName = "Local Name";
        var localChangeLastTimeModified = DateTime.UtcNow.AddMinutes(-30);

        var localTodoList = TodoListBuilder.Create()
            .WithName(originalName)
            .WithLastModified(localChangeLastTimeModified)
            .Build();

        var externalTodoList = ExternalTodoListBuilder.Create()
            .WithName("External Name")
            .WithUpdatedAt(DateTime.UtcNow.AddHours(-1)) // Older than local
            .Build();

        var conflictInfo = ConflictInfoBuilder.Create()
            .WithLocalLastModified(localChangeLastTimeModified)
            .WithExternalLastModified(DateTime.UtcNow.AddHours(-1))
            .WithLastSyncedAt(localChangeLastTimeModified) // Local is newer, no conflict
            .WithResolutionReason("Local is newer")
            .Build();

        // Act
        _todoListConflictResolver.ApplyResolution(localTodoList, externalTodoList, conflictInfo);

        // Assert
        Assert.Equal(originalName, localTodoList.Name); // Should remain unchanged
        Assert.Equal(localChangeLastTimeModified, localTodoList.LastModified); // Should remain unchanged
        Assert.NotNull(localTodoList.LastSyncedAt); // But sync timestamp should be updated
    }
}
