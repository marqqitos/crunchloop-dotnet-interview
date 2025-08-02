using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using Moq.Protected;
using TodoApi.Configuration;

namespace TodoApi.Tests.Services;

public class ExternalTodoApiClientTests : IDisposable
{
    private readonly Mock<HttpMessageHandler> _mockHttpMessageHandler;
    private readonly HttpClient _httpClient;
    private readonly Mock<ILogger<ExternalTodoApiClient>> _mockLogger;
    private readonly ExternalApiOptions _options;
    private readonly ExternalTodoApiClient _client;
    private readonly JsonSerializerOptions _jsonOptions;

    public ExternalTodoApiClientTests()
    {
        _mockHttpMessageHandler = new Mock<HttpMessageHandler>();
        _httpClient = new HttpClient(_mockHttpMessageHandler.Object)
        {
            BaseAddress = new Uri("http://localhost:8080")
        };

        _mockLogger = new Mock<ILogger<ExternalTodoApiClient>>();
        _options = new ExternalApiOptions
        {
            BaseUrl = "http://localhost:8080",
            TimeoutSeconds = 30,
            SourceId = "test-source-id"
        };

        var optionsMock = new Mock<IOptions<ExternalApiOptions>>();
        optionsMock.Setup(x => x.Value).Returns(_options);

        _client = new ExternalTodoApiClient(_httpClient, _mockLogger.Object, optionsMock.Object);

        _jsonOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = true
        };
    }

    [Fact]
    public void SourceId_ShouldReturnConfiguredValue()
    {
        // Act & Assert
        Assert.Equal("test-source-id", _client.SourceId);
    }

    [Fact]
    public async Task GetTodoListsAsync_WithSuccessfulResponse_ShouldReturnTodoLists()
    {
        // Arrange
        var expectedTodoLists = new List<ExternalTodoList>
        {
            new() { Id = "1", Name = "List 1", Items = new List<ExternalTodoItem>() },
            new() { Id = "2", Name = "List 2", Items = new List<ExternalTodoItem>() }
        };
        var jsonResponse = JsonSerializer.Serialize(expectedTodoLists, _jsonOptions);

        SetupHttpResponse(HttpStatusCode.OK, jsonResponse);

        // Act
        var result = await _client.GetTodoListsAsync();

        // Assert
        Assert.Equal(2, result.Count);
        Assert.Equal("List 1", result[0].Name);
        Assert.Equal("List 2", result[1].Name);

        VerifyHttpRequest(HttpMethod.Get, "/todolists");
        VerifyLogCalled(LogLevel.Information, "Successfully fetched 2 TodoLists");
    }

    [Fact]
    public async Task GetTodoListsAsync_WithEmptyResponse_ShouldReturnEmptyList()
    {
        // Arrange
        SetupHttpResponse(HttpStatusCode.OK, "[]");

        // Act
        var result = await _client.GetTodoListsAsync();

        // Assert
        Assert.Empty(result);
        VerifyLogCalled(LogLevel.Information, "Successfully fetched 0 TodoLists");
    }

    [Fact]
    public async Task GetTodoListsAsync_WithHttpError_ShouldThrowAndLog()
    {
        // Arrange
        SetupHttpResponse(HttpStatusCode.InternalServerError, "Internal Server Error");

        // Act & Assert
        var exception = await Assert.ThrowsAsync<HttpRequestException>(() => _client.GetTodoListsAsync());
        
        VerifyLogCalled(LogLevel.Error, "Failed to fetch TodoLists from external API");
    }

    [Fact]
    public async Task CreateTodoListAsync_WithValidData_ShouldReturnCreatedTodoList()
    {
        // Arrange
        var createDto = new CreateExternalTodoList
        {
            Name = "New List",
            SourceId = "test-source-id",
            Items = new List<CreateExternalTodoItem>
            {
                new() { Description = "Task 1", Completed = false, SourceId = "test-source-id" }
            }
        };

        var expectedResponse = new ExternalTodoList
        {
            Id = "new-list-id",
            Name = "New List",
            SourceId = "test-source-id",
            Items = new List<ExternalTodoItem>
            {
                new() { Id = "item-id", Description = "Task 1", Completed = false }
            }
        };

        var jsonResponse = JsonSerializer.Serialize(expectedResponse, _jsonOptions);
        SetupHttpResponse(HttpStatusCode.Created, jsonResponse);

        // Act
        var result = await _client.CreateTodoListAsync(createDto);

        // Assert
        Assert.Equal("new-list-id", result.Id);
        Assert.Equal("New List", result.Name);
        Assert.Single(result.Items);

        VerifyHttpRequest(HttpMethod.Post, "/todolists");
        VerifyLogCalled(LogLevel.Information, "Successfully created TodoList with external ID 'new-list-id'");
    }

    [Fact]
    public async Task CreateTodoListAsync_WithInvalidResponse_ShouldThrowException()
    {
        // Arrange
        var createDto = new CreateExternalTodoList { Name = "Test", SourceId = "test" };
        SetupHttpResponse(HttpStatusCode.Created, "invalid json");

        // Act & Assert
        await Assert.ThrowsAsync<JsonException>(() => _client.CreateTodoListAsync(createDto));
        VerifyLogCalled(LogLevel.Error, "Failed to create TodoList 'Test' in external API");
    }

    [Fact]
    public async Task UpdateTodoListAsync_WithValidData_ShouldReturnUpdatedTodoList()
    {
        // Arrange
        var externalId = "list-123";
        var updateDto = new UpdateExternalTodoList { Name = "Updated Name" };
        var expectedResponse = new ExternalTodoList
        {
            Id = externalId,
            Name = "Updated Name",
            Items = new List<ExternalTodoItem>()
        };

        var jsonResponse = JsonSerializer.Serialize(expectedResponse, _jsonOptions);
        SetupHttpResponse(HttpStatusCode.OK, jsonResponse);

        // Act
        var result = await _client.UpdateTodoListAsync(externalId, updateDto);

        // Assert
        Assert.Equal("Updated Name", result.Name);
        Assert.Equal(externalId, result.Id);

        VerifyHttpRequest(HttpMethod.Patch, $"/todolists/{externalId}");
        VerifyLogCalled(LogLevel.Information, $"Successfully updated TodoList '{externalId}'");
    }

    [Fact]
    public async Task DeleteTodoListAsync_WithValidId_ShouldCompleteSuccessfully()
    {
        // Arrange
        var externalId = "list-to-delete";
        SetupHttpResponse(HttpStatusCode.NoContent, "");

        // Act
        await _client.DeleteTodoListAsync(externalId);

        // Assert
        VerifyHttpRequest(HttpMethod.Delete, $"/todolists/{externalId}");
        VerifyLogCalled(LogLevel.Information, $"Successfully deleted TodoList '{externalId}'");
    }

    [Fact]
    public async Task DeleteTodoListAsync_WithNotFound_ShouldThrowAndLog()
    {
        // Arrange
        var externalId = "non-existent-list";
        SetupHttpResponse(HttpStatusCode.NotFound, "Not Found");

        // Act & Assert
        await Assert.ThrowsAsync<HttpRequestException>(() => _client.DeleteTodoListAsync(externalId));
        VerifyLogCalled(LogLevel.Error, $"Failed to delete TodoList '{externalId}' from external API");
    }

    [Fact]
    public async Task UpdateTodoItemAsync_WithValidData_ShouldReturnUpdatedItem()
    {
        // Arrange
        var todoListId = "list-123";
        var todoItemId = "item-456";
        var updateDto = new UpdateExternalTodoItem { Description = "Updated Task", Completed = true };
        var expectedResponse = new ExternalTodoItem
        {
            Id = todoItemId,
            Description = "Updated Task",
            Completed = true
        };

        var jsonResponse = JsonSerializer.Serialize(expectedResponse, _jsonOptions);
        SetupHttpResponse(HttpStatusCode.OK, jsonResponse);

        // Act
        var result = await _client.UpdateTodoItemAsync(todoListId, todoItemId, updateDto);

        // Assert
        Assert.Equal("Updated Task", result.Description);
        Assert.True(result.Completed);
        Assert.Equal(todoItemId, result.Id);

        VerifyHttpRequest(HttpMethod.Patch, $"/todolists/{todoListId}/todoitems/{todoItemId}");
        VerifyLogCalled(LogLevel.Information, $"Successfully updated TodoItem '{todoItemId}'");
    }

    [Fact]
    public async Task DeleteTodoItemAsync_WithValidIds_ShouldCompleteSuccessfully()
    {
        // Arrange
        var todoListId = "list-123";
        var todoItemId = "item-456";
        SetupHttpResponse(HttpStatusCode.NoContent, "");

        // Act
        await _client.DeleteTodoItemAsync(todoListId, todoItemId);

        // Assert
        VerifyHttpRequest(HttpMethod.Delete, $"/todolists/{todoListId}/todoitems/{todoItemId}");
        VerifyLogCalled(LogLevel.Information, $"Successfully deleted TodoItem '{todoItemId}'");
    }

    [Fact]
    public async Task CreateTodoListAsync_WithNullResponse_ShouldThrowInvalidOperationException()
    {
        // Arrange
        var createDto = new CreateExternalTodoList { Name = "Test", SourceId = "test" };
        SetupHttpResponse(HttpStatusCode.Created, "null");

        // Act & Assert
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => _client.CreateTodoListAsync(createDto));
        Assert.Contains("Failed to deserialize created TodoList", exception.Message);
    }

    [Fact]
    public async Task UpdateTodoItemAsync_WithNullResponse_ShouldThrowInvalidOperationException()
    {
        // Arrange
        var updateDto = new UpdateExternalTodoItem { Description = "Test" };
        SetupHttpResponse(HttpStatusCode.OK, "null");

        // Act & Assert
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => _client.UpdateTodoItemAsync("list-1", "item-1", updateDto));
        Assert.Contains("Failed to deserialize updated TodoItem", exception.Message);
    }

    [Theory]
    [InlineData(HttpStatusCode.BadRequest)]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    public async Task CreateTodoListAsync_WithHttpErrorStatusCodes_ShouldThrowHttpRequestException(HttpStatusCode statusCode)
    {
        // Arrange
        var createDto = new CreateExternalTodoList { Name = "Test", SourceId = "test" };
        SetupHttpResponse(statusCode, "Error occurred");

        // Act & Assert
        await Assert.ThrowsAsync<HttpRequestException>(() => _client.CreateTodoListAsync(createDto));
        VerifyLogCalled(LogLevel.Error, "Failed to create TodoList 'Test' in external API");
    }

    private void SetupHttpResponse(HttpStatusCode statusCode, string content)
    {
        var response = new HttpResponseMessage(statusCode)
        {
            Content = new StringContent(content, Encoding.UTF8, "application/json")
        };

        _mockHttpMessageHandler
            .Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(response);
    }

    private void VerifyHttpRequest(HttpMethod method, string expectedUri)
    {
        _mockHttpMessageHandler
            .Protected()
            .Verify(
                "SendAsync",
                Times.Once(),
                ItExpr.Is<HttpRequestMessage>(req =>
                    req.Method == method &&
                    req.RequestUri!.ToString().EndsWith(expectedUri)),
                ItExpr.IsAny<CancellationToken>());
    }

    private void VerifyLogCalled(LogLevel level, string message)
    {
        _mockLogger.Verify(
            x => x.Log(
                level,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains(message)),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.AtLeastOnce);
    }

    public void Dispose()
    {
        _httpClient?.Dispose();
    }
}