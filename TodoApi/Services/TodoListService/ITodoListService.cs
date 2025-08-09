using TodoApi.Dtos;

namespace TodoApi.Services;

public interface ITodoListService
{
    Task<IList<TodoListResponse>> GetTodoListsAsync();
    Task<TodoListResponse?> GetTodoListAsync(long id);
    Task<TodoListResponse?> UpdateTodoListAsync(long id, UpdateTodoList payload);
    Task<TodoListResponse> CreateTodoListAsync(CreateTodoList payload);
    Task<bool> DeleteTodoListAsync(long id);
	Task MarkAsPendingAsync(long todoListId);
	Task ClearPendingFlagAsync(long todoListId);
	Task<int> GetPendingChangesCountAsync();
}


