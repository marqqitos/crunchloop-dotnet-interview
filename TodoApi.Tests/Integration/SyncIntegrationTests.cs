using Microsoft.EntityFrameworkCore;
using TodoApi.Tests.Builders;

namespace TodoApi.Tests.Integration;

/// <summary>
/// Integration tests that test the full sync flow with real database operations
/// </summary>
public class SyncIntegrationTests : IDisposable
{
    private readonly TodoContext _context;
    private readonly Mock<IExternalTodoApiClient> _mockExternalClient;
    private readonly Mock<ILogger<TodoSyncService>> _mockLogger;
    private readonly TodoSyncService _syncService;

    public SyncIntegrationTests()
    {
        var options = new DbContextOptionsBuilder<TodoContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
        _context = new TodoContext(options);

        _mockExternalClient = new Mock<IExternalTodoApiClient>();
        _mockLogger = new Mock<ILogger<TodoSyncService>>();
        
        _mockExternalClient.Setup(x => x.SourceId).Returns("integration-test-source");
        
        _syncService = new TodoSyncService(_context, _mockExternalClient.Object, _mockLogger.Object);
    }

    [Fact]
    public async Task FullSyncWorkflow_WithComplexScenario_ShouldHandleAllCases()
    {
        // Arrange - Create a complex scenario with multiple lists and items
        var list1 = TodoListBuilder.Create()
            .WithName("Shopping List")
            .WithItem(TodoItemBuilder.Create().WithDescription("Buy milk").WithCompleted(false).Build())
            .WithItem(TodoItemBuilder.Create().WithDescription("Buy bread").WithCompleted(true).Build())
            .Build();

        var list2 = TodoListBuilder.Create()
            .WithName("Work Tasks")
            .WithItem(TodoItemBuilder.Create().WithDescription("Review PR").WithCompleted(false).WithTodoListId(2).Build())
            .WithItem(TodoItemBuilder.Create().WithDescription("Deploy to prod").WithCompleted(false).WithTodoListId(2).Build())
            .WithItem(TodoItemBuilder.Create().WithDescription("Update docs").WithCompleted(true).WithTodoListId(2).Build())
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

    public void Dispose()
    {
        _context?.Dispose();
    }
}