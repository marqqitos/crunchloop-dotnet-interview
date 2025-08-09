using Microsoft.EntityFrameworkCore;
using TodoApi.Dtos;
using TodoApi.Models;

namespace TodoApi.Services;

public class TodoItemService : ITodoItemService
{
    private readonly TodoContext _context;

    public TodoItemService(TodoContext context)
    {
        _context = context;
    }

	public async Task<bool> TodoListExistsAsync(long todoListId)
		=> await _context.TodoList.AnyAsync(t => t.Id == todoListId);

	public async Task<IList<TodoItemResponse>?> GetTodoItemsAsync(long todoListId)
    {
        var todoList = await _context.TodoList.FindAsync(todoListId);

		if (todoList is null)
			return null;

        var items = await _context.TodoItem
            .Where(item => item.TodoListId == todoListId)
            .Select(item => new TodoItemResponse
            {
                Id = item.Id,
                Description = item.Description,
                Completed = item.IsCompleted,
                TodoListId = item.TodoListId
            })
            .ToListAsync();

        return items;
    }

    public async Task<TodoItemResponse?> GetTodoItemAsync(long todoListId, long id)
    {
        var todoList = await _context.TodoList.FindAsync(todoListId);

		if (todoList is null)
			return null;

        var todoItem = await _context.TodoItem
            .Where(item => item.Id == id && item.TodoListId == todoListId)
            .Select(item => new TodoItemResponse
            {
                Id = item.Id,
                Description = item.Description,
                Completed = item.IsCompleted,
                TodoListId = item.TodoListId
            })
            .FirstOrDefaultAsync();

        return todoItem;
    }

    public async Task<TodoItemResponse?> UpdateTodoItemAsync(long todoListId, long id, UpdateTodoItem payload)
    {
        var todoList = await _context.TodoList.FindAsync(todoListId);

		if (todoList is null)
			return null;

        var todoItem = await _context.TodoItem.FirstOrDefaultAsync(item => item.Id == id && item.TodoListId == todoListId);

		if (todoItem is null)
			return null;

        todoItem.Description = payload.Description;
        todoItem.IsCompleted = payload.Completed;
        todoItem.LastModified = DateTime.UtcNow;
        todoItem.IsSyncPending = true;

        await _context.SaveChangesAsync();

        return new TodoItemResponse
        {
            Id = todoItem.Id,
            Description = todoItem.Description,
            Completed = todoItem.IsCompleted,
            TodoListId = todoItem.TodoListId
        };
    }

    public async Task<TodoItemResponse?> CreateTodoItemAsync(long todoListId, CreateTodoItem payload)
    {
        var todoList = await _context.TodoList.FindAsync(todoListId);

		if (todoList is null)
			return null;

        var todoItem = new TodoItem
        {
            Description = payload.Description,
            IsCompleted = payload.Completed,
            TodoListId = todoListId,
            LastModified = DateTime.UtcNow,
            IsSyncPending = true
        };

        _context.TodoItem.Add(todoItem);
        await _context.SaveChangesAsync();

        return new TodoItemResponse
        {
            Id = todoItem.Id,
            Description = todoItem.Description,
            Completed = todoItem.IsCompleted,
            TodoListId = todoItem.TodoListId
        };
    }

    public async Task<bool> DeleteTodoItemAsync(long todoListId, long id)
    {
        var todoList = await _context.TodoList.FindAsync(todoListId);

		if (todoList is null)
			return false;

        var todoItem = await _context.TodoItem.FirstOrDefaultAsync(item => item.Id == id && item.TodoListId == todoListId);

		if (todoItem is null)
			return false;

        _context.TodoItem.Remove(todoItem);
        await _context.SaveChangesAsync();
        return true;
    }
}


