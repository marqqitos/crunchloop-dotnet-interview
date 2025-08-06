using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using TodoApi.Configuration;

namespace TodoApi.Tests.Services;

public class SyncBackgroundServiceTests
{
    private readonly Mock<ILogger<SyncBackgroundService>> _mockLogger;
    private readonly Mock<IServiceScopeFactory> _mockServiceScopeFactory;
    private readonly Mock<IServiceScope> _mockServiceScope;
    private readonly Mock<IServiceProvider> _mockServiceProvider;
    private readonly Mock<ISyncService> _mockSyncService;
    private readonly SyncOptions _syncOptions;

    public SyncBackgroundServiceTests()
    {
        _mockSyncService = new Mock<ISyncService>();
        _mockLogger = new Mock<ILogger<SyncBackgroundService>>();
        _mockServiceProvider = new Mock<IServiceProvider>();
        _mockServiceScope = new Mock<IServiceScope>();
        _mockServiceScopeFactory = new Mock<IServiceScopeFactory>();

        _syncOptions = new SyncOptions
        {
            SyncIntervalMinutes = 1, // Use 1 minute for faster tests
            EnableBackgroundSync = true,
            MaxSyncDurationMinutes = 10,
            SyncOnStartup = false
        };

        // Setup service scope factory
        _mockServiceScope.Setup(x => x.ServiceProvider).Returns(_mockServiceProvider.Object);
        _mockServiceScopeFactory.Setup(x => x.CreateScope()).Returns(_mockServiceScope.Object);
        _mockServiceProvider.Setup(x => x.GetService(typeof(ISyncService))).Returns(_mockSyncService.Object);
    }

    [Fact]
    public async Task ExecuteAsync_ShouldPerformSyncAtConfiguredInterval()
    {
        // Arrange
        var mockOptions = new Mock<IOptions<SyncOptions>>();
        mockOptions.Setup(x => x.Value).Returns(_syncOptions);

        var service = new SyncBackgroundService(
            _mockLogger.Object,
            mockOptions.Object,
            _mockServiceScopeFactory.Object);

        var cancellationTokenSource = new CancellationTokenSource();
        cancellationTokenSource.CancelAfter(TimeSpan.FromSeconds(2)); // Cancel after 2 seconds

        // Act
        await service.StartAsync(cancellationTokenSource.Token);
        await Task.Delay(TimeSpan.FromSeconds(3)); // Wait a bit longer to ensure sync is called
        await service.StopAsync(cancellationTokenSource.Token);

        // Assert
        _mockSyncService.Verify(x => x.PerformFullSyncAsync(), Times.AtLeast(1));
    }

    [Fact]
    public async Task ExecuteAsync_ShouldHandleSyncExceptionsGracefully()
    {
        // Arrange
        var mockOptions = new Mock<IOptions<SyncOptions>>();
        mockOptions.Setup(x => x.Value).Returns(_syncOptions);

        _mockSyncService.Setup(x => x.PerformFullSyncAsync())
            .ThrowsAsync(new InvalidOperationException("Test exception"));

        var service = new SyncBackgroundService(
            _mockLogger.Object,
            mockOptions.Object,
            _mockServiceScopeFactory.Object);

        var cancellationTokenSource = new CancellationTokenSource();
        cancellationTokenSource.CancelAfter(TimeSpan.FromSeconds(2));

        // Act & Assert - Should not throw
        await service.StartAsync(cancellationTokenSource.Token);
        await Task.Delay(TimeSpan.FromSeconds(3));
        await service.StopAsync(cancellationTokenSource.Token);

        // Verify that the service continued running despite the exception
        _mockSyncService.Verify(x => x.PerformFullSyncAsync(), Times.AtLeast(1));
    }

    [Fact]
    public async Task ExecuteAsync_ShouldRespectCancellationToken()
    {
        // Arrange
        var mockOptions = new Mock<IOptions<SyncOptions>>();
        mockOptions.Setup(x => x.Value).Returns(_syncOptions);

        var service = new SyncBackgroundService(
            _mockLogger.Object,
            mockOptions.Object,
            _mockServiceScopeFactory.Object);

        var cancellationTokenSource = new CancellationTokenSource();

        // Act
        await service.StartAsync(cancellationTokenSource.Token);
        cancellationTokenSource.Cancel();
        await service.StopAsync(cancellationTokenSource.Token);

        // Assert - Service should stop gracefully
        _mockSyncService.Verify(x => x.PerformFullSyncAsync(), Times.AtMost(1));
    }

    [Fact]
    public void Constructor_ShouldAcceptValidParameters()
    {
        // Arrange
        var mockOptions = new Mock<IOptions<SyncOptions>>();
        mockOptions.Setup(x => x.Value).Returns(_syncOptions);

        // Act & Assert - Should not throw
        var service = new SyncBackgroundService(
            _mockLogger.Object,
            mockOptions.Object,
            _mockServiceScopeFactory.Object);

        Assert.NotNull(service);
    }

    [Fact]
    public async Task StopAsync_ShouldLogShutdownMessage()
    {
        // Arrange
        var mockOptions = new Mock<IOptions<SyncOptions>>();
        mockOptions.Setup(x => x.Value).Returns(_syncOptions);

        var service = new SyncBackgroundService(
            _mockLogger.Object,
            mockOptions.Object,
            _mockServiceScopeFactory.Object);

        // Act
        await service.StartAsync(CancellationToken.None);
        await service.StopAsync(CancellationToken.None);

        // Assert
        _mockLogger.Verify(
            x => x.Log(
                LogLevel.Information,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString().Contains("stopping")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception, string>>()),
            Times.Once);
    }
}
