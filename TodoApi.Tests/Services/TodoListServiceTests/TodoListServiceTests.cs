using Microsoft.EntityFrameworkCore;
using TodoApi.Dtos;
using TodoApi.Services.TodoListService;
using TodoApi.Tests.Builders;

namespace TodoApi.Tests.Services.TodoListServiceTests;

public class TodoListServiceTests
{
    private readonly TodoContext _context;
    private readonly Mock<ILogger<TodoListService>> _mockLogger;
    private readonly ITodoListService _service;

    public TodoListServiceTests()
    {
        var options = new DbContextOptionsBuilder<TodoContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _context = new TodoContext(options);
        _mockLogger = new Mock<ILogger<TodoListService>>();
        _service = new TodoListService(_context, _mockLogger.Object);
    }

    [Fact]
    public async Task CreateTodoListAsync_Creates_WithPendingAndName()
    {
		// Arrange
        var todoList = TodoListBuilder.Create()
            .WithName("New List")
            .Build();

        _context.TodoList.Add(todoList);
        await _context.SaveChangesAsync();

		// Act
        var created = await _service.CreateTodoList(new CreateTodoList { Name = "New List" });
        Assert.NotNull(created);
        Assert.Equal("New List", created.Name);

		// Assert
        Assert.NotNull(created);
        Assert.True(created.Id > 0);
        Assert.Equal("New List", created.Name);
    }

    [Fact]
    public async Task GetTodoListsAsync_ReturnsAll()
    {
		// Arrange
        var todoList1 = TodoListBuilder.Create()
            .WithName("A")
            .Build();
        var todoList2 = TodoListBuilder.Create()
            .WithName("B")
            .Build();
        _context.TodoList.Add(todoList1);
        _context.TodoList.Add(todoList2);
        await _context.SaveChangesAsync();

		// Act
        var all = await _service.GetTodoLists();

		// Assert
		Assert.Equal(2, all.Count);
        Assert.Equal(2, await _context.TodoList.CountAsync());
    }

    [Fact]
    public async Task GetTodoListAsync_ReturnsNull_WhenMissing()
    {
		// Arrange
        // No list with this ID in DB

		// Act
        var res = await _service.GetTodoListById(999);

		// Assert
		Assert.Null(res);
    }

    [Fact]
    public async Task UpdateTodoListAsync_UpdatesNameAndPending()
    {
		// Arrange
        var list = TodoListBuilder.Create()
            .WithName("Old")
            .Build();
        _context.TodoList.Add(list);
        await _context.SaveChangesAsync();

		// Act
        var updated = await _service.UpdateTodoList(list.Id, new UpdateTodoList { Name = "New" });

		// Assert
		Assert.NotNull(updated);
		Assert.Equal("New", updated!.Name);

        var updatedDb = await _context.TodoList.FindAsync(list.Id);
        Assert.NotNull(updatedDb);
        Assert.Equal("New", updatedDb!.Name);
    }

    [Fact]
    public async Task DeleteTodoListAsync_Deletes_WhenExists()
    {
		// Arrange
        var list = TodoListBuilder.Create()
            .WithName("Del")
            .Build();
        _context.TodoList.Add(list);
        await _context.SaveChangesAsync();

		// Act
		var ok = await _service.DeleteTodoList(list.Id);

		// Assert
        Assert.True(ok);
        var deletedList = await _context.TodoList.FindAsync(list.Id);
        Assert.NotNull(deletedList);
        Assert.True(deletedList.IsDeleted);
        Assert.NotNull(deletedList.DeletedAt);
        Assert.True(deletedList.IsSyncPending);
    }

    [Fact]
    public async Task MarkAsPendingAsync_WhenExists_MarksAsPending()
    {
        // Arrange
        var todoList = TodoListBuilder.Create()
            .WithName("Test List")
            .Build();
        _context.TodoList.Add(todoList);
        await _context.SaveChangesAsync();

        // Act
        await _service.MarkAsPending(todoList.Id);

        // Assert
        var updated = await _context.TodoList.FindAsync(todoList.Id);
        Assert.NotNull(updated);
        Assert.True(updated!.IsSyncPending);
    }

    [Fact]
    public async Task ClearPendingFlagAsync_WhenTodoListExists_ClearsPendingFlag()
    {
        // Arrange
        var todoList = TodoListBuilder.Create()
            .WithName("Test List")
            .WithSyncPending(true)
            .Build();
        _context.TodoList.Add(todoList);
        await _context.SaveChangesAsync();

        // Act
        await _service.ClearPendingFlag(todoList.Id);

        // Assert
        var cleared = await _context.TodoList.FindAsync(todoList.Id);
        Assert.NotNull(cleared);
        Assert.False(cleared!.IsSyncPending);
    }

    [Fact]
    public async Task DeleteTodoListAsync_SoftDeletesList_WhenExists()
    {
        // Arrange
        var todoList = TodoListBuilder.Create()
            .WithName("Test List")
            .Build();
        var todoItem = TodoItemBuilder.Create()
            .WithDescription("Test Item")
            .WithTodoListId(todoList.Id)
            .Build();
        todoList.Items.Add(todoItem);

        _context.TodoList.Add(todoList);
        await _context.SaveChangesAsync();

        // Act
        var result = await _service.DeleteTodoList(todoList.Id);

        // Assert
        Assert.True(result);

        var deletedList = await _context.TodoList
            .Include(tl => tl.Items)
            .FirstOrDefaultAsync(tl => tl.Id == todoList.Id);

        Assert.NotNull(deletedList);
        Assert.True(deletedList.IsDeleted);
        Assert.NotNull(deletedList.DeletedAt);
        Assert.True(deletedList.IsSyncPending);

        // Verify all items are also soft deleted
        Assert.All(deletedList.Items, item =>
        {
            Assert.True(item.IsDeleted);
            Assert.NotNull(item.DeletedAt);
            Assert.True(item.IsSyncPending);
        });
    }

    [Fact]
    public async Task GetTodoListsAsync_ExcludesDeletedLists()
    {
        // Arrange
        var activeList = TodoListBuilder.Create()
            .WithName("Active List")
            .Build();
        var deletedList = TodoListBuilder.Create()
            .WithName("Deleted List")
            .WithDeleted(true)
            .Build();

        _context.TodoList.AddRange(activeList, deletedList);
        await _context.SaveChangesAsync();

        // Act
        var result = await _service.GetTodoLists();

        // Assert
        Assert.Single(result);
        Assert.Equal("Active List", result.First().Name);
    }

    [Fact]
    public async Task GetTodoListAsync_ReturnsNull_WhenListIsDeleted()
    {
        // Arrange
        var deletedList = TodoListBuilder.Create()
            .WithName("Deleted List")
            .WithDeleted(true)
            .Build();
        _context.TodoList.Add(deletedList);
        await _context.SaveChangesAsync();

        // Act
        var result = await _service.GetTodoListById(deletedList.Id);

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public async Task GetTodoListsPendingSync_ReturnsListsWithPendingSyncFlag()
    {
        // Arrange
        var pendingList = TodoListBuilder.Create()
            .WithName("Pending List")
            .WithSyncPending(true)
            .Build();
        var syncedList = TodoListBuilder.Create()
            .WithName("Synced List")
            .WithSyncPending(false)
            .Build();

        _context.TodoList.AddRange(pendingList, syncedList);
        await _context.SaveChangesAsync();

        // Act
        var result = await _service.GetTodoListsPending();

        // Assert
        Assert.Single(result);
        Assert.Equal("Pending List", result.First().Name);
        Assert.True(result.First().IsSyncPending);
    }

    [Fact]
    public async Task GetTodoListsPendingSync_ReturnsListsWithPendingItems()
    {
        // Arrange
        var listWithPendingItem = TodoListBuilder.Create()
            .WithName("List with pending item")
            .WithSyncPending(false)
            .Build();
        var pendingItem = TodoItemBuilder.Create()
            .WithDescription("Pending item")
            .WithSyncPending(true)
            .WithTodoListId(listWithPendingItem.Id)
            .Build();
        listWithPendingItem.Items.Add(pendingItem);

        var listWithoutPendingItems = new TodoList { Name = "List without pending items", IsSyncPending = false };
        var syncedItem = new TodoItem { Description = "Synced item", IsSyncPending = false, TodoListId = listWithoutPendingItems.Id };
        listWithoutPendingItems.Items.Add(syncedItem);

        _context.TodoList.AddRange(listWithPendingItem, listWithoutPendingItems);
        await _context.SaveChangesAsync();

        // Act
        var result = await _service.GetTodoListsPending();

        // Assert
        Assert.Single(result);
        Assert.Equal("List with pending item", result.First().Name);
        Assert.False(result.First().IsSyncPending); // List itself is not pending
        Assert.Contains(result.First().Items, i => i.IsSyncPending); // But has pending items
    }

    [Fact]
    public async Task GetTodoListsPendingSync_ReturnsListsWithBothListAndItemsPending()
    {
        // Arrange
        var listWithBothPending = TodoListBuilder.Create()
            .WithName("Both pending")
            .WithSyncPending(true)
            .Build();
        var pendingItem = TodoItemBuilder.Create()
            .WithDescription("Pending item")
            .WithSyncPending(true)
            .WithTodoListId(listWithBothPending.Id)
            .Build();
        listWithBothPending.Items.Add(pendingItem);

        _context.TodoList.Add(listWithBothPending);
        await _context.SaveChangesAsync();

        // Act
        var result = await _service.GetTodoListsPending();

        // Assert
        Assert.Single(result);
        var returnedList = result.First();
        Assert.Equal("Both pending", returnedList.Name);
        Assert.True(returnedList.IsSyncPending);
        Assert.Contains(returnedList.Items, i => i.IsSyncPending);
    }

    [Fact]
    public async Task GetTodoListsPendingSync_ReturnsEmptyWhenNoPendingSync()
    {
        // Arrange
        var syncedList = TodoListBuilder.Create()
            .WithName("Synced List")
            .WithSyncPending(false)
            .Build();
        var syncedItem = TodoItemBuilder.Create()
            .WithDescription("Synced item")
            .WithSyncPending(false)
            .WithTodoListId(syncedList.Id)
            .Build();
        syncedList.Items.Add(syncedItem);

        _context.TodoList.Add(syncedList);
        await _context.SaveChangesAsync();

        // Act
        var result = await _service.GetTodoListsPending();

        // Assert
        Assert.Empty(result);
    }

    [Fact]
    public async Task GetTodoListsPendingSync_IncludesItemsInResult()
    {
        // Arrange
        var pendingList = TodoListBuilder.Create()
            .WithName("Pending List")
            .WithSyncPending(true)
            .Build();
        var item1 = TodoItemBuilder.Create()
            .WithDescription("Item 1")
            .WithSyncPending(false)
            .WithTodoListId(pendingList.Id)
            .Build();
        var item2 = TodoItemBuilder.Create()
            .WithDescription("Item 2")
            .WithSyncPending(true)
            .WithTodoListId(pendingList.Id)
            .Build();
        pendingList.Items.Add(item1);
        pendingList.Items.Add(item2);

        _context.TodoList.Add(pendingList);
        await _context.SaveChangesAsync();

        // Act
        var result = await _service.GetTodoListsPending();

        // Assert
        Assert.Single(result);
        var returnedList = result.First();
        Assert.Equal(2, returnedList.Items.Count);
        Assert.Contains(returnedList.Items, i => i.Description == "Item 1");
        Assert.Contains(returnedList.Items, i => i.Description == "Item 2");
    }

    [Fact]
    public async Task GetTodoListByExternalIdAsync_ReturnsListWhenExists()
    {
        // Arrange
        var todoList = TodoListBuilder.Create()
            .WithName("External List")
            .WithExternalId("ext-123")
            .Build();
        var todoItem = TodoItemBuilder.Create()
            .WithDescription("External Item")
            .WithTodoListId(todoList.Id)
            .Build();
        todoList.Items.Add(todoItem);

        _context.TodoList.Add(todoList);
        await _context.SaveChangesAsync();

        // Act
        var result = await _service.GetTodoListByExternalId("ext-123");

        // Assert
        Assert.NotNull(result);
        Assert.Equal("External List", result.Name);
        Assert.Equal("ext-123", result.ExternalId);
        Assert.Single(result.Items);
        Assert.Equal("External Item", result.Items.First().Description);
    }

    [Fact]
    public async Task GetTodoListByExternalIdAsync_ReturnsNullWhenNotExists()
    {
        // Arrange
        var todoList = TodoListBuilder.Create()
            .WithName("Different List")
            .WithExternalId("ext-456")
            .Build();
        _context.TodoList.Add(todoList);
        await _context.SaveChangesAsync();

        // Act
        var result = await _service.GetTodoListByExternalId("ext-nonexistent");

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public async Task GetTodoListByExternalIdAsync_ReturnsDeletedListWhenExists()
    {
        // Arrange - Note: This method doesn't filter out deleted lists, unlike GetTodoListAsync
        var deletedList = TodoListBuilder.Create()
            .WithName("Deleted External List")
            .WithExternalId("ext-deleted")
            .WithDeleted(true)
            .Build();
        _context.TodoList.Add(deletedList);
        await _context.SaveChangesAsync();

        // Act
        var result = await _service.GetTodoListByExternalId("ext-deleted");

        // Assert
        Assert.NotNull(result);
        Assert.Equal("Deleted External List", result.Name);
        Assert.Equal("ext-deleted", result.ExternalId);
        Assert.True(result.IsDeleted);
    }

    [Fact]
    public async Task GetTodoListByExternalIdAsync_IncludesAllItemsInResult()
    {
        // Arrange
        var todoList = TodoListBuilder.Create()
            .WithName("List with multiple items")
            .WithExternalId("ext-multi")
            .Build();
        var item1 = TodoItemBuilder.Create()
            .WithDescription("Item 1")
            .WithTodoListId(todoList.Id)
            .Build();
        var item2 = TodoItemBuilder.Create()
            .WithDescription("Item 2")
            .WithTodoListId(todoList.Id)
            .Build();
        var item3 = TodoItemBuilder.Create()
            .WithDescription("Item 3")
            .WithTodoListId(todoList.Id)
            .Build();
        todoList.Items.Add(item1);
        todoList.Items.Add(item2);
        todoList.Items.Add(item3);

        _context.TodoList.Add(todoList);
        await _context.SaveChangesAsync();

        // Act
        var result = await _service.GetTodoListByExternalId("ext-multi");

        // Assert
        Assert.NotNull(result);
        Assert.Equal(3, result.Items.Count);
        Assert.Contains(result.Items, i => i.Description == "Item 1");
        Assert.Contains(result.Items, i => i.Description == "Item 2");
        Assert.Contains(result.Items, i => i.Description == "Item 3");
    }

    [Fact]
    public async Task GetOrphanedTodoListsAsync_ReturnsListsNotInExternalIds()
    {
        // Arrange
        var activeExternalList = TodoListBuilder.Create()
            .WithName("Active External")
            .WithExternalId("ext-active")
            .Build();
        var orphanedExternalList = TodoListBuilder.Create()
            .WithName("Orphaned External")
            .WithExternalId("ext-orphaned")
            .Build();
        var localOnlyList = TodoListBuilder.Create()
            .WithName("Local Only")
            .Build();

        _context.TodoList.AddRange(activeExternalList, orphanedExternalList, localOnlyList);
        await _context.SaveChangesAsync();

        var externalIds = new[] { "ext-active", "ext-other" }; // Only ext-active is present in external

        // Act
        var result = await _service.GetOrphanedTodoLists(externalIds);

        // Assert
        Assert.Single(result);
        Assert.Equal("Orphaned External", result.First().Name);
        Assert.Equal("ext-orphaned", result.First().ExternalId);
    }

    [Fact]
    public async Task GetOrphanedTodoListsAsync_ExcludesDeletedLists()
    {
        // Arrange
        var deletedOrphanedList = TodoListBuilder.Create()
            .WithName("Deleted Orphaned")
            .WithExternalId("ext-deleted-orphaned")
            .WithDeleted(true)
            .Build();
        var activeOrphanedList = TodoListBuilder.Create()
            .WithName("Active Orphaned")
            .WithExternalId("ext-active-orphaned")
            .Build();

        _context.TodoList.AddRange(deletedOrphanedList, activeOrphanedList);
        await _context.SaveChangesAsync();

        var externalIds = new[] { "ext-other" }; // Neither is in external IDs

        // Act
        var result = await _service.GetOrphanedTodoLists(externalIds);

        // Assert
        Assert.Single(result);
        Assert.Equal("Active Orphaned", result.First().Name);
        Assert.False(result.First().IsDeleted);
    }

    [Fact]
    public async Task GetOrphanedTodoListsAsync_ExcludesListsWithNullOrEmptyExternalId()
    {
        // Arrange
        var listWithNullExternalId = TodoListBuilder.Create()
            .WithName("Null External ID")
            .Build();
        var listWithEmptyExternalId = TodoListBuilder.Create()
            .WithName("Empty External ID")
            .Build();
        var listWithValidExternalId = TodoListBuilder.Create()
            .WithName("Valid External ID")
            .WithExternalId("ext-valid")
            .Build();

        _context.TodoList.AddRange(listWithNullExternalId, listWithEmptyExternalId, listWithValidExternalId);
        await _context.SaveChangesAsync();

        var externalIds = new[] { "ext-other" }; // None of the external IDs are in this list

        // Act
        var result = await _service.GetOrphanedTodoLists(externalIds);

        // Assert
        Assert.Single(result);
        Assert.Equal("Valid External ID", result.First().Name);
        Assert.Equal("ext-valid", result.First().ExternalId);
    }

    [Fact]
    public async Task GetOrphanedTodoListsAsync_IncludesOnlyNonDeletedItems()
    {
        // Arrange
        var orphanedList = TodoListBuilder.Create()
            .WithName("Orphaned List")
            .WithExternalId("ext-orphaned")
            .Build();
        var activeItem = TodoItemBuilder.Create()
            .WithDescription("Active Item")
            .WithTodoListId(orphanedList.Id)
            .Build();
        var deletedItem = TodoItemBuilder.Create()
            .WithDescription("Deleted Item")
            .WithIsDeleted(true)
            .WithTodoListId(orphanedList.Id)
            .Build();
        orphanedList.Items.Add(activeItem);
        orphanedList.Items.Add(deletedItem);

        _context.TodoList.Add(orphanedList);
        await _context.SaveChangesAsync();

        var externalIds = new[] { "ext-other" }; // Orphaned list is not in external IDs

        // Act
        var result = await _service.GetOrphanedTodoLists(externalIds);

        // Assert
        Assert.Single(result);
        var returnedList = result.First();
        Assert.Equal("Orphaned List", returnedList.Name);

        // Note: EF Core's filtered include should only return non-deleted items
        // If this assertion fails, it means the filtered include isn't working as expected
        var nonDeletedItems = returnedList.Items.Where(i => !i.IsDeleted).ToList();
        Assert.Single(nonDeletedItems);
        Assert.Equal("Active Item", nonDeletedItems.First().Description);
        Assert.False(nonDeletedItems.First().IsDeleted);
    }

    [Fact]
    public async Task GetOrphanedTodoListsAsync_ReturnsEmptyWhenAllListsAreInExternalIds()
    {
        // Arrange
        var list1 = TodoListBuilder.Create()
            .WithName("List 1")
            .WithExternalId("ext-1")
            .Build();
        var list2 = TodoListBuilder.Create()
            .WithName("List 2")
            .WithExternalId("ext-2")
            .Build();

        _context.TodoList.AddRange(list1, list2);
        await _context.SaveChangesAsync();

        var externalIds = new[] { "ext-1", "ext-2", "ext-3" }; // All existing lists are in external IDs

        // Act
        var result = await _service.GetOrphanedTodoLists(externalIds);

        // Assert
        Assert.Empty(result);
    }

    [Fact]
    public async Task GetOrphanedTodoListsAsync_ReturnsEmptyWhenNoListsHaveExternalIds()
    {
        // Arrange
        var localList1 = TodoListBuilder.Create()
            .WithName("Local 1")
            .Build();
        var localList2 = TodoListBuilder.Create()
            .WithName("Local 2")
            .Build();

        _context.TodoList.AddRange(localList1, localList2);
        await _context.SaveChangesAsync();

        var externalIds = new[] { "ext-1", "ext-2" };

        // Act
        var result = await _service.GetOrphanedTodoLists(externalIds);

        // Assert
        Assert.Empty(result);
    }
}


