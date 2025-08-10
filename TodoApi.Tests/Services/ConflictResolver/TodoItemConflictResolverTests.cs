using TodoApi.Common;
using TodoApi.Tests.Builders;
using TodoApi.Services.ConflictResolutionStrategies;

namespace TodoApi.Tests.Services;

public class TodoItemConflictResolverTests
{
    private readonly Mock<ILogger<TodoListConflictResolver>> _mockTodoListLogger;
    private readonly Mock<ILogger<TodoItemConflictResolver>> _mockTodoItemLogger;
    private readonly Mock<IConflictResolutionStrategyFactory<TodoList, ExternalTodoList>> _mockTodoListStrategyFactory;
    private readonly Mock<IConflictResolutionStrategyFactory<TodoItem, ExternalTodoItem>> _mockTodoItemStrategyFactory;
    private readonly Mock<IConflictResolutionStrategy<TodoList, ExternalTodoList>> _mockTodoListStrategy;
    private readonly Mock<IConflictResolutionStrategy<TodoItem, ExternalTodoItem>> _mockTodoItemStrategy;
    private readonly TodoItemConflictResolver _todoItemConflictResolver;

    public TodoItemConflictResolverTests()
    {
        _mockTodoListLogger = new Mock<ILogger<TodoListConflictResolver>>();
        _mockTodoItemLogger = new Mock<ILogger<TodoItemConflictResolver>>();
        _mockTodoListStrategyFactory = new Mock<IConflictResolutionStrategyFactory<TodoList, ExternalTodoList>>();
        _mockTodoItemStrategyFactory = new Mock<IConflictResolutionStrategyFactory<TodoItem, ExternalTodoItem>>();
        _mockTodoListStrategy = new Mock<IConflictResolutionStrategy<TodoList, ExternalTodoList>>();
        _mockTodoItemStrategy = new Mock<IConflictResolutionStrategy<TodoItem, ExternalTodoItem>>();

        _todoItemConflictResolver = new TodoItemConflictResolver(_mockTodoItemLogger.Object, _mockTodoItemStrategyFactory.Object);
    }

    private void SetupItemStrategyForTest(ConflictResolutionStrategy strategy, bool shouldApplyExternalChanges, string resolutionReason)
    {
        var mockStrategy = new Mock<IConflictResolutionStrategy<TodoItem, ExternalTodoItem>>();
        mockStrategy.Setup(s => s.ShouldApplyExternalChanges(It.IsAny<TodoItem>(), It.IsAny<ExternalTodoItem>(), It.IsAny<ConflictInfo>()))
            .Returns(shouldApplyExternalChanges);
        mockStrategy.Setup(s => s.GetResolutionReason(It.IsAny<TodoItem>(), It.IsAny<ExternalTodoItem>(), It.IsAny<ConflictInfo>()))
            .Returns(resolutionReason);

        _mockTodoItemStrategyFactory.Setup(f => f.GetStrategy(strategy))
            .Returns(mockStrategy.Object);
    }

    [Fact]
    public void ResolveTodoItemConflict_WithNoConflict_ExternalNewer_ReturnsCorrectInfo()
    {
		// Arrange
		var syncTime = DateTime.UtcNow.AddHours(-3);

        var localTodoItem = TodoItemBuilder.Create()
            .WithId(1)
            .WithDescription("Updated External List")
            .WithLastModified(syncTime)
            .WithLastSyncedAt(syncTime)
            .Build();

        var externalTodoItem = ExternalTodoItemBuilder.Create()
            .WithId("ext-123")
            .WithDescription("Updated External List")
            .WithUpdatedAt(syncTime)
            .Build();

        // Act
        var result = _todoItemConflictResolver.ResolveConflict(localTodoItem, externalTodoItem);

        // Assert
        Assert.False(result.HasConflict);
        Assert.False(result.ConflictResolved);
        Assert.Contains("Local entity is newer or same - no change", result.ResolutionReason);
    }

    [Fact]
    public void ResolveTodoItemConflict_WithActualConflict_ExternalWins_ReturnsConflictInfo()
    {
        // Arrange
        SetupItemStrategyForTest(ConflictResolutionStrategy.ExternalWins, true, "External API changes take precedence");

        var baseTime = DateTime.UtcNow.AddHours(-3);
        var localTodoItem = TodoItemBuilder.Create()
            .WithId(1)
            .WithDescription("Local Changes")
            .WithLastModified(DateTime.UtcNow.AddHours(-1)) // Modified after last sync
            .WithLastSyncedAt(baseTime)
            .WithExternalId("ext-123")
            .Build();

        var externalTodoItem = ExternalTodoItemBuilder.Create()
            .WithId("ext-123")
            .WithDescription("External Changes")
            .WithUpdatedAt(DateTime.UtcNow.AddMinutes(-30)) // Also modified after last sync
            .Build();

        // Act
        var result = _todoItemConflictResolver.ResolveConflict(localTodoItem, externalTodoItem);

        // Assert
        Assert.True(result.HasConflict);
        Assert.True(result.ConflictResolved);
        Assert.Equal(ConflictResolutionStrategy.ExternalWins, result.Resolution);
        Assert.Contains("External API changes take precedence", result.ResolutionReason);
        Assert.Single(result.ModifiedFields);
        Assert.Contains(nameof(TodoItem.Description), result.ModifiedFields);
    }

    [Fact]
    public void ResolveTodoItemConflict_WithConflict_LocalWins_ReturnsCorrectStrategy()
    {
        // Arrange
        SetupItemStrategyForTest(ConflictResolutionStrategy.LocalWins, false, "Local changes take precedence");

        var baseTime = DateTime.UtcNow.AddHours(-3);
        var localTodoItem = TodoItemBuilder.Create()
            .WithId(1)
            .WithDescription("Local Changes")
            .WithLastModified(DateTime.UtcNow.AddHours(-1))
            .WithLastSyncedAt(baseTime)
            .Build();

        var externalTodoItem = ExternalTodoItemBuilder.Create()
            .WithId("ext-123")
            .WithDescription("External Changes")
            .WithUpdatedAt(DateTime.UtcNow.AddMinutes(-30))
            .Build();

        // Act
        var result = _todoItemConflictResolver.ResolveConflict(localTodoItem, externalTodoItem, ConflictResolutionStrategy.LocalWins);

        // Assert
        Assert.True(result.HasConflict);
        Assert.True(result.ConflictResolved);
        Assert.Equal(ConflictResolutionStrategy.LocalWins, result.Resolution);
        Assert.Contains("Local changes take precedence", result.ResolutionReason);
    }

    [Fact]
    public void ResolveTodoItemConflict_WithConflict_ManualResolution_ThrowsException()
    {
        // Arrange
        SetupItemStrategyForTest(ConflictResolutionStrategy.ManualResolution, false, "Manual conflict resolution required");

        var baseTime = DateTime.UtcNow.AddHours(-3);
        var localTodoItem = TodoItemBuilder.Create()
            .WithId(1)
            .WithDescription("Local Changes")
            .WithLastModified(DateTime.UtcNow.AddHours(-1))
            .WithLastSyncedAt(baseTime)
            .Build();

        var externalTodoItem = ExternalTodoItemBuilder.Create()
            .WithId("ext-123")
            .WithDescription("External Changes")
            .WithUpdatedAt(DateTime.UtcNow.AddMinutes(-30))
            .Build();

        // Act & Assert
        var exception = Assert.Throws<InvalidOperationException>(() =>
            _todoItemConflictResolver.ResolveConflict(localTodoItem, externalTodoItem, ConflictResolutionStrategy.ManualResolution));

        Assert.Contains("Manual conflict resolution required", exception.Message);
    }

    [Fact]
    public void ResolveTodoItemConflict_WithConflict_DetectsAllFields()
    {
        // Arrange
        SetupItemStrategyForTest(ConflictResolutionStrategy.ExternalWins, true, "External wins");

        var baseTime = DateTime.UtcNow.AddHours(-3);
        var localTodoItem = TodoItemBuilder.Create()
            .WithId(1)
            .WithDescription("Local Description")
            .WithIsCompleted(false)
            .WithLastModified(DateTime.UtcNow.AddHours(-1))
            .WithLastSyncedAt(baseTime)
            .Build();

        var externalTodoItem = ExternalTodoItemBuilder.Create()
            .WithId("ext-item-123")
            .WithDescription("External Description")
            .WithCompleted(true)
            .WithUpdatedAt(DateTime.UtcNow.AddMinutes(-30))
            .Build();

        // Act
        var result = _todoItemConflictResolver.ResolveConflict(localTodoItem, externalTodoItem);

        // Assert
        Assert.True(result.HasConflict);
        Assert.Equal(2, result.ModifiedFields.Count);
        Assert.Contains(nameof(TodoItem.Description), result.ModifiedFields);
        Assert.Contains(nameof(TodoItem.IsCompleted), result.ModifiedFields);
    }

    [Fact]
    public void ApplyResolution_ExternalWins_WithConflict_AppliesExternalChanges()
    {
        // Arrange
        SetupItemStrategyForTest(ConflictResolutionStrategy.ExternalWins, true, "Test conflict resolution");

        var localTodoItem = TodoItemBuilder.Create()
            .WithId(1)
            .WithDescription("Local Name")
            .WithLastModified(DateTime.UtcNow.AddHours(-1))
            .Build();

        var externalTodoItem = ExternalTodoItemBuilder.Create()
            .WithId("ext-123")
            .WithDescription("External Name")
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
        _todoItemConflictResolver.ApplyResolution(localTodoItem, externalTodoItem, conflictInfo);

        // Assert
        Assert.Equal("External Name", localTodoItem.Description);
        Assert.Equal(externalTodoItem.UpdatedAt, localTodoItem.LastModified);
        Assert.NotNull(localTodoItem.LastSyncedAt);
        Assert.True(localTodoItem.LastSyncedAt > DateTime.UtcNow.AddMinutes(-1));
    }

    [Fact]
    public void ApplyResolution_LocalWins_WithConflict_KeepsLocalChanges()
    {
        // Arrange
        SetupItemStrategyForTest(ConflictResolutionStrategy.LocalWins, false, "Local wins strategy");

        var originalName = "Local Name";
        var originalModified = DateTime.UtcNow.AddHours(-1);

        var localTodoItem = TodoItemBuilder.Create()
            .WithId(1)
            .WithDescription(originalName)
            .WithLastModified(originalModified)
            .Build();

        var externalTodoItem = ExternalTodoItemBuilder.Create()
            .WithId("ext-123")
            .WithDescription("External Name")
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
        _todoItemConflictResolver.ApplyResolution(localTodoItem, externalTodoItem, conflictInfo);

        // Assert
        Assert.Equal(originalName, localTodoItem.Description); // Should remain unchanged
        Assert.Equal(originalModified, localTodoItem.LastModified); // Should remain unchanged
    }

    [Fact]
    public void ApplyResolution_NoConflict_ExternalNewer_AppliesChanges()
    {
        // Arrange
        SetupItemStrategyForTest(ConflictResolutionStrategy.ExternalWins, true, "External is newer");

        var localTodoItem = TodoItemBuilder.Create()
            .WithId(1)
            .WithDescription("Local Name")
            .WithLastModified(DateTime.UtcNow.AddHours(-2))
            .Build();

        var externalTodoItem = ExternalTodoItemBuilder.Create()
            .WithId("ext-123")
            .WithDescription("External Name")
            .WithUpdatedAt(DateTime.UtcNow.AddHours(-1)) // Newer than local
            .Build();

        var conflictInfo = ConflictInfoBuilder.Create()
            .WithLocalLastModified(DateTime.UtcNow.AddHours(-2))
            .WithExternalLastModified(DateTime.UtcNow.AddHours(-1))
            .WithLastSyncedAt(DateTime.UtcNow.AddHours(-3)) // External is newer, no conflict
            .WithResolutionReason("External is newer")
            .Build();

        // Act
        _todoItemConflictResolver.ApplyResolution(localTodoItem, externalTodoItem, conflictInfo);

        // Assert
        Assert.Equal("External Name", localTodoItem.Description);
        Assert.Equal(externalTodoItem.UpdatedAt, localTodoItem.LastModified);
        Assert.NotNull(localTodoItem.LastSyncedAt);
    }

    [Fact]
    public void ApplyResolution_NoConflict_LocalNewer_OnlyUpdatesSyncTimestamp()
    {
        // Arrange
        SetupItemStrategyForTest(ConflictResolutionStrategy.LocalWins, false, "Local is newer");

        var originalName = "Local Name";
        var localChangeLastTimeModified = DateTime.UtcNow.AddMinutes(-30);

        var localTodoItem = TodoItemBuilder.Create()
            .WithDescription(originalName)
            .WithLastModified(localChangeLastTimeModified)
            .Build();

        var externalTodoItem = ExternalTodoItemBuilder.Create()
            .WithDescription("External Name")
            .WithUpdatedAt(DateTime.UtcNow.AddHours(-1)) // Older than local
            .Build();

        var conflictInfo = ConflictInfoBuilder.Create()
            .WithLocalLastModified(localChangeLastTimeModified)
            .WithExternalLastModified(DateTime.UtcNow.AddHours(-1))
            .WithLastSyncedAt(localChangeLastTimeModified) // Local is newer, no conflict
            .WithResolutionReason("Local is newer")
            .Build();

        // Act
        _todoItemConflictResolver.ApplyResolution(localTodoItem, externalTodoItem, conflictInfo);

        // Assert
        Assert.Equal(originalName, localTodoItem.Description); // Should remain unchanged
        Assert.Equal(localChangeLastTimeModified, localTodoItem.LastModified); // Should remain unchanged
        Assert.NotNull(localTodoItem.LastSyncedAt); // But sync timestamp should be updated
    }
}
