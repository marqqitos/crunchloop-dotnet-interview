using Microsoft.EntityFrameworkCore;

namespace TodoApi.Tests.Services;

public class DeltaSyncTests
{
    private readonly DbContextOptions<TodoContext> _options;
    private readonly Mock<IExternalTodoApiClient> _mockExternalClient;
    private readonly Mock<IConflictResolver> _mockConflictResolver;
    private readonly Mock<IRetryPolicyService> _mockRetryService;
    private readonly Mock<IChangeDetectionService> _mockChangeDetectionService;
    private readonly Mock<ISyncStateService> _mockSyncStateService;
    private readonly Mock<ILogger<TodoSyncService>> _mockLogger;

    public DeltaSyncTests()
    {
        _options = new DbContextOptionsBuilder<TodoContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
        _mockExternalClient = new Mock<IExternalTodoApiClient>();
        _mockConflictResolver = new Mock<IConflictResolver>();
        _mockRetryService = new Mock<IRetryPolicyService>();
        _mockChangeDetectionService = new Mock<IChangeDetectionService>();
        _mockSyncStateService = new Mock<ISyncStateService>();
        _mockLogger = new Mock<ILogger<TodoSyncService>>();

        // Setup retry policies to return the original task
        _mockRetryService.Setup(x => x.GetSyncRetryPolicy()).Returns(Polly.ResiliencePipeline.Empty);
        _mockRetryService.Setup(x => x.GetDatabaseRetryPolicy()).Returns(Polly.ResiliencePipeline.Empty);
        _mockRetryService.Setup(x => x.GetHttpRetryPolicy()).Returns(Polly.ResiliencePipeline.Empty);
    }

    [Fact]
    public async Task SyncTodoListsFromExternalAsync_WhenDeltaSyncAvailable_UsesDeltaSync()
    {
        // Arrange
        using var context = new TodoContext(_options);
        var syncService = new TodoSyncService(
            context,
            _mockExternalClient.Object,
            _mockConflictResolver.Object,
            _mockRetryService.Object,
            _mockChangeDetectionService.Object,
            _mockSyncStateService.Object,
            _mockLogger.Object
        );

        var lastSyncTime = DateTime.UtcNow.AddHours(-1);
        var externalTodoList = new ExternalTodoList
        {
            Id = "ext-123",
            SourceId = "test-source",
            Name = "Test List",
            UpdatedAt = DateTime.UtcNow,
            Items = new List<ExternalTodoItem>
            {
                new() { Id = "item-1", Description = "Task 1", Completed = false }
            }
        };

        _mockSyncStateService.Setup(x => x.IsDeltaSyncAvailableAsync()).ReturnsAsync(true);
        _mockSyncStateService.Setup(x => x.GetLastSyncTimestampAsync()).ReturnsAsync(lastSyncTime);
        _mockExternalClient.Setup(x => x.GetTodoListsAsync())
            .ReturnsAsync(new List<ExternalTodoList> { externalTodoList });

        // Act
        await syncService.SyncTodoListsFromExternalAsync();

        // Assert
        _mockExternalClient.Verify(x => x.GetTodoListsAsync(), Times.Once);
        _mockSyncStateService.Verify(x => x.UpdateLastSyncTimestampAsync(It.IsAny<DateTime>()), Times.Once);
    }

    [Fact]
    public async Task SyncTodoListsFromExternalAsync_WhenDeltaSyncNotAvailable_UsesFullSync()
    {
        // Arrange
        using var context = new TodoContext(_options);
        var syncService = new TodoSyncService(
            context,
            _mockExternalClient.Object,
            _mockConflictResolver.Object,
            _mockRetryService.Object,
            _mockChangeDetectionService.Object,
            _mockSyncStateService.Object,
            _mockLogger.Object
        );

        var externalTodoList = new ExternalTodoList
        {
            Id = "ext-123",
            SourceId = "test-source",
            Name = "Test List",
            UpdatedAt = DateTime.UtcNow,
            Items = new List<ExternalTodoItem>()
        };

        _mockSyncStateService.Setup(x => x.IsDeltaSyncAvailableAsync()).ReturnsAsync(false);
        _mockExternalClient.Setup(x => x.GetTodoListsAsync())
            .ReturnsAsync(new List<ExternalTodoList> { externalTodoList });

        // Act
        await syncService.SyncTodoListsFromExternalAsync();

        // Assert
        _mockExternalClient.Verify(x => x.GetTodoListsAsync(), Times.Once);
        _mockSyncStateService.Verify(x => x.UpdateLastSyncTimestampAsync(It.IsAny<DateTime>()), Times.Once);
    }

    [Fact]
    public async Task SyncTodoListsFromExternalAsync_WhenNoChanges_DoesNotUpdateSyncTimestamp()
    {
        // Arrange
        using var context = new TodoContext(_options);
        var syncService = new TodoSyncService(
            context,
            _mockExternalClient.Object,
            _mockConflictResolver.Object,
            _mockRetryService.Object,
            _mockChangeDetectionService.Object,
            _mockSyncStateService.Object,
            _mockLogger.Object
        );

        _mockSyncStateService.Setup(x => x.IsDeltaSyncAvailableAsync()).ReturnsAsync(true);
        _mockSyncStateService.Setup(x => x.GetLastSyncTimestampAsync()).ReturnsAsync(DateTime.UtcNow.AddHours(-1));
        _mockExternalClient.Setup(x => x.GetTodoListsAsync())
            .ReturnsAsync(new List<ExternalTodoList>());

        // Act
        await syncService.SyncTodoListsFromExternalAsync();

        // Assert
        _mockSyncStateService.Verify(x => x.UpdateLastSyncTimestampAsync(It.IsAny<DateTime>()), Times.Never);
    }

    [Fact]
    public async Task SyncStateService_GetLastSyncTimestamp_ReturnsMostRecentTimestamp()
    {
        // Arrange
        using var context = new TodoContext(_options);
        var syncStateService = new SyncStateService(context, Mock.Of<ILogger<SyncStateService>>());
        
        var todoList = new TodoList
        {
            Name = "Test List",
            ExternalId = "ext-123",
            LastSyncedAt = DateTime.UtcNow.AddHours(-2)
        };
        
        var todoItem = new TodoItem
        {
            Description = "Test Item",
            ExternalId = "item-123",
            LastSyncedAt = DateTime.UtcNow.AddHours(-1),
            TodoListId = 1
        };

        context.TodoList.Add(todoList);
        context.TodoItem.Add(todoItem);
        await context.SaveChangesAsync();

        // Act
        var result = await syncStateService.GetLastSyncTimestampAsync();

        // Assert
        Assert.NotNull(result);
        Assert.Equal(todoItem.LastSyncedAt, result); // Should return the more recent timestamp
    }

    [Fact]
    public async Task SyncStateService_UpdateLastSyncTimestamp_UpdatesAllSyncedEntities()
    {
        // Arrange
        using var context = new TodoContext(_options);
        var syncStateService = new SyncStateService(context, Mock.Of<ILogger<SyncStateService>>());
        
        var todoList = new TodoList
        {
            Name = "Test List",
            ExternalId = "ext-123",
            LastSyncedAt = DateTime.UtcNow.AddHours(-1)
        };
        
        var todoItem = new TodoItem
        {
            Description = "Test Item",
            ExternalId = "item-123",
            LastSyncedAt = DateTime.UtcNow.AddHours(-1),
            TodoListId = 1
        };

        context.TodoList.Add(todoList);
        context.TodoItem.Add(todoItem);
        await context.SaveChangesAsync();

        var newSyncTime = DateTime.UtcNow;

        // Act
        await syncStateService.UpdateLastSyncTimestampAsync(newSyncTime);

        // Assert
        var updatedTodoList = await context.TodoList.FindAsync(todoList.Id);
        var updatedTodoItem = await context.TodoItem.FindAsync(todoItem.Id);
        
        Assert.NotNull(updatedTodoList);
        Assert.NotNull(updatedTodoItem);
        Assert.Equal(newSyncTime, updatedTodoList.LastSyncedAt);
        Assert.Equal(newSyncTime, updatedTodoItem.LastSyncedAt);
    }

    [Fact]
    public async Task SyncStateService_IsDeltaSyncAvailable_ReturnsTrueWhenLastSyncExists()
    {
        // Arrange
        using var context = new TodoContext(_options);
        var syncStateService = new SyncStateService(context, Mock.Of<ILogger<SyncStateService>>());
        
        var todoList = new TodoList
        {
            Name = "Test List",
            ExternalId = "ext-123",
            LastSyncedAt = DateTime.UtcNow.AddHours(-1)
        };

        context.TodoList.Add(todoList);
        await context.SaveChangesAsync();

        // Act
        var result = await syncStateService.IsDeltaSyncAvailableAsync();

        // Assert
        Assert.True(result);
    }

    [Fact]
    public async Task SyncStateService_IsDeltaSyncAvailable_ReturnsFalseWhenNoLastSync()
    {
        // Arrange
        using var context = new TodoContext(_options);
        var syncStateService = new SyncStateService(context, Mock.Of<ILogger<SyncStateService>>());
        
        var todoList = new TodoList
        {
            Name = "Test List",
            ExternalId = "ext-123",
            LastSyncedAt = null // No previous sync
        };

        context.TodoList.Add(todoList);
        await context.SaveChangesAsync();

        // Act
        var result = await syncStateService.IsDeltaSyncAvailableAsync();

        // Assert
        Assert.False(result);
    }
} 