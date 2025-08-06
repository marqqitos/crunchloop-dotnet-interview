using Microsoft.EntityFrameworkCore;

namespace TodoApi.Tests.Services;

public class TodoSyncServiceTests : IDisposable
{
    private readonly TodoContext _context;
    private readonly Mock<IExternalTodoApiClient> _mockExternalClient;
    private readonly Mock<IConflictResolver> _mockConflictResolver;
    private readonly Mock<IRetryPolicyService> _mockRetryPolicyService;
    private readonly Mock<ILogger<TodoSyncService>> _mockLogger;
    private readonly TodoSyncService _syncService;

    public TodoSyncServiceTests()
    {
        // Setup in-memory database
        var options = new DbContextOptionsBuilder<TodoContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
        _context = new TodoContext(options);

        // Setup mocks
        _mockExternalClient = new Mock<IExternalTodoApiClient>();
        _mockConflictResolver = new Mock<IConflictResolver>();
        _mockRetryPolicyService = new Mock<IRetryPolicyService>();
        _mockLogger = new Mock<ILogger<TodoSyncService>>();

        // Setup mock client defaults
        _mockExternalClient.Setup(x => x.SourceId).Returns("test-source-id");
        
        // Setup retry policy mocks to return empty pipelines for tests
        _mockRetryPolicyService.Setup(x => x.GetHttpRetryPolicy()).Returns(Polly.ResiliencePipeline.Empty);
        _mockRetryPolicyService.Setup(x => x.GetDatabaseRetryPolicy()).Returns(Polly.ResiliencePipeline.Empty);
        _mockRetryPolicyService.Setup(x => x.GetSyncRetryPolicy()).Returns(Polly.ResiliencePipeline.Empty);

        _syncService = new TodoSyncService(_context, _mockExternalClient.Object, _mockConflictResolver.Object, _mockRetryPolicyService.Object, _mockLogger.Object);
    }

    [Fact]
    public async Task SyncTodoListsToExternalAsync_WithUnsyncedLists_ShouldSyncSuccessfully()
    {
        // Arrange
        var todoList = new TodoList
        {
            Name = "Test List",
            ExternalId = null, // Unsynced
            Items = new List<TodoItem>
            {
                new() { Description = "Task 1", IsCompleted = false },
                new() { Description = "Task 2", IsCompleted = true }
            }
        };
        _context.TodoList.Add(todoList);
        await _context.SaveChangesAsync();

        var externalResponse = new ExternalTodoList
        {
            Id = "ext-123",
            Name = "Test List",
            Items = new List<ExternalTodoItem>
            {
                new() { Id = "item-1", Description = "Task 1", Completed = false },
                new() { Id = "item-2", Description = "Task 2", Completed = true }
            }
        };

        _mockExternalClient
            .Setup(x => x.CreateTodoListAsync(It.IsAny<CreateExternalTodoList>()))
            .ReturnsAsync(externalResponse);

        // Act
        await _syncService.SyncTodoListsToExternalAsync();

        // Assert
        var updatedList = await _context.TodoList.Include(tl => tl.Items).FirstAsync();
        Assert.Equal("ext-123", updatedList.ExternalId);
        Assert.NotEqual(default(DateTime), updatedList.LastModified);

        // Verify items were matched and updated
        var item1 = updatedList.Items.First(i => i.Description == "Task 1");
        var item2 = updatedList.Items.First(i => i.Description == "Task 2");
        Assert.Equal("item-1", item1.ExternalId);
        Assert.Equal("item-2", item2.ExternalId);

        // Verify external client was called correctly
        _mockExternalClient.Verify(x => x.CreateTodoListAsync(It.Is<CreateExternalTodoList>(dto =>
            dto.Name == "Test List" &&
            dto.SourceId == "test-source-id" &&
            dto.Items.Count == 2
        )), Times.Once);
    }

    [Fact]
    public async Task SyncTodoListsToExternalAsync_WithNoUnsyncedLists_ShouldReturnEarly()
    {
        // Arrange
        var syncedTodoList = new TodoList
        {
            Name = "Already Synced",
            ExternalId = "already-synced-123" // Already has external ID
        };
        _context.TodoList.Add(syncedTodoList);
        await _context.SaveChangesAsync();

        // Act
        await _syncService.SyncTodoListsToExternalAsync();

        // Assert
        _mockExternalClient.Verify(x => x.CreateTodoListAsync(It.IsAny<CreateExternalTodoList>()), Times.Never);
        
        // Verify appropriate log message
        VerifyLogCalled(LogLevel.Information, "No unsynced TodoLists found");
    }

    [Fact]
    public async Task SyncTodoListsToExternalAsync_WithExternalApiFailure_ShouldLogErrorAndContinue()
    {
        // Arrange
        var todoList1 = new TodoList { Name = "List 1", ExternalId = null };
        var todoList2 = new TodoList { Name = "List 2", ExternalId = null };
        _context.TodoList.AddRange(todoList1, todoList2);
        await _context.SaveChangesAsync();

        _mockExternalClient
            .SetupSequence(x => x.CreateTodoListAsync(It.IsAny<CreateExternalTodoList>()))
            .ThrowsAsync(new HttpRequestException("External API error"))
            .ReturnsAsync(new ExternalTodoList { Id = "ext-456", Name = "List 2", Items = new List<ExternalTodoItem>() });

        // Act
        await _syncService.SyncTodoListsToExternalAsync();

        // Assert
        var lists = await _context.TodoList.ToListAsync();
        Assert.Null(lists.First(l => l.Name == "List 1").ExternalId); // Failed sync
        Assert.Equal("ext-456", lists.First(l => l.Name == "List 2").ExternalId); // Successful sync

        // Verify error was logged
        VerifyLogCalled(LogLevel.Error, "Failed to sync TodoList");
        
        // Verify completion log shows correct counts
        VerifyLogCalled(LogLevel.Information, "Sync completed. Success: 1, Failed: 1");
    }

    [Fact]
    public async Task SyncTodoListsToExternalAsync_WithItemDescriptionMismatch_ShouldHandleGracefully()
    {
        // Arrange
        var todoList = new TodoList
        {
            Name = "Test List",
            ExternalId = null,
            Items = new List<TodoItem>
            {
                new() { Description = "Local Task", IsCompleted = false }
            }
        };
        _context.TodoList.Add(todoList);
        await _context.SaveChangesAsync();

        var externalResponse = new ExternalTodoList
        {
            Id = "ext-123",
            Name = "Test List",
            Items = new List<ExternalTodoItem>
            {
                new() { Id = "item-1", Description = "Different Task", Completed = false }
            }
        };

        _mockExternalClient
            .Setup(x => x.CreateTodoListAsync(It.IsAny<CreateExternalTodoList>()))
            .ReturnsAsync(externalResponse);

        // Act
        await _syncService.SyncTodoListsToExternalAsync();

        // Assert
        var updatedList = await _context.TodoList.Include(tl => tl.Items).FirstAsync();
        Assert.Equal("ext-123", updatedList.ExternalId);
        
        // Local item should not have external ID due to description mismatch
        var localItem = updatedList.Items.First();
        Assert.Null(localItem.ExternalId);
    }

    [Fact]
    public async Task SyncTodoListsToExternalAsync_WithEmptyItemsList_ShouldSyncListOnly()
    {
        // Arrange
        var todoList = new TodoList
        {
            Name = "Empty List",
            ExternalId = null,
            Items = new List<TodoItem>()
        };
        _context.TodoList.Add(todoList);
        await _context.SaveChangesAsync();

        var externalResponse = new ExternalTodoList
        {
            Id = "ext-empty",
            Name = "Empty List",
            Items = new List<ExternalTodoItem>()
        };

        _mockExternalClient
            .Setup(x => x.CreateTodoListAsync(It.IsAny<CreateExternalTodoList>()))
            .ReturnsAsync(externalResponse);

        // Act
        await _syncService.SyncTodoListsToExternalAsync();

        // Assert
        var updatedList = await _context.TodoList.FirstAsync();
        Assert.Equal("ext-empty", updatedList.ExternalId);
        Assert.NotEqual(default(DateTime), updatedList.LastModified);
    }

    [Fact]
    public async Task SyncTodoListsToExternalAsync_WithDatabaseSaveFailure_ShouldThrow()
    {
        // Arrange - This test is harder to implement with InMemory DB
        // In a real scenario, you'd mock the context or use a database that can fail
        var todoList = new TodoList { Name = "Test", ExternalId = null };
        _context.TodoList.Add(todoList);
        await _context.SaveChangesAsync();

        _mockExternalClient
            .Setup(x => x.CreateTodoListAsync(It.IsAny<CreateExternalTodoList>()))
            .ReturnsAsync(new ExternalTodoList { Id = "ext-123", Name = "Test", Items = new List<ExternalTodoItem>() });

        // Dispose context to simulate DB failure
        _context.Dispose();

        // Act & Assert
        await Assert.ThrowsAsync<ObjectDisposedException>(
            () => _syncService.SyncTodoListsToExternalAsync());
    }

    [Fact]
    public async Task SyncTodoListsToExternalAsync_WithMultipleLists_ShouldSyncAllSuccessfully()
    {
        // Arrange
        var lists = new[]
        {
            new TodoList { Name = "List 1", ExternalId = null },
            new TodoList { Name = "List 2", ExternalId = null },
            new TodoList { Name = "List 3", ExternalId = null }
        };
        _context.TodoList.AddRange(lists);
        await _context.SaveChangesAsync();

        _mockExternalClient
            .Setup(x => x.CreateTodoListAsync(It.IsAny<CreateExternalTodoList>()))
            .ReturnsAsync((CreateExternalTodoList dto) => new ExternalTodoList 
            { 
                Id = $"ext-{dto.Name.Replace(' ', '-').ToLower()}", 
                Name = dto.Name, 
                Items = new List<ExternalTodoItem>() 
            });

        // Act
        await _syncService.SyncTodoListsToExternalAsync();

        // Assert
        var updatedLists = await _context.TodoList.ToListAsync();
        Assert.All(updatedLists, list => Assert.NotNull(list.ExternalId));
        Assert.Equal(3, updatedLists.Count(l => l.ExternalId != null));

        // Verify completion log
        VerifyLogCalled(LogLevel.Information, "Sync completed. Success: 3, Failed: 0");
    }

    private void VerifyLogCalled(LogLevel level, string message)
    {
        _mockLogger.Verify(
            x => x.Log(
                level,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains(message)),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.AtLeastOnce);
    }

    public void Dispose()
    {
        _context?.Dispose();
    }
}