using TodoApi.Dtos;

namespace TodoApi.Services;

public interface ITodoItemService
{
    Task<bool> TodoListExistsAsync(long todoListId);
    Task<IList<TodoItemResponse>?> GetTodoItemsAsync(long todoListId);
    Task<TodoItemResponse?> GetTodoItemAsync(long todoListId, long id);
    Task<TodoItemResponse?> UpdateTodoItemAsync(long todoListId, long id, UpdateTodoItem payload);
    Task<TodoItemResponse?> CreateTodoItemAsync(long todoListId, CreateTodoItem payload);
    Task<bool> DeleteTodoItemAsync(long todoListId, long id);
}


