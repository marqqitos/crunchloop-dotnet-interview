using TodoApi.Dtos;
using TodoApi.Dtos.External;
using TodoApi.Models;

namespace TodoApi.Services.TodoListService;

public interface ITodoListService
{
    Task<IList<TodoListResponse>> GetTodoListsAsync();
    Task<TodoListResponse?> GetTodoListAsync(long id);
    Task<TodoListResponse?> UpdateTodoListAsync(long id, UpdateTodoList payload);
    Task<TodoListResponse> CreateTodoListAsync(CreateTodoList payload);
    Task<bool> DeleteTodoListAsync(long id);
	Task MarkAsPendingAsync(long todoListId);
	Task ClearPendingFlagAsync(long todoListId);
    Task<IEnumerable<TodoList>> GetPendingSyncTodoLists();
    Task<bool> ExternalTodoListsMismatch(IEnumerable<ExternalTodoList> externalTodoLists);
    Task<TodoList?> GetTodoListByExternalIdAsync(string externalId);
    Task<IEnumerable<TodoList>> GetOrphanedTodoListsAsync(IEnumerable<string> externalListIds);
}


