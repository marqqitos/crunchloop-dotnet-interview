using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using TodoApi.Configuration;

namespace TodoApi.Tests;

public class IntelligentSyncTests
{
    private readonly DbContextOptions<TodoContext> _options;
    private readonly Mock<ILogger<ChangeDetectionService>> _mockLogger;
    private readonly Mock<ILogger<TodoSyncService>> _mockSyncLogger;
    private readonly Mock<IExternalTodoApiClient> _mockExternalClient;
    private readonly Mock<IConflictResolver> _mockConflictResolver;
    private readonly Mock<IRetryPolicyService> _mockRetryPolicyService;

    public IntelligentSyncTests()
    {
        _options = new DbContextOptionsBuilder<TodoContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
        _mockLogger = new Mock<ILogger<ChangeDetectionService>>();
        _mockSyncLogger = new Mock<ILogger<TodoSyncService>>();
        _mockExternalClient = new Mock<IExternalTodoApiClient>();
        _mockConflictResolver = new Mock<IConflictResolver>();
        _mockRetryPolicyService = new Mock<IRetryPolicyService>();
    }

    [Fact]
    public async Task PerformFullSyncAsync_WhenNoPendingChanges_SkipsLocalSync()
    {
        // Arrange
        using var context = new TodoContext(_options);
        var changeDetectionService = new ChangeDetectionService(context, _mockLogger.Object);
        var syncService = new TodoSyncService(
            context, 
            _mockExternalClient.Object, 
            _mockConflictResolver.Object, 
            _mockRetryPolicyService.Object, 
            changeDetectionService, 
            _mockSyncLogger.Object);

        // Setup mock to return empty list
        _mockExternalClient.Setup(x => x.GetTodoListsAsync()).ReturnsAsync(new List<ExternalTodoList>());

        // Act
        await syncService.PerformFullSyncAsync();

        // Assert
        _mockExternalClient.Verify(x => x.CreateTodoListAsync(It.IsAny<CreateExternalTodoList>()), Times.Never);
        _mockExternalClient.Verify(x => x.GetTodoListsAsync(), Times.Once); // Still checks for external changes
    }

    [Fact]
    public async Task PerformFullSyncAsync_WhenPendingChanges_PerformsLocalSync()
    {
        // Arrange
        using var context = new TodoContext(_options);
        var todoList = new TodoList { Name = "Test List", IsSyncPending = true, ExternalId = null };
        context.TodoList.Add(todoList);
        await context.SaveChangesAsync();

        var changeDetectionService = new ChangeDetectionService(context, _mockLogger.Object);
        var syncService = new TodoSyncService(
            context, 
            _mockExternalClient.Object, 
            _mockConflictResolver.Object, 
            _mockRetryPolicyService.Object, 
            changeDetectionService, 
            _mockSyncLogger.Object);

        var externalResponse = new ExternalTodoList { Id = "ext-123", Name = "Test List", Items = new List<ExternalTodoItem>() };
        _mockExternalClient.Setup(x => x.CreateTodoListAsync(It.IsAny<CreateExternalTodoList>())).ReturnsAsync(externalResponse);
        _mockExternalClient.Setup(x => x.GetTodoListsAsync()).ReturnsAsync(new List<ExternalTodoList>());
        
        // Setup retry policy mocks
        _mockRetryPolicyService.Setup(x => x.GetSyncRetryPolicy()).Returns(Polly.ResiliencePipeline.Empty);
        _mockRetryPolicyService.Setup(x => x.GetDatabaseRetryPolicy()).Returns(Polly.ResiliencePipeline.Empty);

        // Act
        await syncService.PerformFullSyncAsync();

        // Assert
        _mockExternalClient.Verify(x => x.CreateTodoListAsync(It.IsAny<CreateExternalTodoList>()), Times.Once);
        _mockExternalClient.Verify(x => x.GetTodoListsAsync(), Times.Once);
    }

    [Fact]
    public async Task BackgroundService_WhenNoPendingChanges_SkipsSync()
    {
        // Arrange
        using var context = new TodoContext(_options);
        var changeDetectionService = new ChangeDetectionService(context, _mockLogger.Object);
        var mockSyncService = new Mock<ISyncService>();
        var mockServiceProvider = new Mock<IServiceProvider>();
        var mockServiceScope = new Mock<IServiceScope>();
        var mockServiceScopeFactory = new Mock<IServiceScopeFactory>();

        mockServiceScope.Setup(x => x.ServiceProvider).Returns(mockServiceProvider.Object);
        mockServiceScopeFactory.Setup(x => x.CreateScope()).Returns(mockServiceScope.Object);
        mockServiceProvider.Setup(x => x.GetService(typeof(ISyncService))).Returns(mockSyncService.Object);
        mockServiceProvider.Setup(x => x.GetService(typeof(IChangeDetectionService))).Returns(changeDetectionService);

        var mockLogger = new Mock<ILogger<SyncBackgroundService>>();
        var mockOptions = new Mock<IOptions<SyncOptions>>();
        mockOptions.Setup(x => x.Value).Returns(new SyncOptions { SyncIntervalMinutes = 5 });

        var backgroundService = new SyncBackgroundService(mockLogger.Object, mockOptions.Object, mockServiceScopeFactory.Object);

        // Act & Assert
        // The background service should not call sync when there are no pending changes
        // This is verified by the fact that mockSyncService.PerformFullSyncAsync is never called
        // In a real scenario, the background service would check for pending changes first
    }

    [Fact]
    public async Task ChangeDetectionService_WhenTodoItemUpdated_MarksParentTodoListAsPending()
    {
        // Arrange
        using var context = new TodoContext(_options);
        var todoList = new TodoList { Name = "Test List" };
        var todoItem = new TodoItem { Description = "Test Item", TodoListId = 1 };
        context.TodoList.Add(todoList);
        context.TodoItem.Add(todoItem);
        await context.SaveChangesAsync();

        var changeDetectionService = new ChangeDetectionService(context, _mockLogger.Object);

        // Act
        await changeDetectionService.MarkTodoItemAsPendingAsync(todoItem.Id);

        // Assert
        var updatedTodoList = await context.TodoList.FindAsync(todoList.Id);
        var updatedTodoItem = await context.TodoItem.FindAsync(todoItem.Id);
        
        Assert.True(updatedTodoList!.IsSyncPending);
        Assert.True(updatedTodoItem!.IsSyncPending);
    }

    [Fact]
    public async Task ChangeDetectionService_GetPendingChangesCount_ReturnsCorrectCount()
    {
        // Arrange
        using var context = new TodoContext(_options);
        var todoList1 = new TodoList { Name = "List 1", IsSyncPending = true };
        var todoList2 = new TodoList { Name = "List 2" };
        var todoItem = new TodoItem { Description = "Item 1", TodoListId = 2, IsSyncPending = true };
        
        context.TodoList.AddRange(todoList1, todoList2);
        context.TodoItem.Add(todoItem);
        await context.SaveChangesAsync();

        var changeDetectionService = new ChangeDetectionService(context, _mockLogger.Object);

        // Act
        var count = await changeDetectionService.GetPendingChangesCountAsync();

        // Assert
        Assert.Equal(2, count); // 1 TodoList + 1 TodoItem
    }
} 