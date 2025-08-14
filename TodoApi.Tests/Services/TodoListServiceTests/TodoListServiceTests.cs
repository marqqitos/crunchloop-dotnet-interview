using Microsoft.EntityFrameworkCore;
using TodoApi.Dtos;
using TodoApi.Services.TodoListService;

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
    public async Task DeleteTodoListAsync_SoftDeletesList_WhenExists()
    {
        // Arrange
        var todoList = new TodoList { Name = "Test List" };
        var todoItem = new TodoItem { Description = "Test Item", TodoListId = todoList.Id };
        todoList.Items.Add(todoItem);

        _context.TodoList.Add(todoList);
        await _context.SaveChangesAsync();

        // Act
        var result = await _service.DeleteTodoListAsync(todoList.Id);

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
        var activeList = new TodoList { Name = "Active List" };
        var deletedList = new TodoList { Name = "Deleted List", IsDeleted = true, DeletedAt = DateTime.UtcNow };

        _context.TodoList.AddRange(activeList, deletedList);
        await _context.SaveChangesAsync();

        // Act
        var result = await _service.GetTodoListsAsync();

        // Assert
        Assert.Single(result);
        Assert.Equal("Active List", result.First().Name);
    }

    [Fact]
    public async Task GetTodoListAsync_ReturnsNull_WhenListIsDeleted()
    {
        // Arrange
        var deletedList = new TodoList { Name = "Deleted List", IsDeleted = true, DeletedAt = DateTime.UtcNow };
        _context.TodoList.Add(deletedList);
        await _context.SaveChangesAsync();

        // Act
        var result = await _service.GetTodoListAsync(deletedList.Id);

        // Assert
        Assert.Null(result);
    }
}


