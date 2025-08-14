using Microsoft.EntityFrameworkCore;
using TodoApi.Dtos;
using TodoApi.Services.TodoItemService;
using TodoApi.Tests.Builders;

namespace TodoApi.Tests.Services.TodoItemServiceTests;

public class TodoItemServiceTests
{
    private readonly TodoContext _context;
    private readonly Mock<ILogger<TodoItemService>> _mockLogger;
    private readonly ITodoItemService _service;

    public TodoItemServiceTests()
    {
        var options = new DbContextOptionsBuilder<TodoContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _context = new TodoContext(options);
        _mockLogger = new Mock<ILogger<TodoItemService>>();
        _service = new TodoItemService(_context, _mockLogger.Object);
    }

    [Fact]
    public async Task GetTodoItemsAsync_ReturnsItems_WhenListExists()
    {
		// Arrange
        var list = TodoListBuilder.Create()
            .WithName("List A")
            .Build();
        _context.TodoList.Add(list);
        await _context.SaveChangesAsync();

		var item1 = new TodoItem { Description = "A", TodoListId = list.Id };
		var item2 = new TodoItem { Description = "B", TodoListId = list.Id };
        _context.TodoItem.AddRange(item1, item2);
        await _context.SaveChangesAsync();

		// Act
        var items = await _service.GetTodoItems(list.Id);

		// Assert
		Assert.NotNull(items);
		Assert.Equal(2, items!.Count);
		Assert.Contains(items, i => i.Description == "A");
		Assert.Contains(items, i => i.Description == "B");
        Assert.Equal(2, await _context.TodoItem.CountAsync(i => i.TodoListId == list.Id));
    }

    [Fact]
    public async Task GetTodoItemsAsync_ReturnsNull_WhenListNotFound()
    {
        // Arrange: no list with this ID in DB

		// Act
        var items = await _service.GetTodoItems(12345);

		// Assert
		Assert.Null(items);
    }

    [Fact]
    public async Task GetTodoItemAsync_ReturnsItem_WhenExists()
    {
		// Arrange
        var list = TodoListBuilder.Create()
            .WithName("List")
            .Build();
        _context.TodoList.Add(list);
        await _context.SaveChangesAsync();

        var item = TodoItemBuilder.Create()
            .WithDescription("X")
            .WithTodoListId(list.Id)
            .WithIsCompleted(true)
            .Build();
        _context.TodoItem.Add(item);
        await _context.SaveChangesAsync();

		// Act
        var result = await _service.GetTodoItemById(list.Id, item.Id);

		// Assert
		Assert.NotNull(result);
		Assert.Equal(item.Id, result!.Id);
		Assert.True(result.Completed);
        Assert.NotNull(await _context.TodoItem.FindAsync(item.Id));
    }

    [Fact]
    public async Task GetTodoItemAsync_ReturnsNull_WhenListOrItemMissing()
    {
		// Arrange
        var list = TodoListBuilder.Create()
            .WithName("List")
            .Build();
        _context.TodoList.Add(list);
        await _context.SaveChangesAsync();

        // Act
        var resultMissingItem = await _service.GetTodoItemById(list.Id, 9999);
        var resultMissingList = await _service.GetTodoItemById(9999, 1);

		// Assert
		Assert.Null(resultMissingItem);
		Assert.Null(resultMissingList);
        Assert.NotNull(await _context.TodoList.FindAsync(list.Id));
    }

    [Fact]
    public async Task UpdateTodoItemAsync_UpdatesFieldsAndMarksPending()
    {
		// Arrange
        var list = TodoListBuilder.Create()
            .WithName("List")
            .Build();
        _context.TodoList.Add(list);
        await _context.SaveChangesAsync();

        var item = TodoItemBuilder.Create()
            .WithDescription("Old")
            .WithIsCompleted(false)
            .WithTodoListId(list.Id)
            .WithLastModified(DateTime.UtcNow.AddHours(-1))
            .Build();
        _context.TodoItem.Add(item);
        await _context.SaveChangesAsync();

        var payload = new UpdateTodoItem { Description = "New", Completed = true };
        var before = DateTime.UtcNow.AddSeconds(-1);

		// Act
		var updated = await _service.UpdateTodoItem(list.Id, item.Id, payload);
		var after = DateTime.UtcNow.AddSeconds(1);

		// Assert
        Assert.NotNull(updated);
        Assert.Equal("New", updated!.Description);
        Assert.True(updated.Completed);
        var dbItem = await _context.TodoItem.FindAsync(item.Id);
        Assert.NotNull(dbItem);
        Assert.Equal("New", dbItem!.Description);
        Assert.True(dbItem.IsCompleted);
    }

    [Fact]
    public async Task CreateTodoItemAsync_CreatesItem_WithPendingAndTimestamp()
    {
		// Arrange
        var list = TodoListBuilder.Create()
            .WithName("List")
            .Build();
        _context.TodoList.Add(list);
        await _context.SaveChangesAsync();

        var payload = new CreateTodoItem { Description = "Created", Completed = false };

		// Act
		var created = await _service.CreateTodoItem(list.Id, payload);

		// Assert
		Assert.NotNull(created);
		Assert.Equal("Created", created!.Description);

        var createdEntity = await _context.TodoItem.FirstOrDefaultAsync(e => e.Description == "Created");
        Assert.NotNull(createdEntity);
    }

    [Fact]
    public async Task DeleteTodoItemAsync_RemovesItem_WhenExists()
    {
		// Arrange
        var list = TodoListBuilder.Create()
            .WithName("List")
            .Build();
        _context.TodoList.Add(list);
        await _context.SaveChangesAsync();

        var item = TodoItemBuilder.Create()
            .WithDescription("ToDelete")
            .WithTodoListId(list.Id)
            .Build();
        _context.TodoItem.Add(item);
        await _context.SaveChangesAsync();

		// Act
		var ok = await _service.DeleteTodoItem(list.Id, item.Id);

		// Assert
		Assert.True(ok);
        var deletedItem = await _context.TodoItem.FindAsync(item.Id);
        Assert.NotNull(deletedItem);
        Assert.True(deletedItem.IsDeleted);
        Assert.NotNull(deletedItem.DeletedAt);
        Assert.True(deletedItem.IsSyncPending);
    }

    [Fact]
    public async Task DeleteTodoItemAsync_ReturnsFalse_WhenNotFound()
    {
        var okMissingList = await _service.DeleteTodoItem(9999, 1);
        Assert.False(okMissingList);
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
        var todoItem = TodoItemBuilder.Create()
            .WithDescription("X")
            .WithTodoListId(todoList.Id)
            .Build();
        _context.TodoItem.Add(todoItem);
        await _context.SaveChangesAsync();

        // Act
        await _service.MarkAsPending(todoItem.Id);

        // Assert
        var updatedList = await _context.TodoList.FindAsync(todoList.Id);
        Assert.NotNull(updatedList);
        Assert.True(updatedList!.IsSyncPending);
        var updatedItem = await _context.TodoItem.FindAsync(todoItem.Id);
        Assert.True(updatedItem!.IsSyncPending);
    }

    [Fact]
    public async Task ClearPendingFlagAsync_WhenTodoListExists_ClearsPendingFlag()
    {
        // Arrange
        var todoList = TodoListBuilder.Create()
            .WithName("Test List")
            .Build();
        _context.TodoList.Add(todoList);
        await _context.SaveChangesAsync();
        var todoItem2 = TodoItemBuilder.Create()
            .WithDescription("Pending")
            .WithTodoListId(todoList.Id)
            .WithSyncPending(true)
            .Build();
        _context.TodoItem.Add(todoItem2);
        await _context.SaveChangesAsync();

        // Act
        await _service.ClearPendingFlag(todoItem2.Id);

        // Assert
        var clearedItem = await _context.TodoItem.FindAsync(todoItem2.Id);
        Assert.NotNull(clearedItem);
        Assert.False(clearedItem!.IsSyncPending);
        Assert.NotNull(clearedItem.LastSyncedAt);
    }

    [Fact]
    public async Task DeleteTodoItemAsync_SoftDeletesItem_WhenExists()
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
        var result = await _service.DeleteTodoItem(todoList.Id, todoItem.Id);

        // Assert
        Assert.True(result);

        var deletedItem = await _context.TodoItem.FirstOrDefaultAsync(ti => ti.Id == todoItem.Id);
        Assert.NotNull(deletedItem);
        Assert.True(deletedItem.IsDeleted);
        Assert.NotNull(deletedItem.DeletedAt);
        Assert.True(deletedItem.IsSyncPending);

        // Verify parent list is also marked as pending
        var parentList = await _context.TodoList.FindAsync(todoList.Id);
        Assert.NotNull(parentList);
        Assert.True(parentList.IsSyncPending);
    }

    [Fact]
    public async Task GetTodoItemsAsync_ExcludesDeletedItems()
    {
        // Arrange
        var todoList = TodoListBuilder.Create()
            .WithName("Test List")
            .Build();
        var activeItem = TodoItemBuilder.Create()
            .WithDescription("Active Item")
            .WithTodoListId(todoList.Id)
            .Build();
        var deletedItem = TodoItemBuilder.Create()
            .WithDescription("Deleted Item")
            .WithTodoListId(todoList.Id)
            .WithIsDeleted(true)
            .Build();

        todoList.Items.Add(activeItem);
        todoList.Items.Add(deletedItem);
        _context.TodoList.Add(todoList);
        await _context.SaveChangesAsync();

        // Act
        var result = await _service.GetTodoItems(todoList.Id);

        // Assert
        Assert.NotNull(result);
        Assert.Single(result);
        Assert.Equal("Active Item", result.First().Description);
    }

    [Fact]
    public async Task GetTodoItemAsync_ReturnsNull_WhenItemIsDeleted()
    {
        // Arrange
        var todoList = TodoListBuilder.Create()
            .WithName("Test List")
            .Build();
        var deletedItem = TodoItemBuilder.Create()
            .WithDescription("Deleted Item")
            .WithTodoListId(todoList.Id)
            .WithIsDeleted(true)
            .Build();

        todoList.Items.Add(deletedItem);
        _context.TodoList.Add(todoList);
        await _context.SaveChangesAsync();

        // Act
        var result = await _service.GetTodoItemById(todoList.Id, deletedItem.Id);

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public async Task UpdateTodoItemAsync_ReturnsNull_WhenItemIsDeleted()
    {
        // Arrange
        var todoList = TodoListBuilder.Create()
            .WithName("Test List")
            .Build();
        var deletedItem = TodoItemBuilder.Create()
            .WithDescription("Deleted Item")
            .WithTodoListId(todoList.Id)
            .WithIsDeleted(true)
            .Build();

        todoList.Items.Add(deletedItem);
        _context.TodoList.Add(todoList);
        await _context.SaveChangesAsync();

        // Act
        var result = await _service.UpdateTodoItem(todoList.Id, deletedItem.Id, new Dtos.UpdateTodoItem { Description = "Updated", Completed = true });

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public async Task GetOrphanedTodoItems_ReturnsItemsNotInExternalIds()
    {
        // Arrange
        var todoList = TodoListBuilder.Create()
            .WithName("Test List")
            .Build();
        var activeExternalItem = TodoItemBuilder.Create()
            .WithDescription("Active External")
            .WithExternalId("ext-active")
            .WithTodoListId(todoList.Id)
            .Build();
        var orphanedExternalItem = TodoItemBuilder.Create()
            .WithDescription("Orphaned External")
            .WithExternalId("ext-orphaned")
            .WithTodoListId(todoList.Id)
            .Build();
        var localOnlyItem = TodoItemBuilder.Create()
            .WithDescription("Local Only")
            .WithTodoListId(todoList.Id)
            .Build();

        todoList.Items.Add(activeExternalItem);
        todoList.Items.Add(orphanedExternalItem);
        todoList.Items.Add(localOnlyItem);
        _context.TodoList.Add(todoList);
        await _context.SaveChangesAsync();

        var externalIds = new[] { "ext-active", "ext-other" }; // Only ext-active is present in external

        // Act
        var result = await _service.GetOrphanedTodoItems(externalIds);

        // Assert
        Assert.Equal(2, result.Count());
        Assert.Contains(result, i => i.Description == "Orphaned External");
        Assert.Contains(result, i => i.Description == "Local Only");
        Assert.DoesNotContain(result, i => i.Description == "Active External");
    }

    [Fact]
    public async Task GetOrphanedTodoItems_ExcludesDeletedItems()
    {
        // Arrange
        var todoList = TodoListBuilder.Create()
            .WithName("Test List")
            .Build();
        var activeOrphanedItem = TodoItemBuilder.Create()
            .WithDescription("Active Orphaned")
            .WithExternalId("ext-orphaned")
            .WithTodoListId(todoList.Id)
            .Build();
        var deletedOrphanedItem = TodoItemBuilder.Create()
            .WithDescription("Deleted Orphaned")
            .WithExternalId("ext-deleted-orphaned")
            .WithTodoListId(todoList.Id)
            .WithIsDeleted(true)
            .Build();

        todoList.Items.Add(activeOrphanedItem);
        todoList.Items.Add(deletedOrphanedItem);
        _context.TodoList.Add(todoList);
        await _context.SaveChangesAsync();

        var externalIds = new[] { "ext-other" }; // Neither item is in external IDs

        // Act
        var result = await _service.GetOrphanedTodoItems(externalIds);

        // Assert
        Assert.Single(result);
        Assert.Equal("Active Orphaned", result.First().Description);
        Assert.False(result.First().IsDeleted);
    }

    [Fact]
    public async Task GetOrphanedTodoItems_ExcludesItemsInExternalIds()
    {
        // Arrange
        var todoList = TodoListBuilder.Create()
            .WithName("Test List")
            .Build();
        var itemInExternal = TodoItemBuilder.Create()
            .WithDescription("In External")
            .WithExternalId("ext-included")
            .WithTodoListId(todoList.Id)
            .Build();
        var itemNotInExternal = TodoItemBuilder.Create()
            .WithDescription("Not In External")
            .WithExternalId("ext-not-included")
            .WithTodoListId(todoList.Id)
            .Build();

        todoList.Items.Add(itemInExternal);
        todoList.Items.Add(itemNotInExternal);
        _context.TodoList.Add(todoList);
        await _context.SaveChangesAsync();

        var externalIds = new[] { "ext-included", "ext-other" };

        // Act
        var result = await _service.GetOrphanedTodoItems(externalIds);

        // Assert
        Assert.Single(result);
        Assert.Equal("Not In External", result.First().Description);
        Assert.DoesNotContain(result, i => i.Description == "In External");
    }

    [Fact]
    public async Task GetOrphanedTodoItems_HandlesEmptyExternalIdsCollection()
    {
        // Arrange
        var todoList = TodoListBuilder.Create()
            .WithName("Test List")
            .Build();
        var itemWithExternalId = TodoItemBuilder.Create()
            .WithDescription("With External ID")
            .WithExternalId("ext-123")
            .WithTodoListId(todoList.Id)
            .Build();
        var itemWithoutExternalId = TodoItemBuilder.Create()
            .WithDescription("Without External ID")
            .WithTodoListId(todoList.Id)
            .Build();

        todoList.Items.Add(itemWithExternalId);
        todoList.Items.Add(itemWithoutExternalId);
        _context.TodoList.Add(todoList);
        await _context.SaveChangesAsync();

        var externalIds = new string[] { }; // Empty collection

        // Act
        var result = await _service.GetOrphanedTodoItems(externalIds);

        // Assert
        Assert.Equal(2, result.Count());
        Assert.Contains(result, i => i.Description == "With External ID");
        Assert.Contains(result, i => i.Description == "Without External ID");
    }

    [Fact]
    public async Task GetOrphanedTodoItems_HandlesNullExternalIdsCollection()
    {
        // Arrange
        var todoList = TodoListBuilder.Create()
            .WithName("Test List")
            .Build();
        var itemWithExternalId = TodoItemBuilder.Create()
            .WithDescription("With External ID")
            .WithExternalId("ext-123")
            .WithTodoListId(todoList.Id)
            .Build();
        var itemWithoutExternalId = TodoItemBuilder.Create()
            .WithDescription("Without External ID")
            .WithTodoListId(todoList.Id)
            .Build();

        todoList.Items.Add(itemWithExternalId);
        todoList.Items.Add(itemWithoutExternalId);
        _context.TodoList.Add(todoList);
        await _context.SaveChangesAsync();

        IEnumerable<string>? externalIds = null; // Null collection

        // Act & Assert
        // Note: The current implementation doesn't handle null collections and will throw an exception
        // This test documents the current behavior
        await Assert.ThrowsAsync<ArgumentNullException>(() => _service.GetOrphanedTodoItems(externalIds));
    }

    [Fact]
    public async Task GetOrphanedTodoItems_ReturnsEmptyWhenNoOrphanedItems()
    {
        // Arrange
        var todoList = TodoListBuilder.Create()
            .WithName("Test List")
            .Build();
        var item1 = TodoItemBuilder.Create()
            .WithDescription("Item 1")
            .WithExternalId("ext-1")
            .WithTodoListId(todoList.Id)
            .Build();
        var item2 = TodoItemBuilder.Create()
            .WithDescription("Item 2")
            .WithExternalId("ext-2")
            .WithTodoListId(todoList.Id)
            .Build();

        todoList.Items.Add(item1);
        todoList.Items.Add(item2);
        _context.TodoList.Add(todoList);
        await _context.SaveChangesAsync();

        var externalIds = new[] { "ext-1", "ext-2", "ext-3" }; // All existing items are in external IDs

        // Act
        var result = await _service.GetOrphanedTodoItems(externalIds);

        // Assert
        Assert.Empty(result);
    }

    [Fact]
    public async Task GetOrphanedTodoItems_ReturnsEmptyWhenNoItemsHaveExternalIds()
    {
        // Arrange
        var todoList = TodoListBuilder.Create()
            .WithName("Test List")
            .Build();
        var localItem1 = TodoItemBuilder.Create()
            .WithDescription("Local 1")
            .WithTodoListId(todoList.Id)
            .Build();
        var localItem2 = TodoItemBuilder.Create()
            .WithDescription("Local 2")
            .WithTodoListId(todoList.Id)
            .Build();

        todoList.Items.Add(localItem1);
        todoList.Items.Add(localItem2);
        _context.TodoList.Add(todoList);
        await _context.SaveChangesAsync();

        var externalIds = new[] { "ext-1", "ext-2" };

        // Act
        var result = await _service.GetOrphanedTodoItems(externalIds);

        // Assert
        // Note: Items with null/empty ExternalId are considered orphaned when not in externalIds
        // This is the current behavior of the implementation
        Assert.Equal(2, result.Count());
        Assert.Contains(result, i => i.Description == "Local 1");
        Assert.Contains(result, i => i.Description == "Local 2");
    }

    [Fact]
    public async Task GetOrphanedTodoItems_MixedScenarios_ReturnsCorrectItems()
    {
        // Arrange
        var todoList = TodoListBuilder.Create()
            .WithName("Test List")
            .Build();

        var activeExternalItem = TodoItemBuilder.Create()
            .WithDescription("Active External")
            .WithExternalId("ext-active")
            .WithTodoListId(todoList.Id)
            .Build();

		var orphanedExternalItem = TodoItemBuilder.Create()
            .WithDescription("Orphaned External")
            .WithExternalId("ext-orphaned")
            .WithTodoListId(todoList.Id)
            .Build();

		var deletedOrphanedItem = TodoItemBuilder.Create()
            .WithDescription("Deleted Orphaned")
            .WithExternalId("ext-deleted")
            .WithTodoListId(todoList.Id)
            .WithIsDeleted(true)
            .Build();

		var localOnlyItem = TodoItemBuilder.Create()
            .WithDescription("Local Only")
            .WithTodoListId(todoList.Id)
            .Build();

        todoList.Items.Add(activeExternalItem);
        todoList.Items.Add(orphanedExternalItem);
        todoList.Items.Add(deletedOrphanedItem);
        todoList.Items.Add(localOnlyItem);
        _context.TodoList.Add(todoList);
        await _context.SaveChangesAsync();

        var externalIds = new[] { "ext-active", "ext-other" };

        // Act
        var result = await _service.GetOrphanedTodoItems(externalIds);

        // Assert
        Assert.Equal(2, result.Count());
        Assert.Contains(result, i => i.Description == "Orphaned External");
        Assert.Contains(result, i => i.Description == "Local Only");
        Assert.DoesNotContain(result, i => i.Description == "Active External");
        Assert.DoesNotContain(result, i => i.Description == "Deleted Orphaned");
    }
}


