using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using TodoApi.Configuration;
using TodoApi.Dtos.External;

namespace TodoApi.Services;

public class ExternalTodoApiClient : IExternalTodoApiClient
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<ExternalTodoApiClient> _logger;
    private readonly ExternalApiOptions _options;
    private readonly JsonSerializerOptions _jsonOptions;

    public ExternalTodoApiClient(
        HttpClient httpClient, 
        ILogger<ExternalTodoApiClient> logger,
        IOptions<ExternalApiOptions> options)
    {
        _httpClient = httpClient;
        _logger = logger;
        _options = options.Value;
        _jsonOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = true
        };
    }

    public string SourceId => _options.SourceId;

    public async Task<List<ExternalTodoList>> GetTodoListsAsync()
    {
        try
        {
            _logger.LogInformation("Fetching TodoLists from external API");
            
            var response = await _httpClient.GetAsync("/todolists");
            response.EnsureSuccessStatusCode();
            
            var json = await response.Content.ReadAsStringAsync();
            var todoLists = JsonSerializer.Deserialize<List<ExternalTodoList>>(json, _jsonOptions);
            
            _logger.LogInformation("Successfully fetched {Count} TodoLists from external API", todoLists?.Count ?? 0);
            return todoLists ?? new List<ExternalTodoList>();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to fetch TodoLists from external API");
            throw;
        }
    }

    public async Task<ExternalTodoList> CreateTodoListAsync(CreateExternalTodoList createDto)
    {
        try
        {
            _logger.LogInformation("Creating TodoList '{Name}' in external API", createDto.Name);
            
            var json = JsonSerializer.Serialize(createDto, _jsonOptions);
            var content = new StringContent(json, Encoding.UTF8, "application/json");
            
            var response = await _httpClient.PostAsync("/todolists", content);
            response.EnsureSuccessStatusCode();
            
            var responseJson = await response.Content.ReadAsStringAsync();
            var createdTodoList = JsonSerializer.Deserialize<ExternalTodoList>(responseJson, _jsonOptions);
            
            _logger.LogInformation("Successfully created TodoList with external ID '{ExternalId}'", createdTodoList?.Id);
            return createdTodoList ?? throw new InvalidOperationException("Failed to deserialize created TodoList");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create TodoList '{Name}' in external API", createDto.Name);
            throw;
        }
    }

    public async Task<ExternalTodoList> UpdateTodoListAsync(string externalId, UpdateExternalTodoList updateDto)
    {
        try
        {
            _logger.LogInformation("Updating TodoList '{ExternalId}' in external API", externalId);
            
            var json = JsonSerializer.Serialize(updateDto, _jsonOptions);
            var content = new StringContent(json, Encoding.UTF8, "application/json");
            
            var response = await _httpClient.PatchAsync($"/todolists/{externalId}", content);
            response.EnsureSuccessStatusCode();
            
            var responseJson = await response.Content.ReadAsStringAsync();
            var updatedTodoList = JsonSerializer.Deserialize<ExternalTodoList>(responseJson, _jsonOptions);
            
            _logger.LogInformation("Successfully updated TodoList '{ExternalId}'", externalId);
            return updatedTodoList ?? throw new InvalidOperationException("Failed to deserialize updated TodoList");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to update TodoList '{ExternalId}' in external API", externalId);
            throw;
        }
    }

    public async Task DeleteTodoListAsync(string externalId)
    {
        try
        {
            _logger.LogInformation("Deleting TodoList '{ExternalId}' from external API", externalId);
            
            var response = await _httpClient.DeleteAsync($"/todolists/{externalId}");
            response.EnsureSuccessStatusCode();
            
            _logger.LogInformation("Successfully deleted TodoList '{ExternalId}'", externalId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to delete TodoList '{ExternalId}' from external API", externalId);
            throw;
        }
    }

    public async Task<ExternalTodoItem> UpdateTodoItemAsync(string todoListId, string todoItemId, UpdateExternalTodoItem updateDto)
    {
        try
        {
            _logger.LogInformation("Updating TodoItem '{TodoItemId}' in TodoList '{TodoListId}' via external API", todoItemId, todoListId);
            
            var json = JsonSerializer.Serialize(updateDto, _jsonOptions);
            var content = new StringContent(json, Encoding.UTF8, "application/json");
            
            var response = await _httpClient.PatchAsync($"/todolists/{todoListId}/todoitems/{todoItemId}", content);
            response.EnsureSuccessStatusCode();
            
            var responseJson = await response.Content.ReadAsStringAsync();
            var updatedTodoItem = JsonSerializer.Deserialize<ExternalTodoItem>(responseJson, _jsonOptions);
            
            _logger.LogInformation("Successfully updated TodoItem '{TodoItemId}'", todoItemId);
            return updatedTodoItem ?? throw new InvalidOperationException("Failed to deserialize updated TodoItem");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to update TodoItem '{TodoItemId}' in external API", todoItemId);
            throw;
        }
    }

    public async Task DeleteTodoItemAsync(string todoListId, string todoItemId)
    {
        try
        {
            _logger.LogInformation("Deleting TodoItem '{TodoItemId}' from TodoList '{TodoListId}' via external API", todoItemId, todoListId);
            
            var response = await _httpClient.DeleteAsync($"/todolists/{todoListId}/todoitems/{todoItemId}");
            response.EnsureSuccessStatusCode();
            
            _logger.LogInformation("Successfully deleted TodoItem '{TodoItemId}'", todoItemId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to delete TodoItem '{TodoItemId}' from external API", todoItemId);
            throw;
        }
    }
}