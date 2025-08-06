using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Moq.Protected;
using System.Net;
using System.Text.Json;
using TodoApi.Configuration;
using TodoApi.Tests.Builders;

namespace TodoApi.Tests.Integration;

public class RetryIntegrationTests : IAsyncDisposable
{
    private readonly TodoContext _context;
    private readonly Mock<IExternalTodoApiClient> _mockExternalClient;
    private readonly Mock<IConflictResolver> _mockConflictResolver;
    private readonly Mock<ILogger<TodoSyncService>> _mockSyncLogger;
    private readonly IRetryPolicyService _retryPolicyService;
    private readonly TodoSyncService _syncService;

    public RetryIntegrationTests()
    {
        var options = new DbContextOptionsBuilder<TodoContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        _context = new TodoContext(options);
        _mockExternalClient = new Mock<IExternalTodoApiClient>();
        _mockConflictResolver = new Mock<IConflictResolver>();
        _mockSyncLogger = new Mock<ILogger<TodoSyncService>>();

        // Setup retry policy service
        var retryOptions = new RetryOptions
        {
            MaxRetryAttempts = 2,
            BaseDelayMs = 50, // Faster for tests
            MaxDelayMs = 200,
            EnableRetries = true
        };
        var mockRetryOptions = new Mock<IOptions<RetryOptions>>();
        mockRetryOptions.Setup(x => x.Value).Returns(retryOptions);
        var mockRetryLogger = new Mock<ILogger<RetryPolicyService>>();

        _retryPolicyService = new RetryPolicyService(mockRetryOptions.Object, mockRetryLogger.Object);

        _mockExternalClient.Setup(x => x.SourceId).Returns("retry-test-source");

        _syncService = new TodoSyncService(
            _context,
            _mockExternalClient.Object,
            _mockConflictResolver.Object,
            _retryPolicyService,
            _mockSyncLogger.Object);
    }

    [Fact]
    public async Task SyncToExternal_WithTransientFailureThenSuccess_ShouldRetryAndSucceed()
    {
        // Arrange
        var todoList = TodoListBuilder.Create()
            .WithName("Test List")
            .WithItem(TodoItemBuilder.Create().WithDescription("Test Item").Build())
            .Build();

        _context.TodoList.Add(todoList);
        await _context.SaveChangesAsync();

        var callCount = 0;
        var expectedResult = ExternalTodoListBuilder.Create()
            .WithId("ext-123")
            .WithName("Test List")
            .WithItem(ExternalTodoItemBuilder.Create()
                .WithId("ext-item-456")
                .WithDescription("Test Item")
                .Build())
            .Build();

        _mockExternalClient.Setup(x => x.CreateTodoListAsync(It.IsAny<CreateExternalTodoList>()))
            .Returns(() =>
            {
                callCount++;
                if (callCount == 1)
                {
                    throw new HttpRequestException("Transient network error");
                }
                return Task.FromResult(expectedResult);
            });

        // Act
        await _syncService.SyncTodoListsToExternalAsync();

        // Assert
        Assert.Equal(2, callCount);

        var syncedTodoList = await _context.TodoList.FirstAsync();
        Assert.Equal("ext-123", syncedTodoList.ExternalId);

        _mockExternalClient.Verify(x => x.CreateTodoListAsync(It.IsAny<CreateExternalTodoList>()), Times.Exactly(2));
    }

    [Fact]
    public async Task SyncToExternal_WithPersistentFailure_ShouldFailAfterRetries()
    {
        // Arrange
        var todoList = TodoListBuilder.Create()
            .WithName("Test List")
            .Build();

        _context.TodoList.Add(todoList);
        await _context.SaveChangesAsync();

        var callCount = 0;
        _mockExternalClient.Setup(x => x.CreateTodoListAsync(It.IsAny<CreateExternalTodoList>()))
            .Returns(() =>
            {
                callCount++;
                throw new HttpRequestException("Persistent network error");
            });

        // Act & Assert
        await _syncService.SyncTodoListsToExternalAsync();

        // Should have retried the configured number of times
        Assert.Equal(3, callCount); // Initial attempt + 2 retries

        // TodoList should still not have ExternalId
        var todoListAfterSync = await _context.TodoList.FirstAsync();
        Assert.Null(todoListAfterSync.ExternalId);

        _mockExternalClient.Verify(x => x.CreateTodoListAsync(It.IsAny<CreateExternalTodoList>()), Times.Exactly(3));
    }

    [Fact]
    public async Task SyncFromExternal_WithTransientFailureThenSuccess_ShouldRetryAndSucceed()
    {
        // Arrange
        var externalTodoList = ExternalTodoListBuilder.Create()
            .WithId("ext-123")
            .WithName("External List")
            .Build();

        var callCount = 0;
        var (httpClient, mockHandler) = CreateMockedHttpClient();
        mockHandler.Protected()
            .Setup<Task<HttpResponseMessage>>("SendAsync", ItExpr.IsAny<HttpRequestMessage>(), ItExpr.IsAny<CancellationToken>())
            .Returns<HttpRequestMessage, CancellationToken>((request, token) =>
            {
                callCount++;
                if (callCount == 1)
                {
                    throw new TaskCanceledException("Request timeout");
                }
                var response = new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(JsonSerializer.Serialize(new List<ExternalTodoList> { externalTodoList }))
                };
                return Task.FromResult(response);
            });

        var realExternalClient = CreateRealExternalClient(httpClient);
        var realSyncService = CreateSyncServiceWithRealExternalClient(realExternalClient);

        // Act
        await realSyncService.SyncTodoListsFromExternalAsync();

        // Assert
        Assert.Equal(2, callCount);

        var localTodoList = await _context.TodoList.FirstOrDefaultAsync();
        Assert.NotNull(localTodoList);
        Assert.Equal("External List", localTodoList.Name);
        Assert.Equal("ext-123", localTodoList.ExternalId);

        mockHandler.Protected().Verify("SendAsync", Times.Exactly(2), ItExpr.IsAny<HttpRequestMessage>(), ItExpr.IsAny<CancellationToken>());
    }



    [Fact]
    public async Task SyncFromExternal_WithServerError_ShouldRetryAndEventuallySucceed()
    {
        // Arrange
        var externalTodoList = ExternalTodoListBuilder.Create()
            .WithId("ext-456")
            .WithName("Server Error Recovery Test")
            .Build();

        var callCount = 0;
        var (httpClient, mockHandler) = CreateMockedHttpClient();
        mockHandler.Protected()
            .Setup<Task<HttpResponseMessage>>("SendAsync", ItExpr.IsAny<HttpRequestMessage>(), ItExpr.IsAny<CancellationToken>())
            .Returns<HttpRequestMessage, CancellationToken>((request, token) =>
            {
                callCount++;
                if (callCount <= 2)
                {
                    // Simulate server errors that should be retried
                    throw new HttpRequestException("Server returned 500");
                }
                var response = new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(JsonSerializer.Serialize(new List<ExternalTodoList> { externalTodoList }))
                };
                return Task.FromResult(response);
            });

        var realExternalClient = CreateRealExternalClient(httpClient);
        var realSyncService = CreateSyncServiceWithRealExternalClient(realExternalClient);

        // Act
        await realSyncService.SyncTodoListsFromExternalAsync();

        // Assert
        Assert.Equal(3, callCount);

        var localTodoList = await _context.TodoList.FirstOrDefaultAsync();
        Assert.NotNull(localTodoList);
        Assert.Equal("Server Error Recovery Test", localTodoList.Name);

        mockHandler.Protected().Verify("SendAsync", Times.Exactly(3), ItExpr.IsAny<HttpRequestMessage>(), ItExpr.IsAny<CancellationToken>());
    }

    private (HttpClient httpClient, Mock<HttpMessageHandler> mockHandler) CreateMockedHttpClient()
    {
        var mockHandler = new Mock<HttpMessageHandler>();
        var httpClient = new HttpClient(mockHandler.Object)
        {
            BaseAddress = new Uri("http://test.com")
        };
        return (httpClient, mockHandler);
    }

    private ExternalTodoApiClient CreateRealExternalClient(HttpClient httpClient)
    {
        var mockHttpLogger = new Mock<ILogger<ExternalTodoApiClient>>();
        var externalApiOptions = new ExternalApiOptions
        {
            BaseUrl = "http://test.com",
            TimeoutSeconds = 30,
            SourceId = "test-source"
        };
        var mockExternalApiOptions = new Mock<IOptions<ExternalApiOptions>>();
        mockExternalApiOptions.Setup(x => x.Value).Returns(externalApiOptions);

        return new ExternalTodoApiClient(
            httpClient,
            mockHttpLogger.Object,
            mockExternalApiOptions.Object,
            _retryPolicyService);
    }

    private TodoSyncService CreateSyncServiceWithRealExternalClient(ExternalTodoApiClient externalClient)
    {
        return new TodoSyncService(
            _context,
            externalClient,
            _mockConflictResolver.Object,
            _retryPolicyService,
            _mockSyncLogger.Object);
    }

    public async ValueTask DisposeAsync()
    {
        await _context.DisposeAsync();
    }
}
