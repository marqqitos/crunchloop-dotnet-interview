using TodoApi.Dtos;
using TodoApi.Models;

namespace TodoApi.Services.TodoItemService;

public interface ITodoItemService
{
    Task<bool> TodoListExists(long todoListId);
    Task<IList<TodoItemResponse>?> GetTodoItems(long todoListId);
    Task<TodoItemResponse?> GetTodoItemById(long todoListId, long id);
    Task<TodoItemResponse?> UpdateTodoItem(long todoListId, long id, UpdateTodoItem payload);
    Task<TodoItemResponse?> CreateTodoItem(long todoListId, CreateTodoItem payload);
    Task<bool> DeleteTodoItem(long todoListId, long id);
	Task MarkAsPending(long todoItemId);
    Task ClearPendingFlag(long todoItemId);
    Task<IEnumerable<TodoItem>> GetOrphanedTodoItems(IEnumerable<string> externalItemIds);
}


