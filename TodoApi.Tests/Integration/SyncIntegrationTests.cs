using Microsoft.EntityFrameworkCore;
using TodoApi.Common;
using TodoApi.Tests.Builders;

namespace TodoApi.Tests.Integration;

/// <summary>
/// Integration tests that test the full sync flow with real database operations
/// </summary>
public class SyncIntegrationTests : IDisposable
{
    private readonly TodoContext _context;
    private readonly Mock<IExternalTodoApiClient> _mockExternalClient;
    private readonly Mock<IConflictResolver> _mockConflictResolver;
    private readonly Mock<IRetryPolicyService> _mockRetryPolicyService;
    private readonly Mock<ILogger<TodoSyncService>> _mockLogger;
    private readonly TodoSyncService _syncService;

    public SyncIntegrationTests()
    {
        var options = new DbContextOptionsBuilder<TodoContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
        _context = new TodoContext(options);

                _mockExternalClient = new Mock<IExternalTodoApiClient>();
        _mockConflictResolver = new Mock<IConflictResolver>();
        _mockRetryPolicyService = new Mock<IRetryPolicyService>();
        _mockLogger = new Mock<ILogger<TodoSyncService>>();
 
        _mockExternalClient.Setup(x => x.SourceId).Returns("integration-test-source");
        
        // Setup retry policy mocks to return empty pipelines for tests
        _mockRetryPolicyService.Setup(x => x.GetHttpRetryPolicy()).Returns(Polly.ResiliencePipeline.Empty);
        _mockRetryPolicyService.Setup(x => x.GetDatabaseRetryPolicy()).Returns(Polly.ResiliencePipeline.Empty);
        _mockRetryPolicyService.Setup(x => x.GetSyncRetryPolicy()).Returns(Polly.ResiliencePipeline.Empty);
 
        _syncService = new TodoSyncService(_context, _mockExternalClient.Object, _mockConflictResolver.Object, _mockRetryPolicyService.Object, _mockLogger.Object);
    }

    [Fact]
    public async Task FullSyncWorkflow_WithComplexScenario_ShouldHandleAllCases()
    {
        // Arrange - Create a complex scenario with multiple lists and items
        var list1 = TodoListBuilder.Create()
            .WithName("Shopping List")
            .WithItem(TodoItemBuilder.Create().WithDescription("Buy milk").WithIsCompleted(false).Build())
            .WithItem(TodoItemBuilder.Create().WithDescription("Buy bread").WithIsCompleted(true).Build())
            .Build();

        var list2 = TodoListBuilder.Create()
            .WithName("Work Tasks")
            .WithItem(TodoItemBuilder.Create().WithDescription("Review PR").WithIsCompleted(false).WithTodoListId(2).Build())
            .WithItem(TodoItemBuilder.Create().WithDescription("Deploy to prod").WithIsCompleted(false).WithTodoListId(2).Build())
            .WithItem(TodoItemBuilder.Create().WithDescription("Update docs").WithIsCompleted(true).WithTodoListId(2).Build())
            .Build();

        var list3 = TodoListBuilder.Create()
            .WithName("Already Synced")
            .WithExternalId("already-synced-id")
            .Build();

        _context.TodoList.AddRange(list1, list2, list3);
        await _context.SaveChangesAsync();

        // Setup external API responses
        _mockExternalClient
            .Setup(x => x.CreateTodoListAsync(It.Is<CreateExternalTodoList>(dto => dto.Name == "Shopping List")))
            .ReturnsAsync(ExternalTodoListBuilder.Create()
                .WithId("ext-shopping")
                .WithName("Shopping List")
                .WithSourceId("integration-test-source")
                .WithItem(ExternalTodoItemBuilder.Create().WithId("item-milk").WithDescription("Buy milk").WithCompleted(false).Build())
                .WithItem(ExternalTodoItemBuilder.Create().WithId("item-bread").WithDescription("Buy bread").WithCompleted(true).Build())
                .Build());

        _mockExternalClient
            .Setup(x => x.CreateTodoListAsync(It.Is<CreateExternalTodoList>(dto => dto.Name == "Work Tasks")))
            .ReturnsAsync(ExternalTodoListBuilder.Create()
                .WithId("ext-work")
                .WithName("Work Tasks")
                .WithSourceId("integration-test-source")
                .WithItem(ExternalTodoItemBuilder.Create().WithId("item-pr").WithDescription("Review PR").WithCompleted(false).Build())
                .WithItem(ExternalTodoItemBuilder.Create().WithId("item-deploy").WithDescription("Deploy to prod").WithCompleted(false).Build())
                .WithItem(ExternalTodoItemBuilder.Create().WithId("item-docs").WithDescription("Update docs").WithCompleted(true).Build())
                .Build());

        // Act
        await _syncService.SyncTodoListsToExternalAsync();

        // Assert
        var allLists = await _context.TodoList.Include(tl => tl.Items).ToListAsync();

        // Verify sync results
        var shoppingList = allLists.First(l => l.Name == "Shopping List");
        var workList = allLists.First(l => l.Name == "Work Tasks");
        var alreadySyncedList = allLists.First(l => l.Name == "Already Synced");

        // Shopping list should be synced
        Assert.Equal("ext-shopping", shoppingList.ExternalId);
        Assert.NotEqual(default(DateTime), shoppingList.LastModified);

        var milkItem = shoppingList.Items.First(i => i.Description == "Buy milk");
        var breadItem = shoppingList.Items.First(i => i.Description == "Buy bread");
        Assert.Equal("item-milk", milkItem.ExternalId);
        Assert.Equal("item-bread", breadItem.ExternalId);

        // Work list should be synced
        Assert.Equal("ext-work", workList.ExternalId);
        var prItem = workList.Items.First(i => i.Description == "Review PR");
        var deployItem = workList.Items.First(i => i.Description == "Deploy to prod");
        var docsItem = workList.Items.First(i => i.Description == "Update docs");
        Assert.Equal("item-pr", prItem.ExternalId);
        Assert.Equal("item-deploy", deployItem.ExternalId);
        Assert.Equal("item-docs", docsItem.ExternalId);

        // Already synced list should remain unchanged
        Assert.Equal("already-synced-id", alreadySyncedList.ExternalId);

        // Verify external API was called correctly
        _mockExternalClient.Verify(x => x.CreateTodoListAsync(It.IsAny<CreateExternalTodoList>()), Times.Exactly(2));
        _mockExternalClient.Verify(x => x.CreateTodoListAsync(It.Is<CreateExternalTodoList>(dto => dto.Name == "Shopping List")), Times.Once);
        _mockExternalClient.Verify(x => x.CreateTodoListAsync(It.Is<CreateExternalTodoList>(dto => dto.Name == "Work Tasks")), Times.Once);
    }

    [Fact]
    public async Task SyncAfterLocalChanges_ShouldRespectLastModifiedTimestamps()
    {
        // Arrange - Create a list and sync it
        var todoList = TodoListBuilder.Create()
            .WithName("Test List")
            .WithItem(TodoItemBuilder.Create().WithDescription("Original task").Build())
            .Build();

        _context.TodoList.Add(todoList);
        await _context.SaveChangesAsync();

        var originalLastModified = todoList.LastModified;

        _mockExternalClient
            .Setup(x => x.CreateTodoListAsync(It.IsAny<CreateExternalTodoList>()))
            .ReturnsAsync(ExternalTodoListBuilder.Create()
                .WithId("ext-123")
                .WithName("Test List")
                .Build());

        // First sync
        await _syncService.SyncTodoListsToExternalAsync();

        // Verify first sync worked
        var syncedList = await _context.TodoList.FirstAsync();
        Assert.Equal("ext-123", syncedList.ExternalId);
        var firstSyncTime = syncedList.LastModified;
        Assert.True(firstSyncTime > originalLastModified);

        // Act - Run sync again (should not call external API again)
        await _syncService.SyncTodoListsToExternalAsync();

        // Assert - No additional calls should be made
        _mockExternalClient.Verify(x => x.CreateTodoListAsync(It.IsAny<CreateExternalTodoList>()), Times.Once);

        var finalList = await _context.TodoList.FirstAsync();
        Assert.Equal(firstSyncTime, finalList.LastModified); // Timestamp should not change
    }

    [Fact]
    public async Task SyncWithPartialFailures_ShouldPersistSuccessfulSyncs()
    {
        // Arrange
        var list1 = TodoListBuilder.Create()
            .WithName("Success List")
            .Build();
        var list2 = TodoListBuilder.Create()
            .WithName("Failure List")
            .Build();
        var list3 = TodoListBuilder.Create()
            .WithName("Another Success")
            .Build();

        _context.TodoList.AddRange(list1, list2, list3);
        await _context.SaveChangesAsync();

        // Setup mixed success/failure responses
        _mockExternalClient
            .Setup(x => x.CreateTodoListAsync(It.Is<CreateExternalTodoList>(dto => dto.Name == "Success List")))
            .ReturnsAsync(ExternalTodoListBuilder.Create()
                .WithId("ext-success-1")
                .WithName("Success List")
                .Build());

        _mockExternalClient
            .Setup(x => x.CreateTodoListAsync(It.Is<CreateExternalTodoList>(dto => dto.Name == "Failure List")))
            .ThrowsAsync(new HttpRequestException("External API Error"));

        _mockExternalClient
            .Setup(x => x.CreateTodoListAsync(It.Is<CreateExternalTodoList>(dto => dto.Name == "Another Success")))
            .ReturnsAsync(ExternalTodoListBuilder.Create()
                .WithId("ext-success-2")
                .WithName("Another Success")
                .Build());

        // Act
        await _syncService.SyncTodoListsToExternalAsync();

        // Assert
        var allLists = await _context.TodoList.ToListAsync();

        var successList1 = allLists.First(l => l.Name == "Success List");
        var failureList = allLists.First(l => l.Name == "Failure List");
        var successList2 = allLists.First(l => l.Name == "Another Success");

        // Successful syncs should have external IDs
        Assert.Equal("ext-success-1", successList1.ExternalId);
        Assert.Equal("ext-success-2", successList2.ExternalId);

        // Failed sync should not have external ID
        Assert.Null(failureList.ExternalId);

        // All three should have been attempted
        _mockExternalClient.Verify(x => x.CreateTodoListAsync(It.IsAny<CreateExternalTodoList>()), Times.Exactly(3));
    }

    [Fact]
    public async Task SyncTodoListsFromExternalAsync_WithNewExternalData_CreatesLocalTodoList()
    {
        // Arrange
        var item = ExternalTodoItemBuilder.Create()
            .WithId("external-item-456")
            .WithDescription("External Task")
            .WithCompleted(false)
            .WithCreatedAt(DateTime.UtcNow)
            .WithUpdatedAt(DateTime.UtcNow)
			.Build();

        var externalTodoList = ExternalTodoListBuilder.Create()
            .WithId("external-123")
            .WithName("External List")
            .WithCreatedAt(DateTime.UtcNow)
            .WithUpdatedAt(DateTime.UtcNow)
            .WithItem(item)
            .Build();

        _mockExternalClient.Setup(x => x.GetTodoListsAsync())
            .ReturnsAsync(new List<ExternalTodoList> { externalTodoList });

        // Act
        await _syncService.SyncTodoListsFromExternalAsync();

        // Assert
        var localTodoList = await _context.TodoList
            .Include(tl => tl.Items)
            .FirstOrDefaultAsync(tl => tl.ExternalId == "external-123");

        Assert.NotNull(localTodoList);
        Assert.Equal("External List", localTodoList.Name);
        Assert.Equal("external-123", localTodoList.ExternalId);
        Assert.Single(localTodoList.Items);

        var localItem = localTodoList.Items.First();
        Assert.Equal("External Task", localItem.Description);
        Assert.False(localItem.IsCompleted);
        Assert.Equal("external-item-456", localItem.ExternalId);
    }

    [Fact]
    public async Task SyncTodoListsFromExternalAsync_WithExistingTodoList_UpdatesWhenExternalIsNewer()
    {
        // Arrange - Create existing local TodoList
        var existingTodoList = TodoListBuilder.Create()
            .WithName("Old Name")
            .WithExternalId("external-123")
            .WithLastModified(DateTime.UtcNow.AddHours(-1)) // Older than external
            .Build();

        _context.TodoList.Add(existingTodoList);
        await _context.SaveChangesAsync();

        var externalTodoList = ExternalTodoListBuilder.Create()
			.WithId("external-123")
			.WithName("Updated Name")
			.WithCreatedAt(DateTime.UtcNow.AddHours(-2))
			.WithUpdatedAt(DateTime.UtcNow) // Newer than local
			.Build();

        _mockExternalClient.Setup(x => x.GetTodoListsAsync())
            .ReturnsAsync(new List<ExternalTodoList> { externalTodoList });

		_mockConflictResolver.Setup(x => x.ResolveTodoListConflict(
			It.IsAny<TodoList>(),
			It.IsAny<ExternalTodoList>(),
			It.IsAny<ConflictResolutionStrategy>()))
			.Returns(ConflictInfoBuilder.Create()
				.WithResolution(ConflictResolutionStrategy.ExternalWins)
				.Build());

		_mockConflictResolver.Setup(x => x.ApplyResolution(
			It.IsAny<TodoList>(),
			It.IsAny<ExternalTodoList>(),
			It.IsAny<ConflictInfo>()))
			.Callback<TodoList, ExternalTodoList, ConflictInfo>((todoList, externalTodoList, conflictInfo) =>
			{
				todoList.Name = externalTodoList.Name;
				todoList.LastModified = externalTodoList.UpdatedAt;
			});

        // Act
        await _syncService.SyncTodoListsFromExternalAsync();

        // Assert
        var updatedTodoList = await _context.TodoList.FindAsync(existingTodoList.Id);
        Assert.NotNull(updatedTodoList);
        Assert.Equal("Updated Name", updatedTodoList.Name);
        Assert.Equal(externalTodoList.UpdatedAt, updatedTodoList.LastModified);
    }

    [Fact]
    public async Task SyncTodoListsFromExternalAsync_WithExistingTodoList_DoesNotUpdateWhenLocalIsNewer()
    {
        // Arrange - Create existing local TodoList
        var existingTodoList = TodoListBuilder.Create()
            .WithName("Current Name")
            .WithExternalId("external-123")
            .WithLastModified(DateTime.UtcNow) // Newer than external
            .Build();

        _context.TodoList.Add(existingTodoList);
        await _context.SaveChangesAsync();

        var externalTodoList = ExternalTodoListBuilder.Create()
            .WithId("external-123")
            .WithName("Older Name")
            .WithCreatedAt(DateTime.UtcNow.AddHours(-2))
            .WithUpdatedAt(DateTime.UtcNow.AddHours(-1)) // Older than local
            .Build();

        _mockExternalClient.Setup(x => x.GetTodoListsAsync())
            .ReturnsAsync(new List<ExternalTodoList> { externalTodoList });

        // Act
        await _syncService.SyncTodoListsFromExternalAsync();

        // Assert
        var unchangedTodoList = await _context.TodoList.FindAsync(existingTodoList.Id);
        Assert.NotNull(unchangedTodoList);
        Assert.Equal("Current Name", unchangedTodoList.Name); // Should remain unchanged
    }

    [Fact]
    public async Task SyncTodoListsFromExternalAsync_WithNoExternalData_LogsAndReturns()
    {
        // Arrange
        _mockExternalClient.Setup(x => x.GetTodoListsAsync())
            .ReturnsAsync(new List<ExternalTodoList>());

        // Act
        await _syncService.SyncTodoListsFromExternalAsync();

        // Assert
        var todoLists = await _context.TodoList.ToListAsync();
        Assert.Empty(todoLists);

        // Verify appropriate logging occurred (checking the logger was called)
        _mockLogger.Verify(
            x => x.Log(
                LogLevel.Information,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("No TodoLists found in external API")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Fact]
    public async Task SyncTodoListsFromExternalAsync_WithNewTodoItems_CreatesLocalTodoItems()
    {
        // Arrange - Create existing local TodoList with one item
        var item = TodoItemBuilder.Create()
            .WithDescription("Existing Item")
            .WithIsCompleted(false)
            .WithExternalId("external-item-1")
            .WithLastModified(DateTime.UtcNow.AddHours(-1))
            .Build();

        var existingTodoList = TodoListBuilder.Create()
            .WithName("Existing List")
            .WithExternalId("external-123")
            .WithLastModified(DateTime.UtcNow.AddHours(-1))
            .WithItem(item)
            .Build();

        _context.TodoList.Add(existingTodoList);

        await _context.SaveChangesAsync();

        var externalItem1 = ExternalTodoItemBuilder.Create()
            .WithId("external-item-1")
            .WithDescription("Existing Item")
            .WithCompleted(false)
            .WithCreatedAt(DateTime.UtcNow.AddHours(-1))
            .WithUpdatedAt(DateTime.UtcNow.AddHours(-1))
            .Build();

        var externalItem2 = ExternalTodoItemBuilder.Create()
            .WithId("external-item-2")
            .WithDescription("New External Item")
            .WithCompleted(true)
            .WithCreatedAt(DateTime.UtcNow)
            .WithUpdatedAt(DateTime.UtcNow)
            .Build();

        var externalTodoList = ExternalTodoListBuilder.Create()
            .WithId("external-123")
            .WithName("Existing List")
            .WithCreatedAt(DateTime.UtcNow.AddHours(-2))
            .WithUpdatedAt(DateTime.UtcNow.AddHours(-1))
            .WithItem(externalItem1)
            .WithItem(externalItem2)
            .Build();

        _mockExternalClient.Setup(x => x.GetTodoListsAsync())
            .ReturnsAsync(new List<ExternalTodoList> { externalTodoList });

        // Setup conflict resolver mocks to allow proper syncing
        _mockConflictResolver.Setup(x => x.ResolveTodoListConflict(
                It.IsAny<TodoList>(),
                It.IsAny<ExternalTodoList>(),
                It.IsAny<ConflictResolutionStrategy>()))
            .Returns(new ConflictInfo
            {
                EntityType = "TodoList",
                Resolution = ConflictResolutionStrategy.ExternalWins,
                ResolutionReason = string.Empty
            });

        _mockConflictResolver.Setup(x => x.ResolveTodoItemConflict(
                It.IsAny<TodoItem>(),
                It.IsAny<ExternalTodoItem>(),
                It.IsAny<ConflictResolutionStrategy>()))
            .Returns(new ConflictInfo
            {
                EntityType = "TodoItem",
                Resolution = ConflictResolutionStrategy.ExternalWins,
                ResolutionReason = string.Empty
            });

        _mockConflictResolver.Setup(x => x.ApplyResolution(
                It.IsAny<TodoList>(),
                It.IsAny<ExternalTodoList>(),
                It.IsAny<ConflictInfo>()));

        _mockConflictResolver.Setup(x => x.ApplyResolution(
                It.IsAny<TodoItem>(),
                It.IsAny<ExternalTodoItem>(),
                It.IsAny<ConflictInfo>()));

        // Act
        await _syncService.SyncTodoListsFromExternalAsync();

        // Assert
        var updatedTodoList = await _context.TodoList
            .Include(tl => tl.Items)
            .FirstAsync(tl => tl.ExternalId == "external-123");

        Assert.Equal(2, updatedTodoList.Items.Count);

        var newItem = updatedTodoList.Items.FirstOrDefault(i => i.ExternalId == "external-item-2");
        Assert.NotNull(newItem);
        Assert.Equal("New External Item", newItem.Description);
        Assert.True(newItem.IsCompleted);
    }

    public void Dispose()
    {
        _context?.Dispose();
    }
}
