using Microsoft.EntityFrameworkCore;
using TodoApi.Dtos;

namespace TodoApi.Tests.Services;

public class TodoListServiceTests
{
    private readonly TodoContext _context;
    private readonly ITodoListService _service;

    public TodoListServiceTests()
    {
        var options = new DbContextOptionsBuilder<TodoContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _context = new TodoContext(options);
        _service = new TodoListService(_context);
    }

    [Fact]
    public async Task CreateTodoListAsync_Creates_WithPendingAndName()
    {
        var created = await _service.CreateTodoListAsync(new CreateTodoList { Name = "New List" });
        Assert.NotNull(created);
        Assert.Equal("New List", created.Name);

        var entity = await _context.TodoList.FindAsync(created.Id);
        Assert.NotNull(entity);
        Assert.True(entity!.IsSyncPending);
    }

    [Fact]
    public async Task GetTodoListsAsync_ReturnsAll()
    {
        _context.TodoList.Add(new TodoList { Name = "A" });
        _context.TodoList.Add(new TodoList { Name = "B" });
        await _context.SaveChangesAsync();

        var all = await _service.GetTodoListsAsync();
        Assert.Equal(2, all.Count);
    }

    [Fact]
    public async Task GetTodoListAsync_ReturnsNull_WhenMissing()
    {
        var res = await _service.GetTodoListAsync(999);
        Assert.Null(res);
    }

    [Fact]
    public async Task UpdateTodoListAsync_UpdatesNameAndPending()
    {
        var list = new TodoList { Name = "Old" };
        _context.TodoList.Add(list);
        await _context.SaveChangesAsync();

        var updated = await _service.UpdateTodoListAsync(list.Id, new UpdateTodoList { Name = "New" });
        Assert.NotNull(updated);
        Assert.Equal("New", updated!.Name);

        var entity = await _context.TodoList.FindAsync(list.Id);
        Assert.True(entity!.IsSyncPending);
    }

    [Fact]
    public async Task DeleteTodoListAsync_Deletes_WhenExists()
    {
        var list = new TodoList { Name = "Del" };
        _context.TodoList.Add(list);
        await _context.SaveChangesAsync();

        var ok = await _service.DeleteTodoListAsync(list.Id);
        Assert.True(ok);
        Assert.Equal(0, _context.TodoList.Count());
    }
}


