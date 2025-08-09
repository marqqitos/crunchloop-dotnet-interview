using Microsoft.EntityFrameworkCore;
using TodoApi.Dtos;

namespace TodoApi.Tests.Services;

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
        var todoList = new TodoList { Name = "New List" };
        _context.TodoList.Add(todoList);
        await _context.SaveChangesAsync();

		// Act
        var created = await _service.CreateTodoListAsync(new CreateTodoList { Name = "New List" });
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
        var todoList1 = new TodoList { Name = "A" };
        var todoList2 = new TodoList { Name = "B" };
        _context.TodoList.Add(todoList1);
        _context.TodoList.Add(todoList2);
        await _context.SaveChangesAsync();

		// Act
        var all = await _service.GetTodoListsAsync();

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
        var res = await _service.GetTodoListAsync(999);

		// Assert
		Assert.Null(res);
    }

    [Fact]
    public async Task UpdateTodoListAsync_UpdatesNameAndPending()
    {
		// Arrange
        var list = new TodoList { Name = "Old" };
        _context.TodoList.Add(list);
        await _context.SaveChangesAsync();

		// Act
        var updated = await _service.UpdateTodoListAsync(list.Id, new UpdateTodoList { Name = "New" });

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
        var list = new TodoList { Name = "Del" };
        _context.TodoList.Add(list);
        await _context.SaveChangesAsync();

		// Act
		var ok = await _service.DeleteTodoListAsync(list.Id);

		// Assert
        Assert.True(ok);
        Assert.Null(await _context.TodoList.FindAsync(list.Id));
    }

    [Fact]
    public async Task MarkAsPendingAsync_WhenExists_MarksAsPending()
    {
        // Arrange
        var todoList = new TodoList { Name = "Test List" };
        _context.TodoList.Add(todoList);
        await _context.SaveChangesAsync();

        // Act
        await _service.MarkAsPendingAsync(todoList.Id);

        // Assert
        var updated = await _context.TodoList.FindAsync(todoList.Id);
        Assert.NotNull(updated);
        Assert.True(updated!.IsSyncPending);
    }

    [Fact]
    public async Task ClearPendingFlagAsync_WhenTodoListExists_ClearsPendingFlag()
    {
        // Arrange
        var todoList = new TodoList { Name = "Test List", IsSyncPending = true };
        _context.TodoList.Add(todoList);
        await _context.SaveChangesAsync();

        // Act
        await _service.ClearPendingFlagAsync(todoList.Id);

        // Assert
        var cleared = await _context.TodoList.FindAsync(todoList.Id);
        Assert.NotNull(cleared);
        Assert.False(cleared!.IsSyncPending);
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
        // Service counts only TodoLists pending
        Assert.Equal(1, result);
    }
}


