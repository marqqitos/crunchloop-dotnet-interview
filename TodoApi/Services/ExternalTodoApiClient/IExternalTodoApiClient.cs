using TodoApi.Dtos.External;

namespace TodoApi.Services.ExternalTodoApiClient;

public interface IExternalTodoApiClient
{
    /// <summary>
    /// Source ID used to identify this local system in external API
    /// </summary>
    string SourceId { get; }

    /// <summary>
    /// Get all TodoLists from external API
    /// </summary>
    Task<IEnumerable<ExternalTodoList>> GetTodoListsAsync();

	/// <summary>
	/// Get all TodoLists from external API that have pending sync with our local database
	/// </summary>
	Task<IEnumerable<ExternalTodoList>> GetTodoListsPendingSync();

    /// <summary>
    /// Create a new TodoList in external API
    /// </summary>
    Task<ExternalTodoList> CreateTodoListAsync(CreateExternalTodoList createDto);

    /// <summary>
    /// Update an existing TodoList in external API
    /// </summary>
    Task<ExternalTodoList> UpdateTodoListAsync(string id, UpdateExternalTodoList updateDto);

    /// <summary>
    /// Update a TodoItem in external API
    /// </summary>
    Task<ExternalTodoItem> UpdateTodoItemAsync(string todoListId, string itemId, UpdateExternalTodoItem updateDto);

    /// <summary>
    /// Delete a TodoList in external API
    /// </summary>
    Task DeleteTodoListAsync(string id);

    /// <summary>
    /// Delete a TodoItem in external API
    /// </summary>
    Task DeleteTodoItemAsync(string todoListId, string itemId);
}
