using Microsoft.EntityFrameworkCore;
using TodoApi.Tests.Builders;

namespace TodoApi.Tests.Services;

public class DeltaSyncTests
{
    private readonly TodoContext _context;
    private readonly Mock<ILogger<TodoListSyncStateService>> _mockLogger;

    private readonly ISyncStateService _sut;

    public DeltaSyncTests()
    {
        var options = new DbContextOptionsBuilder<TodoContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _context = new TodoContext(options);
        _mockLogger = new Mock<ILogger<TodoListSyncStateService>>();

        _sut = new TodoListSyncStateService(
            _context,
            _mockLogger.Object);
    }

    [Fact]
    public async Task SyncStateService_GetLastSyncTimestamp_ReturnsMostRecentTimestamp()
    {
        // Arrange
        var todoList = TodoListBuilder.Create()
            .WithName("Test List")
            .WithExternalId("ext-123")
            .WithLastSyncedAt(DateTime.UtcNow.AddHours(-2))
            .Build();
        _context.TodoList.Add(todoList);
        await _context.SaveChangesAsync();

        var todoItem = TodoItemBuilder.Create()
            .WithDescription("Test Item")
            .WithExternalId("item-123")
            .WithLastSyncedAt(DateTime.UtcNow.AddHours(-1))
            .WithTodoListId(todoList.Id)
            .Build();
        _context.TodoItem.Add(todoItem);
        await _context.SaveChangesAsync();

        // Act
        var result = await _sut.GetLastSyncTimestampAsync();

        // Assert
        Assert.NotNull(result);
        Assert.NotNull(todoItem.LastSyncedAt);
        Assert.Equal(todoItem.LastSyncedAt.Value, result); // Should return the more recent timestamp
    }

    [Fact]
    public async Task SyncStateService_UpdateLastSyncTimestamp_UpdatesAllSyncedEntities()
    {
        // Arrange
        var todoList = TodoListBuilder.Create()
            .WithName("Test List")
            .WithExternalId("ext-123")
            .WithLastSyncedAt(DateTime.UtcNow.AddHours(-1))
            .Build();
        _context.TodoList.Add(todoList);
        await _context.SaveChangesAsync();

        var todoItem = TodoItemBuilder.Create()
            .WithDescription("Test Item")
            .WithExternalId("item-123")
            .WithLastSyncedAt(DateTime.UtcNow.AddHours(-1))
            .WithTodoListId(todoList.Id)
            .Build();
        _context.TodoItem.Add(todoItem);
        await _context.SaveChangesAsync();

        var newSyncTime = DateTime.UtcNow;

        // Act
        await _sut.UpdateLastSyncTimestampAsync(newSyncTime);

        // Assert
        var updatedTodoList = await _context.TodoList.FindAsync(todoList.Id);
        var updatedTodoItem = await _context.TodoItem.FindAsync(todoItem.Id);

        Assert.NotNull(updatedTodoList);
        Assert.NotNull(updatedTodoItem);
        Assert.Equal(newSyncTime, updatedTodoList.LastSyncedAt);
        Assert.Equal(newSyncTime, updatedTodoItem.LastSyncedAt);
    }

    [Fact]
    public async Task SyncStateService_IsDeltaSyncAvailable_ReturnsTrueWhenLastSyncExists()
    {
        // Arrange
        var todoList = TodoListBuilder.Create()
            .WithName("Test List")
            .WithExternalId("ext-123")
            .WithLastSyncedAt(DateTime.UtcNow.AddHours(-1))
            .Build();
        _context.TodoList.Add(todoList);
        await _context.SaveChangesAsync();

        // Act
        var result = await _sut.IsDeltaSyncAvailableAsync();

        // Assert
        Assert.True(result);
    }

    [Fact]
    public async Task SyncStateService_IsDeltaSyncAvailable_ReturnsFalseWhenNoLastSync()
    {
        // Arrange
        var todoList = TodoListBuilder.Create()
            .WithName("Test List")
            .WithExternalId("ext-123")
            .WithLastSyncedAt(null) // No previous sync
            .Build();
        _context.TodoList.Add(todoList);
        await _context.SaveChangesAsync();

        // Act
        var result = await _sut.IsDeltaSyncAvailableAsync();

        // Assert
        Assert.False(result);
    }
}
