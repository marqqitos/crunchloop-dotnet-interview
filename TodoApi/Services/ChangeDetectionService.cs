using Microsoft.EntityFrameworkCore;
using TodoApi.Models;

namespace TodoApi.Services;

/// <summary>
/// Service for detecting changes and determining when sync is needed
/// </summary>
public class ChangeDetectionService : IChangeDetectionService
{
    private readonly TodoContext _context;
    private readonly ILogger<ChangeDetectionService> _logger;

    public ChangeDetectionService(TodoContext context, ILogger<ChangeDetectionService> logger)
    {
        _context = context;
        _logger = logger;
    }

    public async Task<bool> HasPendingChangesAsync()
    {
        var hasPendingTodoLists = await _context.TodoList
            .AnyAsync(tl => tl.IsSyncPending);

        var hasPendingTodoItems = await _context.TodoItem
            .AnyAsync(ti => ti.IsSyncPending);

        var hasPendingChanges = hasPendingTodoLists || hasPendingTodoItems;

        if (hasPendingChanges)
        {
            _logger.LogDebug("Found pending changes: TodoLists={TodoListsPending}, TodoItems={TodoItemsPending}",
                await _context.TodoList.CountAsync(tl => tl.IsSyncPending),
                await _context.TodoItem.CountAsync(ti => ti.IsSyncPending));
        }

        return hasPendingChanges;
    }

    public async Task MarkTodoListAsPendingAsync(long todoListId)
    {
        var todoList = await _context.TodoList.FindAsync(todoListId);
        if (todoList != null)
        {
            todoList.IsSyncPending = true;
            todoList.LastModified = DateTime.UtcNow;
            await _context.SaveChangesAsync();
            
            _logger.LogDebug("Marked TodoList {TodoListId} as pending sync", todoListId);
        }
        else
        {
            _logger.LogWarning("Attempted to mark non-existent TodoList {TodoListId} as pending", todoListId);
        }
    }

    public async Task MarkTodoItemAsPendingAsync(long todoItemId)
    {
        var todoItem = await _context.TodoItem.FindAsync(todoItemId);
        if (todoItem != null)
        {
            todoItem.IsSyncPending = true;
            todoItem.LastModified = DateTime.UtcNow;
            
            // Also mark the parent TodoList as pending since it contains changed items
            var todoList = await _context.TodoList.FindAsync(todoItem.TodoListId);
            if (todoList != null)
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

    public async Task ClearTodoListPendingFlagAsync(long todoListId)
    {
        var todoList = await _context.TodoList.FindAsync(todoListId);
        if (todoList != null)
        {
            todoList.IsSyncPending = false;
            todoList.LastSyncedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync();
            
            _logger.LogDebug("Cleared pending flag for TodoList {TodoListId}", todoListId);
        }
    }

    public async Task ClearTodoItemPendingFlagAsync(long todoItemId)
    {
        var todoItem = await _context.TodoItem.FindAsync(todoItemId);
        if (todoItem != null)
        {
            todoItem.IsSyncPending = false;
            todoItem.LastSyncedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync();
            
            _logger.LogDebug("Cleared pending flag for TodoItem {TodoItemId}", todoItemId);
        }
    }

    public async Task<int> GetPendingChangesCountAsync()
    {
        var pendingTodoLists = await _context.TodoList.CountAsync(tl => tl.IsSyncPending);
        var pendingTodoItems = await _context.TodoItem.CountAsync(ti => ti.IsSyncPending);
        
        return pendingTodoLists + pendingTodoItems;
    }
} 