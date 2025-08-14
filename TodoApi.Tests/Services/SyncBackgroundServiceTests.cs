using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using TodoApi.Configuration;
using TodoApi.Services.ExternalTodoApiClient;
using TodoApi.Services.SyncService;
using TodoApi.Services.TodoItemService;
using TodoApi.Services.TodoListService;

namespace TodoApi.Tests.Services;

public class SyncBackgroundServiceTests
{
    private readonly Mock<ILogger<SyncBackgroundService>> _mockLogger;
    private readonly Mock<ISyncService> _mockSyncService;
	private readonly Mock<ITodoListService> _mockTodoListService;
	private readonly Mock<ITodoItemService> _mockTodoItemService;
	private readonly Mock<IExternalTodoApiClient> _mockExternalApiClient;
    private readonly IServiceScopeFactory _serviceScopeFactory;
    private readonly SyncOptions _syncOptions;

	private SyncBackgroundService _sut;

    public SyncBackgroundServiceTests()
    {
        _mockSyncService = new Mock<ISyncService>();
        _mockLogger = new Mock<ILogger<SyncBackgroundService>>();
        _mockTodoListService = new Mock<ITodoListService>();
        _mockTodoItemService = new Mock<ITodoItemService>();
		_mockExternalApiClient = new Mock<IExternalTodoApiClient>();

        _syncOptions = new SyncOptions
        {
            SyncIntervalSeconds = 1, // Use 1 minute for faster tests
            EnableBackgroundSync = true,
            MaxSyncDurationMinutes = 10,
            SyncOnStartup = false
        };

        // Create a real service collection and register our mocked services
        var services = new ServiceCollection();
        services.AddScoped<ISyncService>(_ => _mockSyncService.Object);
        services.AddScoped<ITodoListService>(_ => _mockTodoListService.Object);
        services.AddScoped<ITodoItemService>(_ => _mockTodoItemService.Object);
        services.AddScoped<IExternalTodoApiClient>(_ => _mockExternalApiClient.Object);

        var serviceProvider = services.BuildServiceProvider();
        _serviceScopeFactory = serviceProvider.GetRequiredService<IServiceScopeFactory>();

		_sut = new SyncBackgroundService(
			_serviceScopeFactory,
			_mockLogger.Object,
			new OptionsWrapper<SyncOptions>(_syncOptions));
    }

    [Fact]
    public async Task ExecuteAsync_ShouldPerformSyncAtConfiguredInterval()
    {
        // Arrange
        var mockOptions = new Mock<IOptions<SyncOptions>>();
        mockOptions.Setup(x => x.Value).Returns(_syncOptions);

        var cancellationTokenSource = new CancellationTokenSource();
        cancellationTokenSource.CancelAfter(TimeSpan.FromSeconds(2)); // Cancel after 2 seconds

		_mockTodoListService.Setup(x => x.GetPendingSyncTodoLists()).ReturnsAsync(new List<TodoList>());

        // Act
        await _sut.StartAsync(cancellationTokenSource.Token);
        await Task.Delay(TimeSpan.FromSeconds(3)); // Wait a bit longer to ensure sync is called
        await _sut.StopAsync(cancellationTokenSource.Token);

        // Assert
        _mockSyncService.Verify(x => x.PerformFullSync(), Times.AtLeast(1));
    }

    [Fact]
    public async Task ExecuteAsync_ShouldHandleSyncExceptionsGracefully()
    {
        // Arrange
        var mockOptions = new Mock<IOptions<SyncOptions>>();
        mockOptions.Setup(x => x.Value).Returns(_syncOptions);

		_mockTodoListService.Setup(x => x.GetPendingSyncTodoLists()).ReturnsAsync(new List<TodoList>());

        _mockSyncService.Setup(x => x.PerformFullSync())
            .ThrowsAsync(new InvalidOperationException("Test exception"));

        var cancellationTokenSource = new CancellationTokenSource();
        cancellationTokenSource.CancelAfter(TimeSpan.FromSeconds(2));

        // Act & Assert - Should not throw
        await _sut.StartAsync(cancellationTokenSource.Token);
        await Task.Delay(TimeSpan.FromSeconds(3));
        await _sut.StopAsync(cancellationTokenSource.Token);

        // Verify that the service continued running despite the exception
        _mockSyncService.Verify(x => x.PerformFullSync(), Times.AtLeast(1));
    }

    [Fact]
    public async Task ExecuteAsync_ShouldRespectCancellationToken()
    {
        // Arrange
        var mockOptions = new Mock<IOptions<SyncOptions>>();
        mockOptions.Setup(x => x.Value).Returns(_syncOptions);

        var cancellationTokenSource = new CancellationTokenSource();

        // Act
        await _sut.StartAsync(cancellationTokenSource.Token);
        cancellationTokenSource.Cancel();
        await _sut.StopAsync(cancellationTokenSource.Token);

        // Assert - Service should stop gracefully
        _mockSyncService.Verify(x => x.PerformFullSync(), Times.AtMost(1));
    }

    [Fact]
    public async Task StopAsync_ShouldLogShutdownMessage()
    {
        // Arrange
        var mockOptions = new Mock<IOptions<SyncOptions>>();
        mockOptions.Setup(x => x.Value).Returns(_syncOptions);

        // Act
        await _sut.StartAsync(CancellationToken.None);
        await _sut.StopAsync(CancellationToken.None);

        // Assert
        _mockLogger.Verify(
            x => x.Log(
                LogLevel.Information,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v != null && v.ToString()!.Contains("stopping")),
                It.IsAny<Exception?>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }
}
