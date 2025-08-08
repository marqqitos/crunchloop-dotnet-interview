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
    private readonly IRetryPolicyService _retryPolicyService;

    public ExternalTodoApiClient(
        HttpClient httpClient, 
        ILogger<ExternalTodoApiClient> logger,
        IOptions<ExternalApiOptions> options,
        IRetryPolicyService retryPolicyService)
    {
        _httpClient = httpClient;
        _logger = logger;
        _options = options.Value;
        _retryPolicyService = retryPolicyService;
        _jsonOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = true
        };
    }

    public string SourceId => _options.SourceId;

    public async Task<List<ExternalTodoList>> GetTodoListsAsync()
    {
        _logger.LogInformation("Fetching TodoLists from external API");
        
        var retryPolicy = _retryPolicyService.GetHttpRetryPolicy();
        
        return await retryPolicy.ExecuteAsync(async cancellationToken =>
        {
            var response = await _httpClient.GetAsync("/todolists", cancellationToken);
            response.EnsureSuccessStatusCode();
            
            var json = await response.Content.ReadAsStringAsync(cancellationToken);
            var todoLists = JsonSerializer.Deserialize<List<ExternalTodoList>>(json, _jsonOptions);
            
            _logger.LogInformation("Successfully fetched {Count} TodoLists from external API", todoLists?.Count ?? 0);
            return todoLists ?? new List<ExternalTodoList>();
        });
    }

    public async Task<ExternalTodoList> CreateTodoListAsync(CreateExternalTodoList createDto)
    {
        _logger.LogInformation("Creating TodoList '{Name}' in external API", createDto.Name);
        
        var retryPolicy = _retryPolicyService.GetHttpRetryPolicy();
        
        return await retryPolicy.ExecuteAsync(async cancellationToken =>
        {
            var json = JsonSerializer.Serialize(createDto, _jsonOptions);
            var content = new StringContent(json, Encoding.UTF8, "application/json");
            
            var response = await _httpClient.PostAsync("/todolists", content, cancellationToken);
            response.EnsureSuccessStatusCode();
            
            var responseJson = await response.Content.ReadAsStringAsync(cancellationToken);
            var createdTodoList = JsonSerializer.Deserialize<ExternalTodoList>(responseJson, _jsonOptions);
            
            _logger.LogInformation("Successfully created TodoList with external ID '{ExternalId}'", createdTodoList?.Id);
            return createdTodoList ?? throw new InvalidOperationException("Failed to deserialize created TodoList");
        });
    }

    public async Task<ExternalTodoList> UpdateTodoListAsync(string externalId, UpdateExternalTodoList updateDto)
    {
        _logger.LogInformation("Updating TodoList '{ExternalId}' in external API", externalId);
        
        var retryPolicy = _retryPolicyService.GetHttpRetryPolicy();
        
        return await retryPolicy.ExecuteAsync(async cancellationToken =>
        {
            var json = JsonSerializer.Serialize(updateDto, _jsonOptions);
            var content = new StringContent(json, Encoding.UTF8, "application/json");
            
            var response = await _httpClient.PatchAsync($"/todolists/{externalId}", content, cancellationToken);
            response.EnsureSuccessStatusCode();
            
            var responseJson = await response.Content.ReadAsStringAsync(cancellationToken);
            var updatedTodoList = JsonSerializer.Deserialize<ExternalTodoList>(responseJson, _jsonOptions);
            
            _logger.LogInformation("Successfully updated TodoList '{ExternalId}'", externalId);
            return updatedTodoList ?? throw new InvalidOperationException("Failed to deserialize updated TodoList");
        });
    }

    public async Task DeleteTodoListAsync(string externalId)
    {
        _logger.LogInformation("Deleting TodoList '{ExternalId}' from external API", externalId);
        
        var retryPolicy = _retryPolicyService.GetHttpRetryPolicy();
        
        await retryPolicy.ExecuteAsync(async cancellationToken =>
        {
            var response = await _httpClient.DeleteAsync($"/todolists/{externalId}", cancellationToken);
            response.EnsureSuccessStatusCode();
            
            _logger.LogInformation("Successfully deleted TodoList '{ExternalId}'", externalId);
        });
    }

    public async Task<ExternalTodoItem> UpdateTodoItemAsync(string todoListId, string todoItemId, UpdateExternalTodoItem updateDto)
    {
        _logger.LogInformation("Updating TodoItem '{TodoItemId}' in TodoList '{TodoListId}' via external API", todoItemId, todoListId);
        
        var retryPolicy = _retryPolicyService.GetHttpRetryPolicy();
        
        return await retryPolicy.ExecuteAsync(async cancellationToken =>
        {
            var json = JsonSerializer.Serialize(updateDto, _jsonOptions);
            var content = new StringContent(json, Encoding.UTF8, "application/json");
            
            var response = await _httpClient.PatchAsync($"/todolists/{todoListId}/todoitems/{todoItemId}", content, cancellationToken);
            response.EnsureSuccessStatusCode();
            
            var responseJson = await response.Content.ReadAsStringAsync(cancellationToken);
            var updatedTodoItem = JsonSerializer.Deserialize<ExternalTodoItem>(responseJson, _jsonOptions);
            
            _logger.LogInformation("Successfully updated TodoItem '{TodoItemId}'", todoItemId);
            return updatedTodoItem ?? throw new InvalidOperationException("Failed to deserialize updated TodoItem");
        });
    }

    public async Task DeleteTodoItemAsync(string todoListId, string todoItemId)
    {
        _logger.LogInformation("Deleting TodoItem '{TodoItemId}' from TodoList '{TodoListId}' via external API", todoItemId, todoListId);
        
        var retryPolicy = _retryPolicyService.GetHttpRetryPolicy();
        
        await retryPolicy.ExecuteAsync(async cancellationToken =>
        {
            var response = await _httpClient.DeleteAsync($"/todolists/{todoListId}/todoitems/{todoItemId}", cancellationToken);
            response.EnsureSuccessStatusCode();
            
            _logger.LogInformation("Successfully deleted TodoItem '{TodoItemId}'", todoItemId);
        });
    }
}