using TodoApi.Dtos;
using TodoApi.Models;

namespace TodoApi.Services.TodoListService;

public interface ITodoListService
{
    Task<IList<TodoListResponse>> GetTodoLists();
    Task<TodoListResponse?> GetTodoListById(long id);
    Task<TodoListResponse?> UpdateTodoList(long id, UpdateTodoList payload);
    Task<TodoListResponse> CreateTodoList(CreateTodoList payload);
    Task<bool> DeleteTodoList(long id);
	Task MarkAsPending(long todoListId);
	Task ClearPendingFlag(long todoListId);
    Task<IEnumerable<TodoList>> GetTodoListsPending();
    Task<TodoList?> GetTodoListByExternalId(string externalId);
    Task<IEnumerable<TodoList>> GetOrphanedTodoLists(IEnumerable<string> externalListIds);
}


