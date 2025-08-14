using Microsoft.EntityFrameworkCore;
using TodoApi.Services.SyncStateService;
using TodoApi.Tests.Builders;

namespace TodoApi.Tests.Services.SyncServiceStateTests;

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
        var result = await _sut.GetLastSyncTimestamp();

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
        await _sut.UpdateLastSyncTimestamp(newSyncTime);

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
        var result = await _sut.IsDeltaSyncAvailable();

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
        var result = await _sut.IsDeltaSyncAvailable();

        // Assert
        Assert.False(result);
    }

    [Fact]
    public async Task GetEarliestLastModifiedAsync_WithBothTodoListsAndItems_ReturnsEarliestTimestamp()
    {
        // Arrange
        var earliestTime = new DateTime(2025, 1, 1, 10, 0, 0, DateTimeKind.Utc);
        var middleTime = new DateTime(2025, 1, 1, 12, 0, 0, DateTimeKind.Utc);
        var latestTime = new DateTime(2025, 1, 1, 14, 0, 0, DateTimeKind.Utc);

        // Create TodoLists with different LastModified timestamps
        var todoList1 = TodoListBuilder.Create()
            .WithName("List 1")
            .WithLastModified(middleTime)
            .Build();

        var todoList2 = TodoListBuilder.Create()
            .WithName("List 2")
            .WithLastModified(latestTime)
            .Build();

        _context.TodoList.AddRange(todoList1, todoList2);
        await _context.SaveChangesAsync();

        // Create TodoItems with different LastModified timestamps
        var todoItem1 = TodoItemBuilder.Create()
            .WithDescription("Item 1")
            .WithLastModified(earliestTime) // This should be the earliest
            .WithTodoListId(todoList1.Id)
            .Build();

        var todoItem2 = TodoItemBuilder.Create()
            .WithDescription("Item 2")
            .WithLastModified(middleTime)
            .WithTodoListId(todoList2.Id)
            .Build();

        _context.TodoItem.AddRange(todoItem1, todoItem2);
        await _context.SaveChangesAsync();

        // Act
        var result = await _sut.GetEarliestLastModified();

        // Assert
        Assert.NotNull(result);
        Assert.Equal(earliestTime, result.Value);
    }

    [Fact]
    public async Task GetEarliestLastModifiedAsync_WithOnlyTodoLists_ReturnsEarliestTodoListTimestamp()
    {
        // Arrange
        var earliestTime = new DateTime(2025, 1, 1, 10, 0, 0, DateTimeKind.Utc);
        var laterTime = new DateTime(2025, 1, 1, 12, 0, 0, DateTimeKind.Utc);

        var todoList1 = TodoListBuilder.Create()
            .WithName("List 1")
            .WithLastModified(laterTime)
            .Build();

        var todoList2 = TodoListBuilder.Create()
            .WithName("List 2")
            .WithLastModified(earliestTime) // This should be the earliest
            .Build();

        _context.TodoList.AddRange(todoList1, todoList2);
        await _context.SaveChangesAsync();

        // Act
        var result = await _sut.GetEarliestLastModified();

        // Assert
        Assert.NotNull(result);
        Assert.Equal(earliestTime, result.Value);
    }

    [Fact]
    public async Task GetEarliestLastModifiedAsync_WithOnlyTodoItems_ReturnsEarliestTodoItemTimestamp()
    {
        // Arrange
        var earliestTime = new DateTime(2025, 1, 1, 10, 0, 0, DateTimeKind.Utc);
        var laterTime = new DateTime(2025, 1, 1, 12, 0, 0, DateTimeKind.Utc);

        // Create a TodoList first (required for foreign key)
        var todoList = TodoListBuilder.Create()
            .WithName("Parent List")
            .WithLastModified(default) // Default timestamp, should not affect result
            .Build();
        _context.TodoList.Add(todoList);
        await _context.SaveChangesAsync();

        var todoItem1 = TodoItemBuilder.Create()
            .WithDescription("Item 1")
            .WithLastModified(laterTime)
            .WithTodoListId(todoList.Id)
            .Build();

        var todoItem2 = TodoItemBuilder.Create()
            .WithDescription("Item 2")
            .WithLastModified(earliestTime) // This should be the earliest
            .WithTodoListId(todoList.Id)
            .Build();

        _context.TodoItem.AddRange(todoItem1, todoItem2);
        await _context.SaveChangesAsync();

        // Act
        var result = await _sut.GetEarliestLastModified();

        // Assert
        Assert.NotNull(result);
        Assert.Equal(earliestTime, result.Value);
    }

    [Fact]
    public async Task GetEarliestLastModifiedAsync_WithNoEntities_ReturnsNull()
    {
        // Arrange - Empty database

        // Act
        var result = await _sut.GetEarliestLastModified();

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public async Task GetEarliestLastModifiedAsync_WithDefaultTimestamps_ReturnsNull()
    {
        // Arrange
        var todoList = TodoListBuilder.Create()
            .WithName("List with default timestamp")
            .WithLastModified(default) // Default DateTime value
            .Build();
        _context.TodoList.Add(todoList);
        await _context.SaveChangesAsync();

        var todoItem = TodoItemBuilder.Create()
            .WithDescription("Item with default timestamp")
            .WithLastModified(default) // Default DateTime value
            .WithTodoListId(todoList.Id)
            .Build();
        _context.TodoItem.Add(todoItem);
        await _context.SaveChangesAsync();

        // Act
        var result = await _sut.GetEarliestLastModified();

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public async Task GetEarliestLastModifiedAsync_WithMixedDefaultAndValidTimestamps_ReturnsEarliestValid()
    {
        // Arrange
        var validTime = new DateTime(2025, 1, 1, 10, 0, 0, DateTimeKind.Utc);

        // TodoList with default timestamp
        var todoListWithDefault = TodoListBuilder.Create()
            .WithName("List with default")
            .WithLastModified(default)
            .Build();

        // TodoList with valid timestamp
        var todoListWithValid = TodoListBuilder.Create()
            .WithName("List with valid timestamp")
            .WithLastModified(validTime)
            .Build();

        _context.TodoList.AddRange(todoListWithDefault, todoListWithValid);
        await _context.SaveChangesAsync();

        // TodoItem with default timestamp
        var todoItemWithDefault = TodoItemBuilder.Create()
            .WithDescription("Item with default")
            .WithLastModified(default)
            .WithTodoListId(todoListWithDefault.Id)
            .Build();

        _context.TodoItem.Add(todoItemWithDefault);
        await _context.SaveChangesAsync();

        // Act
        var result = await _sut.GetEarliestLastModified();

        // Assert
        Assert.NotNull(result);
        Assert.Equal(validTime, result.Value);
    }

    [Fact]
    public async Task GetEarliestLastModifiedAsync_WithTodoListEarlierThanTodoItem_ReturnsTodoListTimestamp()
    {
        // Arrange
        var todoListTime = new DateTime(2025, 1, 1, 8, 0, 0, DateTimeKind.Utc); // Earlier
        var todoItemTime = new DateTime(2025, 1, 1, 10, 0, 0, DateTimeKind.Utc); // Later

        var todoList = TodoListBuilder.Create()
            .WithName("Earlier List")
            .WithLastModified(todoListTime)
            .Build();
        _context.TodoList.Add(todoList);
        await _context.SaveChangesAsync();

        var todoItem = TodoItemBuilder.Create()
            .WithDescription("Later Item")
            .WithLastModified(todoItemTime)
            .WithTodoListId(todoList.Id)
            .Build();
        _context.TodoItem.Add(todoItem);
        await _context.SaveChangesAsync();

        // Act
        var result = await _sut.GetEarliestLastModified();

        // Assert
        Assert.NotNull(result);
        Assert.Equal(todoListTime, result.Value);
    }

    [Fact]
    public async Task GetEarliestLastModifiedAsync_WithTodoItemEarlierThanTodoList_ReturnsTodoItemTimestamp()
    {
        // Arrange
        var todoListTime = new DateTime(2025, 1, 1, 10, 0, 0, DateTimeKind.Utc); // Later
        var todoItemTime = new DateTime(2025, 1, 1, 8, 0, 0, DateTimeKind.Utc); // Earlier

        var todoList = TodoListBuilder.Create()
            .WithName("Later List")
            .WithLastModified(todoListTime)
            .Build();
        _context.TodoList.Add(todoList);
        await _context.SaveChangesAsync();

        var todoItem = TodoItemBuilder.Create()
            .WithDescription("Earlier Item")
            .WithLastModified(todoItemTime)
            .WithTodoListId(todoList.Id)
            .Build();
        _context.TodoItem.Add(todoItem);
        await _context.SaveChangesAsync();

        // Act
        var result = await _sut.GetEarliestLastModified();

        // Assert
        Assert.NotNull(result);
        Assert.Equal(todoItemTime, result.Value);
    }

    [Fact]
    public async Task GetEarliestLastModifiedAsync_WithIdenticalTimestamps_ReturnsTheTimestamp()
    {
        // Arrange
        var identicalTime = new DateTime(2025, 1, 1, 10, 0, 0, DateTimeKind.Utc);

        var todoList = TodoListBuilder.Create()
            .WithName("List")
            .WithLastModified(identicalTime)
            .Build();
        _context.TodoList.Add(todoList);
        await _context.SaveChangesAsync();

        var todoItem = TodoItemBuilder.Create()
            .WithDescription("Item")
            .WithLastModified(identicalTime)
            .WithTodoListId(todoList.Id)
            .Build();
        _context.TodoItem.Add(todoItem);
        await _context.SaveChangesAsync();

        // Act
        var result = await _sut.GetEarliestLastModified();

        // Assert
        Assert.NotNull(result);
        Assert.Equal(identicalTime, result.Value);
    }
}
