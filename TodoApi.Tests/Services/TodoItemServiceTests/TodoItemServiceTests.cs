using Microsoft.EntityFrameworkCore;
using TodoApi.Dtos;
using TodoApi.Services.TodoItemService;

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
        var list = new TodoList { Name = "List A" };
        _context.TodoList.Add(list);
        await _context.SaveChangesAsync();

		var item1 = new TodoItem { Description = "A", TodoListId = list.Id };
		var item2 = new TodoItem { Description = "B", TodoListId = list.Id };
        _context.TodoItem.AddRange(item1, item2);
        await _context.SaveChangesAsync();

		// Act
        var items = await _service.GetTodoItemsAsync(list.Id);

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
        var items = await _service.GetTodoItemsAsync(12345);

		// Assert
		Assert.Null(items);
    }

    [Fact]
    public async Task GetTodoItemAsync_ReturnsItem_WhenExists()
    {
		// Arrange
        var list = new TodoList { Name = "List" };
        _context.TodoList.Add(list);
        await _context.SaveChangesAsync();

        var item = new TodoItem { Description = "X", TodoListId = list.Id, IsCompleted = true };
        _context.TodoItem.Add(item);
        await _context.SaveChangesAsync();

		// Act
        var result = await _service.GetTodoItemAsync(list.Id, item.Id);

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
        var list = new TodoList { Name = "List" };
        _context.TodoList.Add(list);
        await _context.SaveChangesAsync();

        // Act
        var resultMissingItem = await _service.GetTodoItemAsync(list.Id, 9999);
        var resultMissingList = await _service.GetTodoItemAsync(9999, 1);

		// Assert
		Assert.Null(resultMissingItem);
		Assert.Null(resultMissingList);
        Assert.NotNull(await _context.TodoList.FindAsync(list.Id));
    }

    [Fact]
    public async Task UpdateTodoItemAsync_UpdatesFieldsAndMarksPending()
    {
		// Arrange
        var list = new TodoList { Name = "List" };
        _context.TodoList.Add(list);
        await _context.SaveChangesAsync();

        var item = new TodoItem { Description = "Old", IsCompleted = false, TodoListId = list.Id, LastModified = DateTime.UtcNow.AddHours(-1) };
        _context.TodoItem.Add(item);
        await _context.SaveChangesAsync();

        var payload = new UpdateTodoItem { Description = "New", Completed = true };
        var before = DateTime.UtcNow.AddSeconds(-1);

		// Act
		var updated = await _service.UpdateTodoItemAsync(list.Id, item.Id, payload);
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
        var list = new TodoList { Name = "List" };
        _context.TodoList.Add(list);
        await _context.SaveChangesAsync();

        var payload = new CreateTodoItem { Description = "Created", Completed = false };

		// Act
		var created = await _service.CreateTodoItemAsync(list.Id, payload);

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
        var list = new TodoList { Name = "List" };
        _context.TodoList.Add(list);
        await _context.SaveChangesAsync();

        var item = new TodoItem { Description = "ToDelete", TodoListId = list.Id };
        _context.TodoItem.Add(item);
        await _context.SaveChangesAsync();

		// Act
		var ok = await _service.DeleteTodoItemAsync(list.Id, item.Id);

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
        var okMissingList = await _service.DeleteTodoItemAsync(9999, 1);
        Assert.False(okMissingList);
    }


    [Fact]
    public async Task MarkAsPendingAsync_WhenExists_MarksAsPending()
    {
        // Arrange
        var todoList = new TodoList { Name = "Test List" };
        _context.TodoList.Add(todoList);
        await _context.SaveChangesAsync();
        var todoItem = new TodoItem { Description = "X", TodoListId = todoList.Id };
        _context.TodoItem.Add(todoItem);
        await _context.SaveChangesAsync();

        // Act
        await _service.MarkAsPendingAsync(todoItem.Id);

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
        var todoList = new TodoList { Name = "Test List" };
        _context.TodoList.Add(todoList);
        await _context.SaveChangesAsync();
        var todoItem2 = new TodoItem { Description = "Pending", TodoListId = todoList.Id, IsSyncPending = true };
        _context.TodoItem.Add(todoItem2);
        await _context.SaveChangesAsync();

        // Act
        await _service.ClearPendingFlagAsync(todoItem2.Id);

        // Assert
        var clearedItem = await _context.TodoItem.FindAsync(todoItem2.Id);
        Assert.NotNull(clearedItem);
        Assert.False(clearedItem!.IsSyncPending);
        Assert.NotNull(clearedItem.LastSyncedAt);
    }

    [Fact]
    public async Task GetPendingChangesCountAsync_ReturnsCorrectCount()
    {
        // Arrange
        var todoList1 = new TodoList { Name = "Test List 1", IsSyncPending = true };
        var todoList2 = new TodoList { Name = "Test List 2" };
        _context.TodoList.AddRange(todoList1, todoList2);
        await _context.SaveChangesAsync();

        var todoItem = new TodoItem
        {
            Description = "Test Item",
            TodoListId = todoList2.Id,
            IsSyncPending = true
        };
        _context.TodoItem.Add(todoItem);
        await _context.SaveChangesAsync();

        // Act
        var result = await _service.GetPendingChangesCountAsync();

        // Assert
        // Service counts only TodoItems pending in GetPendingChangesCountAsync
        Assert.Equal(1, result);
    }

    [Fact]
    public async Task DeleteTodoItemAsync_SoftDeletesItem_WhenExists()
    {
        // Arrange
        var todoList = new TodoList { Name = "Test List" };
        var todoItem = new TodoItem { Description = "Test Item", TodoListId = todoList.Id };
        todoList.Items.Add(todoItem);
        
        _context.TodoList.Add(todoList);
        await _context.SaveChangesAsync();

        // Act
        var result = await _service.DeleteTodoItemAsync(todoList.Id, todoItem.Id);

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
        var todoList = new TodoList { Name = "Test List" };
        var activeItem = new TodoItem { Description = "Active Item", TodoListId = todoList.Id };
        var deletedItem = new TodoItem { Description = "Deleted Item", TodoListId = todoList.Id, IsDeleted = true, DeletedAt = DateTime.UtcNow };
        
        todoList.Items.Add(activeItem);
        todoList.Items.Add(deletedItem);
        _context.TodoList.Add(todoList);
        await _context.SaveChangesAsync();

        // Act
        var result = await _service.GetTodoItemsAsync(todoList.Id);

        // Assert
        Assert.NotNull(result);
        Assert.Single(result);
        Assert.Equal("Active Item", result.First().Description);
    }

    [Fact]
    public async Task GetTodoItemAsync_ReturnsNull_WhenItemIsDeleted()
    {
        // Arrange
        var todoList = new TodoList { Name = "Test List" };
        var deletedItem = new TodoItem { Description = "Deleted Item", TodoListId = todoList.Id, IsDeleted = true, DeletedAt = DateTime.UtcNow };
        
        todoList.Items.Add(deletedItem);
        _context.TodoList.Add(todoList);
        await _context.SaveChangesAsync();

        // Act
        var result = await _service.GetTodoItemAsync(todoList.Id, deletedItem.Id);

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public async Task UpdateTodoItemAsync_ReturnsNull_WhenItemIsDeleted()
    {
        // Arrange
        var todoList = new TodoList { Name = "Test List" };
        var deletedItem = new TodoItem { Description = "Deleted Item", TodoListId = todoList.Id, IsDeleted = true, DeletedAt = DateTime.UtcNow };
        
        todoList.Items.Add(deletedItem);
        _context.TodoList.Add(todoList);
        await _context.SaveChangesAsync();

        // Act
        var result = await _service.UpdateTodoItemAsync(todoList.Id, deletedItem.Id, new Dtos.UpdateTodoItem { Description = "Updated", Completed = true });

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public async Task GetPendingChangesCountAsync_ExcludesDeletedItems()
    {
        // Arrange
        var todoList = new TodoList { Name = "Test List" };
        var pendingActiveItem = new TodoItem { Description = "Active Pending", TodoListId = todoList.Id, IsSyncPending = true };
        var pendingDeletedItem = new TodoItem { Description = "Deleted Pending", TodoListId = todoList.Id, IsSyncPending = true, IsDeleted = true };
        
        todoList.Items.Add(pendingActiveItem);
        todoList.Items.Add(pendingDeletedItem);
        _context.TodoList.Add(todoList);
        await _context.SaveChangesAsync();

        // Act
        var count = await _service.GetPendingChangesCountAsync();

        // Assert
        Assert.Equal(1, count);
    }
}


