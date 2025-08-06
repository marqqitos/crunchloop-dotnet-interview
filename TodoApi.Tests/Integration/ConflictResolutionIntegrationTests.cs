using Microsoft.EntityFrameworkCore;
using TodoApi.Tests.Builders;

namespace TodoApi.Tests.Integration;

public class ConflictResolutionIntegrationTests : IAsyncDisposable
{
    private readonly TodoContext _context;
    private readonly Mock<IExternalTodoApiClient> _mockApiClient;
    private readonly Mock<IRetryPolicyService> _mockRetryPolicyService;
    private readonly Mock<ILogger<TodoSyncService>> _mockSyncLogger;
    private readonly Mock<ILogger<ConflictResolver>> _mockConflictLogger;
    private readonly ConflictResolver _conflictResolver;
    private readonly TodoSyncService _syncService;

    public ConflictResolutionIntegrationTests()
    {
        var options = new DbContextOptionsBuilder<TodoContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        _context = new TodoContext(options);
        _mockApiClient = new Mock<IExternalTodoApiClient>();
        _mockRetryPolicyService = new Mock<IRetryPolicyService>();
        _mockSyncLogger = new Mock<ILogger<TodoSyncService>>();
        _mockConflictLogger = new Mock<ILogger<ConflictResolver>>();

        _mockApiClient.Setup(x => x.SourceId).Returns("test-source");
        
        // Setup retry policy mocks to return empty pipelines for tests
        _mockRetryPolicyService.Setup(x => x.GetHttpRetryPolicy()).Returns(Polly.ResiliencePipeline.Empty);
        _mockRetryPolicyService.Setup(x => x.GetDatabaseRetryPolicy()).Returns(Polly.ResiliencePipeline.Empty);
        _mockRetryPolicyService.Setup(x => x.GetSyncRetryPolicy()).Returns(Polly.ResiliencePipeline.Empty);

        _conflictResolver = new ConflictResolver(_mockConflictLogger.Object);
        _syncService = new TodoSyncService(_context, _mockApiClient.Object, _conflictResolver, _mockRetryPolicyService.Object, _mockSyncLogger.Object);
    }

    [Fact]
    public async Task SyncTodoListsFromExternalAsync_WithConflictingChanges_ExternalWins()
    {
        // Arrange
        var baseTime = DateTime.UtcNow.AddHours(-3);
        var localModifiedTime = DateTime.UtcNow.AddHours(-1);
        var externalModifiedTime = DateTime.UtcNow.AddMinutes(-30);

        // Create local TodoList that was synced 3 hours ago, but modified 1 hour ago
        var localTodoList = TodoListBuilder.Create()
			.WithName("Local Changes")
			.WithExternalId("ext-123")
			.WithLastModified(localModifiedTime)
			.WithLastSyncedAt(baseTime)
			.Build();

        _context.TodoList.Add(localTodoList);
        await _context.SaveChangesAsync();

        // External API returns a TodoList that was also modified after the last sync
        var externalTodoList = ExternalTodoListBuilder.Create()
			.WithId("ext-123")
			.WithName("External Changes")
			.WithCreatedAt(baseTime.AddHours(-1))
			.WithUpdatedAt(externalModifiedTime)
			.Build();

        _mockApiClient.Setup(x => x.GetTodoListsAsync())
            .ReturnsAsync(new List<ExternalTodoList> { externalTodoList });

        // Act
        await _syncService.SyncTodoListsFromExternalAsync();

        // Assert
        var updatedTodoList = await _context.TodoList.FindAsync(localTodoList.Id);
        Assert.NotNull(updatedTodoList);

        // External should win - name should be updated to external value
        Assert.Equal("External Changes", updatedTodoList.Name);
        Assert.Equal(externalModifiedTime, updatedTodoList.LastModified);
        Assert.NotNull(updatedTodoList.LastSyncedAt);
        Assert.True(updatedTodoList.LastSyncedAt > DateTime.UtcNow.AddMinutes(-1));
    }

    [Fact]
    public async Task SyncTodoListsFromExternalAsync_WithConflictingTodoItems_ExternalWins()
    {
        // Arrange
        var baseTime = DateTime.UtcNow.AddHours(-3);
        var localModifiedTime = DateTime.UtcNow.AddHours(-1);
        var externalModifiedTime = DateTime.UtcNow.AddMinutes(-30);

        // Create local TodoList with a TodoItem that has been modified locally
        var localTodoList = TodoListBuilder.Create()
			.WithName("Test List")
			.WithExternalId("ext-123")
			.WithLastModified(baseTime)
			.WithLastSyncedAt(baseTime)
			.Build();

        var localTodoItem = TodoItemBuilder.Create()
			.WithDescription("Local Item Description")
			.WithIsCompleted(false)
			.WithExternalId("ext-item-456")
			.WithLastModified(localModifiedTime)
			.WithLastSyncedAt(baseTime)
			.Build();

        localTodoList.Items.Add(localTodoItem);
        _context.TodoList.Add(localTodoList);
        await _context.SaveChangesAsync();

        // External API returns the same TodoItem but with different values
        var externalTodoItem = ExternalTodoItemBuilder.Create()
			.WithId("ext-item-456")
			.WithDescription("External Item Description")
			.WithCompleted(true)
			.WithCreatedAt(baseTime.AddHours(-1))
			.WithUpdatedAt(externalModifiedTime)
			.Build();

        var externalTodoList = ExternalTodoListBuilder.Create()
			.WithId("ext-123")
			.WithName("Test List")
			.WithCreatedAt(baseTime.AddHours(-1))
			.WithUpdatedAt(baseTime)
			.WithItem(externalTodoItem)
			.Build();

        _mockApiClient.Setup(x => x.GetTodoListsAsync())
            .ReturnsAsync(new List<ExternalTodoList> { externalTodoList });

        // Act
        await _syncService.SyncTodoListsFromExternalAsync();

        // Assert
        var updatedTodoList = await _context.TodoList
            .Include(tl => tl.Items)
            .FirstAsync(tl => tl.Id == localTodoList.Id);

        var updatedTodoItem = updatedTodoList.Items.First();

        // External should win - TodoItem should be updated to external values
        Assert.Equal("External Item Description", updatedTodoItem.Description);
        Assert.True(updatedTodoItem.IsCompleted);
        Assert.Equal(externalModifiedTime, updatedTodoItem.LastModified);
        Assert.NotNull(updatedTodoItem.LastSyncedAt);
    }

    [Fact]
    public async Task SyncTodoListsFromExternalAsync_WithNoConflict_ExternalNewer_UpdatesNormally()
    {
        // Arrange
        var baseTime = DateTime.UtcNow.AddHours(-3);
        var externalModifiedTime = DateTime.UtcNow.AddHours(-1);

        // Create local TodoList that hasn't been modified since last sync
        var localTodoList = TodoListBuilder.Create()
			.WithName("Original Name")
			.WithExternalId("ext-123")
			.WithLastModified(baseTime)
			.WithLastSyncedAt(baseTime)
			.Build();

        _context.TodoList.Add(localTodoList);
        await _context.SaveChangesAsync();

        // External API returns a TodoList that has been modified since last sync
        var externalTodoList = ExternalTodoListBuilder.Create()
			.WithId("ext-123")
			.WithName("Updated Name")
			.WithCreatedAt(baseTime.AddHours(-1))
			.WithUpdatedAt(externalModifiedTime)
			.Build();

        _mockApiClient.Setup(x => x.GetTodoListsAsync())
            .ReturnsAsync(new List<ExternalTodoList> { externalTodoList });

        // Act
        await _syncService.SyncTodoListsFromExternalAsync();

        // Assert
        var updatedTodoList = await _context.TodoList.FindAsync(localTodoList.Id);
        Assert.NotNull(updatedTodoList);

        // Should update normally (no conflict)
        Assert.Equal("Updated Name", updatedTodoList.Name);
        Assert.Equal(externalModifiedTime, updatedTodoList.LastModified);
        Assert.NotNull(updatedTodoList.LastSyncedAt);
    }

    [Fact]
    public async Task SyncTodoListsFromExternalAsync_WithNoConflict_LocalNewer_OnlyUpdatesSyncTimestamp()
    {
        // Arrange
        var baseTime = DateTime.UtcNow.AddHours(-3);
        var localModifiedTime = DateTime.UtcNow.AddHours(-1);
        var externalModifiedTime = DateTime.UtcNow.AddHours(-2);

        var originalName = "Local Name";

        // Create local TodoList that has been modified after the external one
        var localTodoList = TodoListBuilder.Create()
			.WithName(originalName)
			.WithExternalId("ext-123")
			.WithLastModified(localModifiedTime)
			.WithLastSyncedAt(baseTime)
			.Build();

        _context.TodoList.Add(localTodoList);
        await _context.SaveChangesAsync();

        // External API returns a TodoList that hasn't been modified recently
        var externalTodoList = ExternalTodoListBuilder.Create()
			.WithId("ext-123")
			.WithName(originalName)
			.WithCreatedAt(baseTime.AddHours(-1))
			.WithUpdatedAt(externalModifiedTime)
			.Build();

        _mockApiClient.Setup(x => x.GetTodoListsAsync())
            .ReturnsAsync(new List<ExternalTodoList> { externalTodoList });

        // Act
        await _syncService.SyncTodoListsFromExternalAsync();

        // Assert
        var updatedTodoList = await _context.TodoList.FindAsync(localTodoList.Id);
        Assert.NotNull(updatedTodoList);

        // Local data should remain unchanged
        Assert.Equal(originalName, updatedTodoList.Name);
        Assert.Equal(localModifiedTime, updatedTodoList.LastModified);

        // But sync timestamp should be updated
        Assert.NotNull(updatedTodoList.LastSyncedAt);
        Assert.True(updatedTodoList.LastSyncedAt > DateTime.UtcNow.AddMinutes(-1));
    }

    [Fact]
    public async Task PerformFullSyncAsync_WithConflicts_ResolvesInBothDirections()
    {
        // Arrange
        var baseTime = DateTime.UtcNow.AddHours(-3);

        // Create unsynced local TodoList (will be pushed to external)
		var localTodoItem = TodoItemBuilder.Create()
			.WithDescription("Local Item")
			.WithIsCompleted(false)
			.WithLastModified(DateTime.UtcNow.AddMinutes(-10))
			.Build();

        var unsyncedLocal = TodoListBuilder.Create()
			.WithName("Unsynced Local List")
			.WithExternalId(null) // Not synced yet
			.WithLastModified(DateTime.UtcNow.AddMinutes(-10))
			.WithItem(localTodoItem)
			.Build();

        // Create already-synced local TodoList that has conflicts with external
        var conflictedLocal = TodoListBuilder.Create()
			.WithName("Local Conflicted Name")
			.WithExternalId("ext-conflict-123")
			.WithLastModified(DateTime.UtcNow.AddHours(-1)) // Modified after last sync
			.WithLastSyncedAt(baseTime)
			.Build();

        _context.TodoList.AddRange(unsyncedLocal, conflictedLocal);
        await _context.SaveChangesAsync();

		// Mock external API responses
		var unsyncedLocalItemCreatedInExternalAfterSync = ExternalTodoItemBuilder.Create()
			.WithId("ext-item-012")
			.WithDescription("Local Item")
			.WithCompleted(false)
			.WithCreatedAt(DateTime.UtcNow)
			.WithUpdatedAt(DateTime.UtcNow)
			.Build();

		var unsyncedLocalListCreatedInExternalAfterSync = ExternalTodoListBuilder.Create()
			.WithId("ext-new-012")
			.WithName("Unsynced Local List")
			.WithCreatedAt(DateTime.UtcNow)
			.WithUpdatedAt(DateTime.UtcNow)
			.WithItem(unsyncedLocalItemCreatedInExternalAfterSync)
			.Build();

		var unsyncedExternalTodoItem = ExternalTodoItemBuilder.Create()
			.WithId("ext-item-789")
			.WithDescription("Local Item")
			.WithCompleted(false)
			.WithCreatedAt(DateTime.UtcNow)
			.WithUpdatedAt(DateTime.UtcNow)
			.Build();

        var unsyncedExternalList = ExternalTodoListBuilder.Create()
			.WithId("ext-new-456")
			.WithName("Unsynced External List")
			.WithCreatedAt(DateTime.UtcNow)
			.WithUpdatedAt(DateTime.UtcNow)
			.WithItem(unsyncedExternalTodoItem)
			.Build();

        var conflictedExternalList = ExternalTodoListBuilder.Create()
			.WithId("ext-conflict-123")
			.WithName("External Conflicted Name") // Different from local
			.WithCreatedAt(baseTime.AddHours(-1))
			.WithUpdatedAt(DateTime.UtcNow.AddMinutes(-30)) // Modified after last sync
			.Build();

        // Setup mocks
        _mockApiClient.Setup(x => x.CreateTodoListAsync(It.IsAny<CreateExternalTodoList>()))
            .ReturnsAsync(unsyncedLocalListCreatedInExternalAfterSync);

        _mockApiClient.Setup(x => x.GetTodoListsAsync())
            .ReturnsAsync(new List<ExternalTodoList> { unsyncedExternalList, conflictedExternalList });

        // Act
        await _syncService.PerformFullSyncAsync();

        // Assert
        var allTodoLists = await _context.TodoList.Include(tl => tl.Items).ToListAsync();

        // Should have 3 TodoLists: 2 original + 1 created from external
        Assert.Equal(3, allTodoLists.Count);

        // Verify unsynced local list now has external ID
        var syncedLocal = allTodoLists.First(tl => tl.Name == "Unsynced Local List");
        Assert.Equal("ext-new-012", syncedLocal.ExternalId);
        Assert.NotNull(syncedLocal.LastSyncedAt);

        // Verify conflicted list was resolved with external wins
        var resolvedConflict = allTodoLists.First(tl => tl.ExternalId == "ext-conflict-123");
        Assert.Equal("External Conflicted Name", resolvedConflict.Name);
        Assert.NotNull(resolvedConflict.LastSyncedAt);

        // Verify new local list was created from external
        var newFromExternal = allTodoLists.FirstOrDefault(tl => tl.ExternalId == "ext-new-456" && tl.Id != syncedLocal.Id);
        Assert.NotNull(newFromExternal);
    }

    public async ValueTask DisposeAsync()
    {
        await _context.DisposeAsync();
    }
}
