using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using TodoApi.Configuration;

namespace TodoApi.Tests.Integration;

public class SyncBackgroundServiceIntegrationTests : IDisposable
{
    private readonly ServiceProvider _serviceProvider;
    private readonly TodoContext _context;
    private readonly SyncBackgroundService _backgroundService;
    private readonly Mock<IExternalTodoApiClient> _mockExternalApiClient;

    public SyncBackgroundServiceIntegrationTests()
    {
        // Setup in-memory database
        var services = new ServiceCollection();
        services.AddDbContext<TodoContext>(options =>
            options.UseInMemoryDatabase("TestDatabase_" + Guid.NewGuid()));

        // Setup configuration
        var syncOptions = new SyncOptions
        {
            SyncIntervalMinutes = 1, // Use 1 minute for faster tests
            EnableBackgroundSync = true,
            MaxSyncDurationMinutes = 10,
            SyncOnStartup = false
        };

        services.Configure<SyncOptions>(options =>
        {
            options.SyncIntervalMinutes = syncOptions.SyncIntervalMinutes;
            options.EnableBackgroundSync = syncOptions.EnableBackgroundSync;
            options.MaxSyncDurationMinutes = syncOptions.MaxSyncDurationMinutes;
            options.SyncOnStartup = syncOptions.SyncOnStartup;
        });

        // Setup external API client mock
        _mockExternalApiClient = new Mock<IExternalTodoApiClient>();
        _mockExternalApiClient.Setup(x => x.SourceId).Returns("test-source");

        // Mock GetTodoListsAsync to return empty list by default
        _mockExternalApiClient.Setup(x => x.GetTodoListsAsync())
            .ReturnsAsync(new List<ExternalTodoList>());

        services.AddScoped<IExternalTodoApiClient>(_ => _mockExternalApiClient.Object);
        services.AddScoped<IRetryPolicyService, RetryPolicyService>();
        services.AddScoped<IConflictResolver, ConflictResolver>();
        services.AddScoped<IChangeDetectionService, ChangeDetectionService>();
        services.AddScoped<ISyncStateService, SyncStateService>();
        services.AddScoped<ISyncService, TodoSyncService>();
        services.AddLogging(builder => builder.AddConsole());

        _serviceProvider = services.BuildServiceProvider();
        _context = _serviceProvider.GetRequiredService<TodoContext>();
        _context.Database.EnsureCreated();

        // Create the background service
        var syncService = _serviceProvider.GetRequiredService<ISyncService>();
        var logger = _serviceProvider.GetRequiredService<ILogger<SyncBackgroundService>>();
        var options = _serviceProvider.GetRequiredService<IOptions<SyncOptions>>();

        _backgroundService = new SyncBackgroundService(
            logger,
            options,
            _serviceProvider.GetRequiredService<IServiceScopeFactory>());
    }

    [Fact]
    public async Task BackgroundService_ShouldRespectCancellationToken()
    {
        // Arrange
        var cancellationTokenSource = new CancellationTokenSource();

        // Act
        await _backgroundService.StartAsync(cancellationTokenSource.Token);
        cancellationTokenSource.Cancel();
        await _backgroundService.StopAsync(cancellationTokenSource.Token);

        // Assert - Service should stop gracefully
        _mockExternalApiClient.Verify(x => x.CreateTodoListAsync(It.IsAny<Dtos.External.CreateExternalTodoList>()), Times.Never);
    }

    public void Dispose()
    {
        _backgroundService?.Dispose();
        _context?.Dispose();
        _serviceProvider?.Dispose();
    }
}
