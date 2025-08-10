using Microsoft.EntityFrameworkCore;
using TodoApi.Common;
using TodoApi.Services.ConflictResolver;
using TodoApi.Services.ExternalTodoApiClient;
using TodoApi.Services.RetryPolicyService;
using TodoApi.Services.SyncService;
using TodoApi.Services.SyncStateService;
using TodoApi.Services.TodoItemService;
using TodoApi.Services.TodoListService;
using TodoApi.Tests.Builders;

namespace TodoApi.Tests.Services.SyncServiceTests;

/// <summary>
/// Integration tests that test the full sync flow with real database operations
/// </summary>
public class TodoListSyncServiceTests : IAsyncDisposable
{
	private readonly TodoContext _context;
	private readonly Mock<IExternalTodoApiClient> _mockExternalClient;
	private readonly Mock<IConflictResolver<TodoList, ExternalTodoList>> _mockTodoListConflictResolver;
	private readonly Mock<IConflictResolver<TodoItem, ExternalTodoItem>> _mockTodoItemConflictResolver;
	private readonly Mock<IRetryPolicyService> _mockRetryPolicyService;
	private readonly Mock<ILogger<TodoListSyncService>> _mockLogger;
	private readonly Mock<ISyncStateService> _mockSyncStateService;
	private readonly Mock<ITodoListService> _mockTodoListService;
	private readonly Mock<ITodoItemService> _mockTodoItemService;
	private readonly TodoListSyncService _syncService;

	public TodoListSyncServiceTests()
	{
		var options = new DbContextOptionsBuilder<TodoContext>()
			.UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
			.Options;
		_context = new TodoContext(options);

		_mockExternalClient = new Mock<IExternalTodoApiClient>();
		_mockTodoListConflictResolver = new Mock<IConflictResolver<TodoList, ExternalTodoList>>();
		_mockTodoItemConflictResolver = new Mock<IConflictResolver<TodoItem, ExternalTodoItem>>();
		_mockRetryPolicyService = new Mock<IRetryPolicyService>();
		_mockLogger = new Mock<ILogger<TodoListSyncService>>();
		_mockTodoListService = new Mock<ITodoListService>();
		_mockTodoItemService = new Mock<ITodoItemService>();
		_mockSyncStateService = new Mock<ISyncStateService>();
		_mockExternalClient.Setup(x => x.SourceId).Returns("integration-test-source");

		// Setup retry policy mocks to return empty pipelines for tests
		_mockRetryPolicyService.Setup(x => x.GetHttpRetryPolicy()).Returns(Polly.ResiliencePipeline.Empty);
		_mockRetryPolicyService.Setup(x => x.GetDatabaseRetryPolicy()).Returns(Polly.ResiliencePipeline.Empty);
		_mockRetryPolicyService.Setup(x => x.GetSyncRetryPolicy()).Returns(Polly.ResiliencePipeline.Empty);

		// Setup conflict resolver mocks to simulate proper conflict resolution behavior
		SetupConflictResolverMocks();

		_syncService = new TodoListSyncService(
			_context,
			_mockExternalClient.Object,
			_mockTodoListConflictResolver.Object,
			_mockTodoItemConflictResolver.Object,
			_mockRetryPolicyService.Object,
			_mockTodoListService.Object,
			_mockTodoItemService.Object,
			_mockSyncStateService.Object, _mockLogger.Object);
	}

	private void SetupConflictResolverMocks()
	{
		// Setup TodoList conflict resolver
		_mockTodoListConflictResolver
			.Setup(x => x.ResolveConflict(It.IsAny<TodoList>(), It.IsAny<ExternalTodoList>(), It.IsAny<ConflictResolutionStrategy>()))
			.Returns<TodoList, ExternalTodoList, ConflictResolutionStrategy>((local, external, strategy) =>
			{
				var conflictInfo = ConflictInfoBuilder.Create()
					.WithEntityType("TodoList")
					.WithEntityId(local.Id.ToString())
					.WithExternalEntityId(external.Id)
					.WithLocalLastModified(local.LastModified)
					.WithExternalLastModified(external.UpdatedAt)
					.WithLastSyncedAt(local.LastSyncedAt)
					.WithResolution(strategy)
					.WithResolutionReason(strategy == ConflictResolutionStrategy.ExternalWins ? "External wins conflict resolution" : "Local wins conflict resolution")
					.Build();

				// Detect conflicts based on field differences and timestamps
				// First check if there are actual field differences
				if (local.Name != external.Name)
				{
					conflictInfo.ModifiedFields.Add("Name");
				}

				// Only consider it a conflict if both local and external have been modified since last sync
				// AND there are actual field differences
				if (conflictInfo.ModifiedFields.Count > 0 &&
					local.LastSyncedAt.HasValue &&
					local.LastModified > local.LastSyncedAt.Value &&
					external.UpdatedAt > local.LastSyncedAt.Value)
				{
					// This is a true conflict - both sides modified since last sync with different values
					conflictInfo.ModifiedFields.Add("LastModified");
				}

				return conflictInfo;
			});

		_mockTodoListConflictResolver
			.Setup(x => x.ApplyResolution(It.IsAny<TodoList>(), It.IsAny<ExternalTodoList>(), It.IsAny<ConflictInfo>()))
			.Callback<TodoList, ExternalTodoList, ConflictInfo>((local, external, conflictInfo) =>
			{
				// Check if there's a conflict
				if (conflictInfo.HasConflict)
				{
					// Apply external changes if external wins
					if (conflictInfo.Resolution == ConflictResolutionStrategy.ExternalWins)
					{
						local.Name = external.Name;
						local.LastModified = external.UpdatedAt;
					}
					// Always update sync timestamp for conflicts
					local.LastSyncedAt = DateTime.UtcNow;
				}
				else
				{
					// No conflict - check which is newer
					if (external.UpdatedAt > local.LastModified)
					{
						// External is newer, apply changes
						local.Name = external.Name;
						local.LastModified = external.UpdatedAt;
					}
					// If local is newer or same, don't change LastModified, just update sync timestamp
					local.LastSyncedAt = DateTime.UtcNow;
				}
			});

		// Setup TodoItem conflict resolver
		_mockTodoItemConflictResolver
			.Setup(x => x.ResolveConflict(It.IsAny<TodoItem>(), It.IsAny<ExternalTodoItem>(), It.IsAny<ConflictResolutionStrategy>()))
			.Returns<TodoItem, ExternalTodoItem, ConflictResolutionStrategy>((local, external, strategy) =>
			{
				var conflictInfo = new ConflictInfo
				{
					EntityType = "TodoItem",
					EntityId = local.Id.ToString(),
					ExternalEntityId = external.Id,
					LocalLastModified = local.LastModified,
					ExternalLastModified = external.UpdatedAt,
					LastSyncedAt = local.LastSyncedAt,
					Resolution = strategy,
					ResolutionReason = strategy == ConflictResolutionStrategy.ExternalWins ? "External wins conflict resolution" : "Local wins conflict resolution"
				};

				// Detect conflicts based on field differences and timestamps
				// First check if there are actual field differences
				if (local.Description != external.Description)
				{
					conflictInfo.ModifiedFields.Add("Description");
				}
				if (local.IsCompleted != external.Completed)
				{
					conflictInfo.ModifiedFields.Add("IsCompleted");
				}

				// Only consider it a conflict if both local and external have been modified since last sync
				// AND there are actual field differences
				if (conflictInfo.ModifiedFields.Count > 0 &&
					local.LastSyncedAt.HasValue &&
					local.LastModified > local.LastSyncedAt.Value &&
					external.UpdatedAt > local.LastSyncedAt.Value)
				{
					// This is a true conflict - both sides modified since last sync with different values
					conflictInfo.ModifiedFields.Add("LastModified");
				}

				return conflictInfo;
			});



		_mockTodoItemConflictResolver
			.Setup(x => x.ApplyResolution(It.IsAny<TodoItem>(), It.IsAny<ExternalTodoItem>(), It.IsAny<ConflictInfo>()))
			.Callback<TodoItem, ExternalTodoItem, ConflictInfo>((local, external, conflictInfo) =>
			{
				// Check if there's a conflict
				if (conflictInfo.HasConflict)
				{
					// Apply external changes if external wins
					if (conflictInfo.Resolution == ConflictResolutionStrategy.ExternalWins)
					{
						local.Description = external.Description;
						local.IsCompleted = external.Completed;
						local.LastModified = external.UpdatedAt;
					}
					// Always update sync timestamp for conflicts
					local.LastSyncedAt = DateTime.UtcNow;
				}
				else
				{
					// No conflict - check which is newer
					if (external.UpdatedAt > local.LastModified)
					{
						// External is newer, apply changes
						local.Description = external.Description;
						local.IsCompleted = external.Completed;
						local.LastModified = external.UpdatedAt;
					}
					// If local is newer or same, don't change LastModified, just update sync timestamp
					local.LastSyncedAt = DateTime.UtcNow;
				}
			});
	}

	[Fact]
	public async Task SyncTodoListsToExternalAsync_WithComplexScenario_ShouldHandleAllCases()
	{
		// Arrange - Create a complex scenario with multiple lists and items
		var list1 = TodoListBuilder.Create()
			.WithName("Shopping List")
			.WithItem(TodoItemBuilder.Create().WithDescription("Buy milk").WithIsCompleted(false).Build())
			.WithItem(TodoItemBuilder.Create().WithDescription("Buy bread").WithIsCompleted(true).Build())
			.Build();

		var list2 = TodoListBuilder.Create()
			.WithName("Work Tasks")
			.WithItem(TodoItemBuilder.Create().WithDescription("Review PR").WithIsCompleted(false).WithTodoListId(2).Build())
			.WithItem(TodoItemBuilder.Create().WithDescription("Deploy to prod").WithIsCompleted(false).WithTodoListId(2).Build())
			.WithItem(TodoItemBuilder.Create().WithDescription("Update docs").WithIsCompleted(true).WithTodoListId(2).Build())
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
	public async Task SyncTodoListsToExternalAsync_AfterLocalChanges_ShouldRespectLastModifiedTimestamps()
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
	public async Task SyncTodoListsToExternalAsync_WithPartialFailures_ShouldPersistSuccessfulSyncs()
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

	[Fact]
	public async Task SyncTodoListsToExternalAsync_WithNewExternalData_CreatesLocalTodoList()
	{
		// Arrange
		var item = ExternalTodoItemBuilder.Create()
			.WithId("external-item-456")
			.WithDescription("External Task")
			.WithCompleted(false)
			.WithCreatedAt(DateTime.UtcNow)
			.WithUpdatedAt(DateTime.UtcNow)
			.Build();

		var externalTodoList = ExternalTodoListBuilder.Create()
			.WithId("external-123")
			.WithName("External List")
			.WithCreatedAt(DateTime.UtcNow)
			.WithUpdatedAt(DateTime.UtcNow)
			.WithItem(item)
			.Build();

		_mockExternalClient.Setup(x => x.GetTodoListsAsync())
			.ReturnsAsync(new List<ExternalTodoList> { externalTodoList });

		// Act
		await _syncService.SyncTodoListsFromExternalAsync();

		// Assert
		var localTodoList = await _context.TodoList
			.Include(tl => tl.Items)
			.FirstOrDefaultAsync(tl => tl.ExternalId == "external-123");

		Assert.NotNull(localTodoList);
		Assert.Equal("External List", localTodoList.Name);
		Assert.Equal("external-123", localTodoList.ExternalId);
		Assert.Single(localTodoList.Items);

		var localItem = localTodoList.Items.First();
		Assert.Equal("External Task", localItem.Description);
		Assert.False(localItem.IsCompleted);
		Assert.Equal("external-item-456", localItem.ExternalId);
	}

	[Fact]
	public async Task SyncTodoListsFromExternalAsync_WithExistingTodoList_UpdatesWhenExternalIsNewer()
	{
		// Arrange - Create existing local TodoList
		var existingTodoList = TodoListBuilder.Create()
			.WithName("Old Name")
			.WithExternalId("external-123")
			.WithLastModified(DateTime.UtcNow.AddHours(-1)) // Older than external
			.Build();

		_context.TodoList.Add(existingTodoList);
		await _context.SaveChangesAsync();

		var externalTodoList = ExternalTodoListBuilder.Create()
			.WithId("external-123")
			.WithName("Updated Name")
			.WithCreatedAt(DateTime.UtcNow.AddHours(-2))
			.WithUpdatedAt(DateTime.UtcNow) // Newer than local
			.Build();

		_mockExternalClient.Setup(x => x.GetTodoListsAsync())
			.ReturnsAsync(new List<ExternalTodoList> { externalTodoList });

		_mockTodoListConflictResolver.Setup(x => x.ResolveConflict(
			It.IsAny<TodoList>(),
			It.IsAny<ExternalTodoList>(),
			It.IsAny<ConflictResolutionStrategy>()))
			.Returns(ConflictInfoBuilder.Create()
		.WithResolution(ConflictResolutionStrategy.ExternalWins)
		.Build());

		_mockTodoListConflictResolver.Setup(x => x.ApplyResolution(
			It.IsAny<TodoList>(),
			It.IsAny<ExternalTodoList>(),
			It.IsAny<ConflictInfo>()))
			.Callback<TodoList, ExternalTodoList, ConflictInfo>((todoList, externalTodoList, conflictInfo) =>
			{
				todoList.Name = externalTodoList.Name;
				todoList.LastModified = externalTodoList.UpdatedAt;
			});

		// Act
		await _syncService.SyncTodoListsFromExternalAsync();

		// Assert
		var updatedTodoList = await _context.TodoList.FindAsync(existingTodoList.Id);
		Assert.NotNull(updatedTodoList);
		Assert.Equal("Updated Name", updatedTodoList.Name);
		Assert.Equal(externalTodoList.UpdatedAt, updatedTodoList.LastModified);
	}

	[Fact]
	public async Task SyncTodoListsFromExternalAsync_WithExistingTodoList_DoesNotUpdateWhenLocalIsNewer()
	{
		// Arrange - Create existing local TodoList
		var existingTodoList = TodoListBuilder.Create()
			.WithName("Current Name")
			.WithExternalId("external-123")
			.WithLastModified(DateTime.UtcNow) // Newer than external
			.Build();

		_context.TodoList.Add(existingTodoList);
		await _context.SaveChangesAsync();

		var externalTodoList = ExternalTodoListBuilder.Create()
			.WithId("external-123")
			.WithName("Older Name")
			.WithCreatedAt(DateTime.UtcNow.AddHours(-2))
			.WithUpdatedAt(DateTime.UtcNow.AddHours(-1)) // Older than local
			.Build();

		_mockExternalClient.Setup(x => x.GetTodoListsAsync())
			.ReturnsAsync(new List<ExternalTodoList> { externalTodoList });

		// Act
		await _syncService.SyncTodoListsFromExternalAsync();

		// Assert
		var unchangedTodoList = await _context.TodoList.FindAsync(existingTodoList.Id);
		Assert.NotNull(unchangedTodoList);
		Assert.Equal("Current Name", unchangedTodoList.Name); // Should remain unchanged
	}

	[Fact]
	public async Task SyncTodoListsFromExternalAsync_WithNoExternalData_LogsAndReturns()
	{
		// Arrange
		_mockExternalClient.Setup(x => x.GetTodoListsAsync())
			.ReturnsAsync(new List<ExternalTodoList>());

		// Act
		await _syncService.SyncTodoListsFromExternalAsync();

		// Assert
		var todoLists = await _context.TodoList.ToListAsync();
		Assert.Empty(todoLists);

		// Verify appropriate logging occurred (checking the logger was called)
		_mockLogger.Verify(
			x => x.Log(
				LogLevel.Information,
				It.IsAny<EventId>(),
				It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("No TodoLists found in external API")),
				It.IsAny<Exception>(),
				It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
			Times.Once);
	}

	[Fact]
	public async Task SyncTodoListsFromExternalAsync_WithNewTodoItems_CreatesLocalTodoItems()
	{
		// Arrange - Create existing local TodoList with one item
		var item = TodoItemBuilder.Create()
			.WithDescription("Existing Item")
			.WithIsCompleted(false)
			.WithExternalId("external-item-1")
			.WithLastModified(DateTime.UtcNow.AddHours(-1))
			.Build();

		var existingTodoList = TodoListBuilder.Create()
			.WithName("Existing List")
			.WithExternalId("external-123")
			.WithLastModified(DateTime.UtcNow.AddHours(-1))
			.WithItem(item)
			.Build();

		_context.TodoList.Add(existingTodoList);

		await _context.SaveChangesAsync();

		var externalItem1 = ExternalTodoItemBuilder.Create()
			.WithId("external-item-1")
			.WithDescription("Existing Item")
			.WithCompleted(false)
			.WithCreatedAt(DateTime.UtcNow.AddHours(-1))
			.WithUpdatedAt(DateTime.UtcNow.AddHours(-1))
			.Build();

		var externalItem2 = ExternalTodoItemBuilder.Create()
			.WithId("external-item-2")
			.WithDescription("New External Item")
			.WithCompleted(true)
			.WithCreatedAt(DateTime.UtcNow)
			.WithUpdatedAt(DateTime.UtcNow)
			.Build();

		var externalTodoList = ExternalTodoListBuilder.Create()
			.WithId("external-123")
			.WithName("Existing List")
			.WithCreatedAt(DateTime.UtcNow.AddHours(-2))
			.WithUpdatedAt(DateTime.UtcNow.AddHours(-1))
			.WithItem(externalItem1)
			.WithItem(externalItem2)
			.Build();

		_mockExternalClient.Setup(x => x.GetTodoListsAsync())
			.ReturnsAsync(new List<ExternalTodoList> { externalTodoList });

		// Setup conflict resolver mocks to allow proper syncing
		_mockTodoListConflictResolver.Setup(x => x.ResolveConflict(
				It.IsAny<TodoList>(),
				It.IsAny<ExternalTodoList>(),
				It.IsAny<ConflictResolutionStrategy>()))
			.Returns(new ConflictInfo
			{
				EntityType = "TodoList",
				Resolution = ConflictResolutionStrategy.ExternalWins,
				ResolutionReason = string.Empty
			});

		_mockTodoItemConflictResolver.Setup(x => x.ResolveConflict(
				It.IsAny<TodoItem>(),
				It.IsAny<ExternalTodoItem>(),
				It.IsAny<ConflictResolutionStrategy>()))
			.Returns(new ConflictInfo
			{
				EntityType = "TodoItem",
				Resolution = ConflictResolutionStrategy.ExternalWins,
				ResolutionReason = string.Empty
			});

		_mockTodoListConflictResolver.Setup(x => x.ApplyResolution(
				It.IsAny<TodoList>(),
				It.IsAny<ExternalTodoList>(),
				It.IsAny<ConflictInfo>()));

		_mockTodoItemConflictResolver.Setup(x => x.ApplyResolution(
				It.IsAny<TodoItem>(),
				It.IsAny<ExternalTodoItem>(),
				It.IsAny<ConflictInfo>()));

		// Act
		await _syncService.SyncTodoListsFromExternalAsync();

		// Assert
		var updatedTodoList = await _context.TodoList
			.Include(tl => tl.Items)
			.FirstAsync(tl => tl.ExternalId == "external-123");

		Assert.Equal(2, updatedTodoList.Items.Count);

		var newItem = updatedTodoList.Items.FirstOrDefault(i => i.ExternalId == "external-item-2");
		Assert.NotNull(newItem);
		Assert.Equal("New External Item", newItem.Description);
		Assert.True(newItem.IsCompleted);
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

		_mockExternalClient.Setup(x => x.GetTodoListsAsync())
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
		Assert.True(updatedTodoList.LastSyncedAt > baseTime);
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

		_mockExternalClient.Setup(x => x.GetTodoListsAsync())
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

		_mockExternalClient.Setup(x => x.GetTodoListsAsync())
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
		// Arrange - Use fixed times to avoid timezone issues
		var fixedTime = new DateTime(2025, 1, 1, 12, 0, 0, DateTimeKind.Utc);
		var baseTime = fixedTime.AddHours(-3);
		var localModifiedTime = fixedTime.AddHours(-1);
		var externalModifiedTime = fixedTime.AddHours(-2);

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

		_mockExternalClient.Setup(x => x.GetTodoListsAsync())
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
		Assert.True(updatedTodoList.LastSyncedAt > baseTime);
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
		_mockExternalClient.Setup(x => x.CreateTodoListAsync(It.IsAny<CreateExternalTodoList>()))
			.ReturnsAsync(unsyncedLocalListCreatedInExternalAfterSync);

		_mockExternalClient.Setup(x => x.GetTodoListsAsync())
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
		await _syncService.PerformFullSyncAsync();

		// Assert
		_mockExternalClient.Verify(x => x.CreateTodoListAsync(It.IsAny<CreateExternalTodoList>()), Times.Never);
		_mockExternalClient.Verify(x => x.GetTodoListsAsync(), Times.Once); // Still checks for external changes
	}

	[Fact]
	public async Task PerformFullSyncAsync_WhenPendingChanges_PerformsLocalSync()
	{
		// Arrange
		var todoList = TodoListBuilder.Create()
			.WithName("Test List")
			.WithIsSyncPending(true)
			.WithExternalId(null)
			.Build();

		_context.TodoList.Add(todoList);
		await _context.SaveChangesAsync();

		var externalResponse = ExternalTodoListBuilder.Create()
			.WithId("ext-123")
			.WithName("Test List")
			.Build();

		_mockExternalClient.Setup(x => x.CreateTodoListAsync(It.IsAny<CreateExternalTodoList>())).ReturnsAsync(externalResponse);
		_mockExternalClient.Setup(x => x.GetTodoListsAsync()).ReturnsAsync(new List<ExternalTodoList>());

		// Setup retry policy mocks
		_mockRetryPolicyService.Setup(x => x.GetSyncRetryPolicy()).Returns(Polly.ResiliencePipeline.Empty);
		_mockRetryPolicyService.Setup(x => x.GetDatabaseRetryPolicy()).Returns(Polly.ResiliencePipeline.Empty);
		_mockTodoListService.Setup(x => x.GetPendingChangesCountAsync()).ReturnsAsync(0);
		_mockTodoItemService.Setup(x => x.GetPendingChangesCountAsync()).ReturnsAsync(0);
		_mockSyncStateService.Setup(x => x.IsDeltaSyncAvailableAsync()).ReturnsAsync(false);

		// Act
		await _syncService.PerformFullSyncAsync();

		// Assert
		_mockExternalClient.Verify(x => x.CreateTodoListAsync(It.IsAny<CreateExternalTodoList>()), Times.Once);
		_mockExternalClient.Verify(x => x.GetTodoListsAsync(), Times.Once);
	}

	[Fact]
	public async Task SyncTodoListsToExternalAsync_WithUnsyncedLists_ShouldSyncSuccessfully()
	{
		// Arrange
		var todoList = TodoListBuilder.Create()
			.WithName("Test List")
			.WithExternalId(null) // Unsynced
			.WithItem(TodoItemBuilder.Create()
				.WithDescription("Task 1")
				.WithIsCompleted(false)
				.Build())
			.WithItem(TodoItemBuilder.Create()
				.WithDescription("Task 2")
				.WithIsCompleted(true)
				.Build())
			.Build();

		_context.TodoList.Add(todoList);
		await _context.SaveChangesAsync();

		var externalResponse = ExternalTodoListBuilder.Create()
			.WithId("ext-123")
			.WithName("Test List")
			.WithSourceId("test-source-id")
			.WithItem(ExternalTodoItemBuilder.Create()
				.WithId("item-1")
				.WithDescription("Task 1")
				.WithCompleted(false)
				.Build())
			.WithItem(ExternalTodoItemBuilder.Create()
				.WithId("item-2")
				.WithDescription("Task 2")
				.WithCompleted(true)
				.Build())
			.Build();

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
			dto.SourceId == "integration-test-source" &&
			dto.Items.Count == 2
		)), Times.Once);
	}

	[Fact]
	public async Task SyncTodoListsToExternalAsync_WithNoUnsyncedLists_ShouldReturnEarly()
	{
		// Arrange
		var syncedTodoList = TodoListBuilder.Create()
			.WithName("Already Synced")
			.WithExternalId("already-synced-123") // Already has external ID
			.Build();

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
		var todoList1 = TodoListBuilder.Create()
			.WithName("List 1")
			.WithExternalId(null)
			.Build();

		var todoList2 = TodoListBuilder.Create()
			.WithName("List 2")
			.WithExternalId(null)
			.Build();

		_context.TodoList.AddRange(todoList1, todoList2);
		await _context.SaveChangesAsync();

		_mockExternalClient
			.SetupSequence(x => x.CreateTodoListAsync(It.IsAny<CreateExternalTodoList>()))
			.ThrowsAsync(new HttpRequestException("External API error"))
			.ReturnsAsync(ExternalTodoListBuilder.Create()
				.WithId("ext-456")
				.WithName("List 2")
				.Build());

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
		var todoList = TodoListBuilder.Create()
			.WithName("Test List")
			.WithExternalId(null)
			.WithItem(TodoItemBuilder.Create()
				.WithDescription("Local Task")
				.WithIsCompleted(false)
				.Build())
			.Build();

		_context.TodoList.Add(todoList);
		await _context.SaveChangesAsync();

		var externalResponse = ExternalTodoListBuilder.Create()
			.WithId("ext-123")
			.WithName("Test List")
			.WithItem(ExternalTodoItemBuilder.Create()
				.WithId("item-1")
				.WithDescription("Different Task")
				.WithCompleted(false)
				.Build())
			.Build();

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
		var todoList = TodoListBuilder.Create()
			.WithName("Empty List")
			.WithExternalId(null)
			.Build();

		_context.TodoList.Add(todoList);
		await _context.SaveChangesAsync();

		var externalResponse = ExternalTodoListBuilder.Create()
			.WithId("ext-empty")
			.WithName("Empty List")
			.Build();

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
		var todoList = TodoListBuilder.Create()
			.WithName("Test")
			.WithExternalId(null)
			.Build();
		_context.TodoList.Add(todoList);
		await _context.SaveChangesAsync();

		_mockExternalClient
			.Setup(x => x.CreateTodoListAsync(It.IsAny<CreateExternalTodoList>()))
			.ReturnsAsync(ExternalTodoListBuilder.Create()
				.WithId("ext-123")
				.WithName("Test")
				.Build());

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
			TodoListBuilder.Create()
				.WithName("List 1")
				.WithExternalId(null)
				.Build(),
			TodoListBuilder.Create()
				.WithName("List 2")
				.WithExternalId(null)
				.Build(),
			TodoListBuilder.Create()
				.WithName("List 3")
				.WithExternalId(null)
				.Build()
		};
		_context.TodoList.AddRange(lists);
		await _context.SaveChangesAsync();

		_mockExternalClient
			.Setup(x => x.CreateTodoListAsync(It.IsAny<CreateExternalTodoList>()))
			.ReturnsAsync((CreateExternalTodoList dto) => ExternalTodoListBuilder.Create()
				.WithId($"ext-{dto.Name.Replace(' ', '-').ToLower()}")
				.WithName(dto.Name)
				.Build());

		// Act
		await _syncService.SyncTodoListsToExternalAsync();

		// Assert
		var updatedLists = await _context.TodoList.ToListAsync();
		Assert.All(updatedLists, list => Assert.NotNull(list.ExternalId));
		Assert.Equal(3, updatedLists.Count(l => l.ExternalId != null));

		// Verify completion log
		VerifyLogCalled(LogLevel.Information, "Sync completed. Success: 3, Failed: 0");
	}


	[Fact]
	public async Task SyncTodoListsFromExternalAsync_WhenDeltaSyncAvailable_UsesDeltaSync()
	{
		// Arrange
		var lastSyncTime = DateTime.UtcNow.AddHours(-1);
		var externalTodoList = ExternalTodoListBuilder.Create()
			.WithId("ext-123")
			.WithSourceId("test-source")
			.WithName("Test List")
			.WithUpdatedAt(DateTime.UtcNow)
			.WithItem(ExternalTodoItemBuilder.Create()
				.WithId("item-1")
				.WithDescription("Task 1")
				.WithCompleted(false)
				.Build())
			.Build();

		_mockSyncStateService.Setup(x => x.IsDeltaSyncAvailableAsync()).ReturnsAsync(true);
		_mockSyncStateService.Setup(x => x.GetLastSyncTimestampAsync()).ReturnsAsync(lastSyncTime);
		_mockExternalClient.Setup(x => x.GetTodoListsAsync())
			.ReturnsAsync(new List<ExternalTodoList> { externalTodoList });

		// Act
		await _syncService.SyncTodoListsFromExternalAsync();

		// Assert
		_mockExternalClient.Verify(x => x.GetTodoListsAsync(), Times.Once);
		_mockSyncStateService.Verify(x => x.UpdateLastSyncTimestampAsync(It.IsAny<DateTime>()), Times.Once);
	}

	[Fact]
	public async Task SyncTodoListsFromExternalAsync_WhenDeltaSyncNotAvailable_UsesFullSync()
	{
		// Arrange
		var externalTodoList = ExternalTodoListBuilder.Create()
			.WithId("ext-123")
			.WithSourceId("test-source")
			.WithName("Test List")
			.WithUpdatedAt(DateTime.UtcNow)
			.Build();

		_mockSyncStateService.Setup(x => x.IsDeltaSyncAvailableAsync()).ReturnsAsync(false);
		_mockExternalClient.Setup(x => x.GetTodoListsAsync())
			.ReturnsAsync(new List<ExternalTodoList> { externalTodoList });

		// Act
		await _syncService.SyncTodoListsFromExternalAsync();

		// Assert
		_mockExternalClient.Verify(x => x.GetTodoListsAsync(), Times.Once);
		_mockSyncStateService.Verify(x => x.UpdateLastSyncTimestampAsync(It.IsAny<DateTime>()), Times.Once);
	}

	[Fact]
	public async Task SyncTodoListsFromExternalAsync_WhenNoChanges_DoesNotUpdateSyncTimestamp()
	{
		// Arrange
		_mockSyncStateService.Setup(x => x.IsDeltaSyncAvailableAsync()).ReturnsAsync(true);
		_mockSyncStateService.Setup(x => x.GetLastSyncTimestampAsync()).ReturnsAsync(DateTime.UtcNow.AddHours(-1));
		_mockExternalClient.Setup(x => x.GetTodoListsAsync())
			.ReturnsAsync(new List<ExternalTodoList>());

		// Act
		await _syncService.SyncTodoListsFromExternalAsync();

		// Assert
		_mockSyncStateService.Verify(x => x.UpdateLastSyncTimestampAsync(It.IsAny<DateTime>()), Times.Never);
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

	public async ValueTask DisposeAsync()
	{
		await _context.DisposeAsync();
	}
}
