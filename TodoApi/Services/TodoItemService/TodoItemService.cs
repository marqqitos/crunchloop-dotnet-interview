using Microsoft.EntityFrameworkCore;
using TodoApi.Dtos;
using TodoApi.Models;

namespace TodoApi.Services.TodoItemService;

public class TodoItemService : ITodoItemService
{
    private readonly TodoContext _context;
    private readonly ILogger<TodoItemService> _logger;

    public TodoItemService(TodoContext context, ILogger<TodoItemService> logger)
    {
        _context = context;
        _logger = logger;
    }

    public async Task<bool> TodoListExists(long todoListId)
        => await _context.TodoList.AnyAsync(t => t.Id == todoListId && !t.IsDeleted);

    public async Task<IList<TodoItemResponse>?> GetTodoItems(long todoListId)
    {
        var todoList = await _context.TodoList
            .FirstOrDefaultAsync(tl => tl.Id == todoListId && !tl.IsDeleted);

        if (todoList is null)
            return null;

        var items = await _context.TodoItem
            .Where(item => item.TodoListId == todoListId && !item.IsDeleted)
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

    public async Task<TodoItemResponse?> GetTodoItemById(long todoListId, long id)
    {
        var todoList = await _context.TodoList
            .FirstOrDefaultAsync(tl => tl.Id == todoListId && !tl.IsDeleted);

        if (todoList is null)
            return null;

        var todoItem = await _context.TodoItem
            .Where(item => item.Id == id && item.TodoListId == todoListId && !item.IsDeleted)
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

    public async Task<TodoItemResponse?> UpdateTodoItem(long todoListId, long id, UpdateTodoItem payload)
    {
        var todoList = await _context.TodoList
            .FirstOrDefaultAsync(tl => tl.Id == todoListId && !tl.IsDeleted);

        if (todoList is null)
            return null;

        var todoItem = await _context.TodoItem.FirstOrDefaultAsync(item => item.Id == id && item.TodoListId == todoListId && !item.IsDeleted);

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

    public async Task<TodoItemResponse?> CreateTodoItem(long todoListId, CreateTodoItem payload)
    {
        var todoList = await _context.TodoList
            .FirstOrDefaultAsync(tl => tl.Id == todoListId && !tl.IsDeleted);

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

    public async Task<bool> DeleteTodoItem(long todoListId, long id)
    {
        var todoList = await _context.TodoList
            .FirstOrDefaultAsync(tl => tl.Id == todoListId && !tl.IsDeleted);

        if (todoList is null)
            return false;

        var todoItem = await _context.TodoItem.FirstOrDefaultAsync(item => item.Id == id && item.TodoListId == todoListId && !item.IsDeleted);

        if (todoItem is null)
            return false;

        // Soft delete the TodoItem
        var now = DateTime.UtcNow;
        todoItem.IsDeleted = true;
        todoItem.DeletedAt = now;
        todoItem.IsSyncPending = true;
        todoItem.LastModified = now;

        // Also mark the parent TodoList as pending since it contains changed items
        todoList.IsSyncPending = true;
        todoList.LastModified = now;

        await _context.SaveChangesAsync();
        _logger.LogInformation("Soft deleted TodoItem {TodoItemId}", id);
        return true;
    }

    public async Task MarkAsPending(long todoItemId)
    {
        var todoItem = await _context.TodoItem
            .FirstOrDefaultAsync(ti => ti.Id == todoItemId);

        if (todoItem is not null)
        {
            todoItem.IsSyncPending = true;
            todoItem.LastModified = DateTime.UtcNow;

            // Also mark the parent TodoList as pending since it contains changed items
            var todoList = await _context.TodoList
                .FirstOrDefaultAsync(tl => tl.Id == todoItem.TodoListId);

            if (todoList is not null)
            {
                todoList.IsSyncPending = true;
                todoList.LastModified = DateTime.UtcNow;
            }

            await _context.SaveChangesAsync();

            _logger.LogDebug("Marked TodoItem {TodoItemId} and its parent TodoList {TodoListId} as pending sync",
                todoItemId, todoItem.TodoListId);
        }
        else
        {
            _logger.LogWarning("Attempted to mark non-existent TodoItem {TodoItemId} as pending", todoItemId);
        }
    }

    public async Task ClearPendingFlag(long todoItemId)
    {
        var todoItem = await _context.TodoItem
            .FirstOrDefaultAsync(ti => ti.Id == todoItemId);
        if (todoItem is not null)
        {
            todoItem.IsSyncPending = false;
            todoItem.LastSyncedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync();

            _logger.LogDebug("Cleared pending flag for TodoItem {TodoItemId}", todoItemId);
        }
    }

    public async Task<IEnumerable<TodoItem>> GetOrphanedTodoItems(IEnumerable<string> externalItemIds)
    {
        var orphanedLocalItems = await _context.TodoItem
            .Where(ti => !ti.IsDeleted && !externalItemIds.Contains(ti.ExternalId))
            .ToListAsync();

        return orphanedLocalItems;
    }
}


