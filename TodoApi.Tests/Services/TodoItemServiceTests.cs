using Microsoft.EntityFrameworkCore;
using TodoApi.Dtos;

namespace TodoApi.Tests.Services;

public class TodoItemServiceTests
{
    private readonly TodoContext _context;
    private readonly ITodoItemService _service;

    public TodoItemServiceTests()
    {
        var options = new DbContextOptionsBuilder<TodoContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
        _context = new TodoContext(options);
        _service = new TodoItemService(_context);
    }

    [Fact]
    public async Task GetTodoItemsAsync_ReturnsItems_WhenListExists()
    {
        var list = new TodoList { Name = "List A" };
        _context.TodoList.Add(list);
        await _context.SaveChangesAsync();

        _context.TodoItem.AddRange(
            new TodoItem { Description = "A", TodoListId = list.Id },
            new TodoItem { Description = "B", TodoListId = list.Id }
        );
        await _context.SaveChangesAsync();

        var items = await _service.GetTodoItemsAsync(list.Id);

        Assert.NotNull(items);
        Assert.Equal(2, items!.Count);
        Assert.Contains(items, i => i.Description == "A");
        Assert.Contains(items, i => i.Description == "B");
    }

    [Fact]
    public async Task GetTodoItemsAsync_ReturnsNull_WhenListNotFound()
    {
        var items = await _service.GetTodoItemsAsync(12345);
        Assert.Null(items);
    }

    [Fact]
    public async Task GetTodoItemAsync_ReturnsItem_WhenExists()
    {
        var list = new TodoList { Name = "List" };
        _context.TodoList.Add(list);
        await _context.SaveChangesAsync();

        var item = new TodoItem { Description = "X", TodoListId = list.Id, IsCompleted = true };
        _context.TodoItem.Add(item);
        await _context.SaveChangesAsync();

        var result = await _service.GetTodoItemAsync(list.Id, item.Id);

        Assert.NotNull(result);
        Assert.Equal(item.Id, result!.Id);
        Assert.True(result.Completed);
    }

    [Fact]
    public async Task GetTodoItemAsync_ReturnsNull_WhenListOrItemMissing()
    {
        var list = new TodoList { Name = "List" };
        _context.TodoList.Add(list);
        await _context.SaveChangesAsync();

        var resultMissingItem = await _service.GetTodoItemAsync(list.Id, 9999);
        var resultMissingList = await _service.GetTodoItemAsync(9999, 1);

        Assert.Null(resultMissingItem);
        Assert.Null(resultMissingList);
    }

    [Fact]
    public async Task UpdateTodoItemAsync_UpdatesFieldsAndMarksPending()
    {
        var list = new TodoList { Name = "List" };
        _context.TodoList.Add(list);
        await _context.SaveChangesAsync();

        var item = new TodoItem { Description = "Old", IsCompleted = false, TodoListId = list.Id, LastModified = DateTime.UtcNow.AddHours(-1) };
        _context.TodoItem.Add(item);
        await _context.SaveChangesAsync();

        var payload = new UpdateTodoItem { Description = "New", Completed = true };
        var before = DateTime.UtcNow.AddSeconds(-1);
        var updated = await _service.UpdateTodoItemAsync(list.Id, item.Id, payload);
        var after = DateTime.UtcNow.AddSeconds(1);

        Assert.NotNull(updated);
        Assert.Equal("New", updated!.Description);
        Assert.True(updated.Completed);

        var reloaded = await _context.TodoItem.FindAsync(item.Id);
        Assert.True(reloaded!.IsSyncPending);
        Assert.InRange(reloaded.LastModified, before, after);
    }

    [Fact]
    public async Task CreateTodoItemAsync_CreatesItem_WithPendingAndTimestamp()
    {
        var list = new TodoList { Name = "List" };
        _context.TodoList.Add(list);
        await _context.SaveChangesAsync();

        var payload = new CreateTodoItem { Description = "Created", Completed = false };
        var created = await _service.CreateTodoItemAsync(list.Id, payload);

        Assert.NotNull(created);
        Assert.Equal("Created", created!.Description);

        var entity = await _context.TodoItem.FindAsync(created.Id);
        Assert.NotNull(entity);
        Assert.True(entity!.IsSyncPending);
        Assert.InRange(entity.LastModified, DateTime.UtcNow.AddMinutes(-1), DateTime.UtcNow.AddMinutes(1));
    }

    [Fact]
    public async Task DeleteTodoItemAsync_RemovesItem_WhenExists()
    {
        var list = new TodoList { Name = "List" };
        _context.TodoList.Add(list);
        await _context.SaveChangesAsync();

        var item = new TodoItem { Description = "ToDelete", TodoListId = list.Id };
        _context.TodoItem.Add(item);
        await _context.SaveChangesAsync();

        var ok = await _service.DeleteTodoItemAsync(list.Id, item.Id);
        Assert.True(ok);

        var stillThere = await _context.TodoItem.FindAsync(item.Id);
        Assert.Null(stillThere);
    }

    [Fact]
    public async Task DeleteTodoItemAsync_ReturnsFalse_WhenNotFound()
    {
        var okMissingList = await _service.DeleteTodoItemAsync(9999, 1);
        Assert.False(okMissingList);
    }
}


