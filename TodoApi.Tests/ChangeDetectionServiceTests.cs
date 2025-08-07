using Microsoft.EntityFrameworkCore;

namespace TodoApi.Tests;

public class ChangeDetectionServiceTests
{
    private readonly DbContextOptions<TodoContext> _options;
    private readonly Mock<ILogger<ChangeDetectionService>> _mockLogger;

    public ChangeDetectionServiceTests()
    {
        _options = new DbContextOptionsBuilder<TodoContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
        _mockLogger = new Mock<ILogger<ChangeDetectionService>>();
    }

    [Fact]
    public async Task HasPendingChangesAsync_WhenNoPendingChanges_ReturnsFalse()
    {
        // Arrange
        using var context = new TodoContext(_options);
        var service = new ChangeDetectionService(context, _mockLogger.Object);

        // Act
        var result = await service.HasPendingChangesAsync();

        // Assert
        Assert.False(result);
    }

    [Fact]
    public async Task HasPendingChangesAsync_WhenTodoListPending_ReturnsTrue()
    {
        // Arrange
        using var context = new TodoContext(_options);
        var todoList = new TodoList { Name = "Test List", IsSyncPending = true };
        context.TodoList.Add(todoList);
        await context.SaveChangesAsync();

        var service = new ChangeDetectionService(context, _mockLogger.Object);

        // Act
        var result = await service.HasPendingChangesAsync();

        // Assert
        Assert.True(result);
    }

    [Fact]
    public async Task HasPendingChangesAsync_WhenTodoItemPending_ReturnsTrue()
    {
        // Arrange
        using var context = new TodoContext(_options);
        var todoList = new TodoList { Name = "Test List" };
        context.TodoList.Add(todoList);
        await context.SaveChangesAsync();

        var todoItem = new TodoItem 
        { 
            Description = "Test Item", 
            TodoListId = todoList.Id, 
            IsSyncPending = true 
        };
        context.TodoItem.Add(todoItem);
        await context.SaveChangesAsync();

        var service = new ChangeDetectionService(context, _mockLogger.Object);

        // Act
        var result = await service.HasPendingChangesAsync();

        // Assert
        Assert.True(result);
    }

    [Fact]
    public async Task MarkTodoListAsPendingAsync_WhenTodoListExists_MarksAsPending()
    {
        // Arrange
        using var context = new TodoContext(_options);
        var todoList = new TodoList { Name = "Test List" };
        context.TodoList.Add(todoList);
        await context.SaveChangesAsync();

        var service = new ChangeDetectionService(context, _mockLogger.Object);

        // Act
        await service.MarkTodoListAsPendingAsync(todoList.Id);

        // Assert
        var updatedTodoList = await context.TodoList.FindAsync(todoList.Id);
        Assert.True(updatedTodoList!.IsSyncPending);
    }

    [Fact]
    public async Task MarkTodoItemAsPendingAsync_WhenTodoItemExists_MarksAsPending()
    {
        // Arrange
        using var context = new TodoContext(_options);
        var todoList = new TodoList { Name = "Test List" };
        context.TodoList.Add(todoList);
        await context.SaveChangesAsync();

        var todoItem = new TodoItem 
        { 
            Description = "Test Item", 
            TodoListId = todoList.Id 
        };
        context.TodoItem.Add(todoItem);
        await context.SaveChangesAsync();

        var service = new ChangeDetectionService(context, _mockLogger.Object);

        // Act
        await service.MarkTodoItemAsPendingAsync(todoItem.Id);

        // Assert
        var updatedTodoItem = await context.TodoItem.FindAsync(todoItem.Id);
        var updatedTodoList = await context.TodoList.FindAsync(todoList.Id);
        Assert.True(updatedTodoItem!.IsSyncPending);
        Assert.True(updatedTodoList!.IsSyncPending); // Parent should also be marked
    }

    [Fact]
    public async Task ClearTodoListPendingFlagAsync_WhenTodoListExists_ClearsPendingFlag()
    {
        // Arrange
        using var context = new TodoContext(_options);
        var todoList = new TodoList { Name = "Test List", IsSyncPending = true };
        context.TodoList.Add(todoList);
        await context.SaveChangesAsync();

        var service = new ChangeDetectionService(context, _mockLogger.Object);

        // Act
        await service.ClearTodoListPendingFlagAsync(todoList.Id);

        // Assert
        var updatedTodoList = await context.TodoList.FindAsync(todoList.Id);
        Assert.False(updatedTodoList!.IsSyncPending);
        Assert.NotNull(updatedTodoList.LastSyncedAt);
    }

    [Fact]
    public async Task GetPendingChangesCountAsync_ReturnsCorrectCount()
    {
        // Arrange
        using var context = new TodoContext(_options);
        var todoList1 = new TodoList { Name = "Test List 1", IsSyncPending = true };
        var todoList2 = new TodoList { Name = "Test List 2" };
        context.TodoList.AddRange(todoList1, todoList2);
        await context.SaveChangesAsync();

        var todoItem = new TodoItem 
        { 
            Description = "Test Item", 
            TodoListId = todoList2.Id, 
            IsSyncPending = true 
        };
        context.TodoItem.Add(todoItem);
        await context.SaveChangesAsync();

        var service = new ChangeDetectionService(context, _mockLogger.Object);

        // Act
        var result = await service.GetPendingChangesCountAsync();

        // Assert
        Assert.Equal(2, result); // 1 TodoList + 1 TodoItem
    }
} 