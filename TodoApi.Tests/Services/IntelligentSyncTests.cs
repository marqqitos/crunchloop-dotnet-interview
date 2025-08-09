using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using TodoApi.Configuration;

namespace TodoApi.Tests;

public class IntelligentSyncTests
{
    private readonly TodoContext _dbContext;
    private readonly Mock<ILogger<TodoListSyncService>> _mockSyncLogger;
    private readonly Mock<IExternalTodoApiClient> _mockExternalClient;
    private readonly Mock<IConflictResolver<TodoList, ExternalTodoList>> _mockTodoListConflictResolver;
    private readonly Mock<IConflictResolver<TodoItem, ExternalTodoItem>> _mockTodoItemConflictResolver;
    private readonly Mock<IRetryPolicyService> _mockRetryPolicyService;
    private readonly Mock<ISyncStateService> _mockSyncStateService;
	private readonly Mock<ITodoListService> _mockTodoListService;
	private readonly Mock<ITodoItemService> _mockTodoItemService;

	private ISyncService _sut;

    public IntelligentSyncTests()
    {
        var options = new DbContextOptionsBuilder<TodoContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
        _dbContext = new TodoContext(options);
        _mockSyncLogger = new Mock<ILogger<TodoListSyncService>>();
        _mockExternalClient = new Mock<IExternalTodoApiClient>();
        _mockTodoListConflictResolver = new Mock<IConflictResolver<TodoList, ExternalTodoList>>();
        _mockTodoItemConflictResolver = new Mock<IConflictResolver<TodoItem, ExternalTodoItem>>();
        _mockRetryPolicyService = new Mock<IRetryPolicyService>();
        _mockSyncStateService = new Mock<ISyncStateService>();
		_mockTodoListService = new Mock<ITodoListService>();
		_mockTodoItemService = new Mock<ITodoItemService>();

		_sut = new TodoListSyncService(
            _dbContext,
			_mockExternalClient.Object,
			            _mockTodoListConflictResolver.Object,
            _mockTodoItemConflictResolver.Object,
			_mockRetryPolicyService.Object,
			_mockTodoListService.Object,
			_mockTodoItemService.Object,
			_mockSyncStateService.Object,
			_mockSyncLogger.Object);
    }

    [Fact]
    public async Task PerformFullSyncAsync_WhenNoPendingChanges_SkipsLocalSync()
    {
        // Arrange

        // Setup mock to return empty list
        _mockExternalClient.Setup(x => x.GetTodoListsAsync()).ReturnsAsync(new List<ExternalTodoList>());
        _mockTodoListService.Setup(x => x.GetPendingChangesCountAsync()).ReturnsAsync(0);
        _mockTodoItemService.Setup(x => x.GetPendingChangesCountAsync()).ReturnsAsync(0);
        _mockSyncStateService.Setup(x => x.IsDeltaSyncAvailableAsync()).ReturnsAsync(false);

        // Act
        await _sut.PerformFullSyncAsync();

        // Assert
        _mockExternalClient.Verify(x => x.CreateTodoListAsync(It.IsAny<CreateExternalTodoList>()), Times.Never);
        _mockExternalClient.Verify(x => x.GetTodoListsAsync(), Times.Once); // Still checks for external changes
    }

    [Fact]
    public async Task PerformFullSyncAsync_WhenPendingChanges_PerformsLocalSync()
    {
        // Arrange
        var todoList = new TodoList { Name = "Test List", IsSyncPending = true, ExternalId = null };
        _dbContext.TodoList.Add(todoList);
        await _dbContext.SaveChangesAsync();

        var externalResponse = new ExternalTodoList { Id = "ext-123", Name = "Test List", Items = new List<ExternalTodoItem>() };
        _mockExternalClient.Setup(x => x.CreateTodoListAsync(It.IsAny<CreateExternalTodoList>())).ReturnsAsync(externalResponse);
        _mockExternalClient.Setup(x => x.GetTodoListsAsync()).ReturnsAsync(new List<ExternalTodoList>());

        // Setup retry policy mocks
        _mockRetryPolicyService.Setup(x => x.GetSyncRetryPolicy()).Returns(Polly.ResiliencePipeline.Empty);
        _mockRetryPolicyService.Setup(x => x.GetDatabaseRetryPolicy()).Returns(Polly.ResiliencePipeline.Empty);
        _mockTodoListService.Setup(x => x.GetPendingChangesCountAsync()).ReturnsAsync(0);
        _mockTodoItemService.Setup(x => x.GetPendingChangesCountAsync()).ReturnsAsync(0);
        _mockSyncStateService.Setup(x => x.IsDeltaSyncAvailableAsync()).ReturnsAsync(false);

        // Act
        await _sut.PerformFullSyncAsync();

        // Assert
        _mockExternalClient.Verify(x => x.CreateTodoListAsync(It.IsAny<CreateExternalTodoList>()), Times.Once);
        _mockExternalClient.Verify(x => x.GetTodoListsAsync(), Times.Once);
    }
}
