using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using Moq.Protected;
using TodoApi.Configuration;
using TodoApi.Services.ExternalTodoApiClient;
using TodoApi.Services.RetryPolicyService;
using TodoApi.Services.SyncStateService;
using TodoApi.Tests.Builders;

namespace TodoApi.Tests.Services.ExternalTodoApiClientTests;

public class ExternalTodoApiClientTests : IDisposable
{
    private readonly Mock<HttpMessageHandler> _mockHttpMessageHandler;
    private readonly HttpClient _httpClient;
    private readonly Mock<ILogger<ExternalTodoApiClient>> _mockLogger;
    private readonly Mock<IRetryPolicyService> _mockRetryPolicyService;
	private readonly Mock<ISyncStateService> _mockSyncStateService;
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
        _mockRetryPolicyService = new Mock<IRetryPolicyService>();
		_mockSyncStateService = new Mock<ISyncStateService>();

		_options = new ExternalApiOptions
        {
            BaseUrl = "http://localhost:8080",
            TimeoutSeconds = 30,
            SourceId = "test-source-id"
        };

        var optionsMock = new Mock<IOptions<ExternalApiOptions>>();
        optionsMock.Setup(x => x.Value).Returns(_options);

        // Setup retry policy mocks to return empty pipelines for tests
        _mockRetryPolicyService.Setup(x => x.GetHttpRetryPolicy()).Returns(Polly.ResiliencePipeline.Empty);

        _client = new ExternalTodoApiClient(
			_httpClient,
			_mockLogger.Object,
			optionsMock.Object,
			_mockRetryPolicyService.Object,
			_mockSyncStateService.Object);

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
        var result = (await _client.GetTodoLists()).ToList();

        // Assert
        Assert.Equal(2, result.Count);
        Assert.Equal("List 1", result[0].Name);
        Assert.Equal("List 2", result[1].Name);

        VerifyHttpRequest(HttpMethod.Get, "/todolists");
    }

    [Fact]
    public async Task GetTodoListsAsync_WithEmptyResponse_ShouldReturnEmptyList()
    {
        // Arrange
        SetupHttpResponse(HttpStatusCode.OK, "[]");

        // Act
        var result = await _client.GetTodoLists();

        // Assert
        Assert.Empty(result);
    }

    [Fact]
    public async Task GetTodoListsAsync_WithHttpError_ShouldThrowAndLog()
    {
        // Arrange
        SetupHttpResponse(HttpStatusCode.InternalServerError, "Internal Server Error");

        // Act & Assert
        var exception = await Assert.ThrowsAsync<HttpRequestException>(() => _client.GetTodoLists());
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
        var result = await _client.CreateTodoList(createDto);

        // Assert
        Assert.Equal("new-list-id", result.Id);
        Assert.Equal("New List", result.Name);
        Assert.Single(result.Items);

        VerifyHttpRequest(HttpMethod.Post, "/todolists");
    }

    [Fact]
    public async Task CreateTodoListAsync_WithInvalidResponse_ShouldThrowException()
    {
        // Arrange
        var createDto = new CreateExternalTodoList { Name = "Test", SourceId = "test" };
        SetupHttpResponse(HttpStatusCode.Created, "invalid json");

        // Act & Assert
        await Assert.ThrowsAsync<JsonException>(() => _client.CreateTodoList(createDto));
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
        var result = await _client.UpdateTodoList(externalId, updateDto);

        // Assert
        Assert.Equal("Updated Name", result.Name);
        Assert.Equal(externalId, result.Id);

        VerifyHttpRequest(HttpMethod.Patch, $"/todolists/{externalId}");
    }

    [Fact]
    public async Task DeleteTodoListAsync_WithValidId_ShouldCompleteSuccessfully()
    {
        // Arrange
        var externalId = "list-to-delete";
        SetupHttpResponse(HttpStatusCode.NoContent, "");

        // Act
        await _client.DeleteTodoList(externalId);

        // Assert
        VerifyHttpRequest(HttpMethod.Delete, $"/todolists/{externalId}");
    }

    [Fact]
    public async Task DeleteTodoListAsync_WithNotFound_ShouldThrowAndLog()
    {
        // Arrange
        var externalId = "non-existent-list";
        SetupHttpResponse(HttpStatusCode.NotFound, "Not Found");

        // Act & Assert
        await Assert.ThrowsAsync<HttpRequestException>(() => _client.DeleteTodoList(externalId));
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
        var result = await _client.UpdateTodoItem(todoListId, todoItemId, updateDto);

        // Assert
        Assert.Equal("Updated Task", result.Description);
        Assert.True(result.Completed);
        Assert.Equal(todoItemId, result.Id);

        VerifyHttpRequest(HttpMethod.Patch, $"/todolists/{todoListId}/todoitems/{todoItemId}");
    }

    [Fact]
    public async Task DeleteTodoItemAsync_WithValidIds_ShouldCompleteSuccessfully()
    {
        // Arrange
        var todoListId = "list-123";
        var todoItemId = "item-456";
        SetupHttpResponse(HttpStatusCode.NoContent, "");

        // Act
        await _client.DeleteTodoItem(todoListId, todoItemId);

        // Assert
        VerifyHttpRequest(HttpMethod.Delete, $"/todolists/{todoListId}/todoitems/{todoItemId}");
    }

    [Fact]
    public async Task CreateTodoListAsync_WithNullResponse_ShouldThrowInvalidOperationException()
    {
        // Arrange
        var createDto = new CreateExternalTodoList { Name = "Test", SourceId = "test" };
        SetupHttpResponse(HttpStatusCode.Created, "null");

        // Act & Assert
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => _client.CreateTodoList(createDto));
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
            () => _client.UpdateTodoItem("list-1", "item-1", updateDto));
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
        await Assert.ThrowsAsync<HttpRequestException>(() => _client.CreateTodoList(createDto));
    }

    [Fact]
    public async Task GetTodoListsPendingSync_WhenDeltaSyncNotAvailable_ShouldReturnAllTodoLists()
    {
        // Arrange
		var externalTodoList1 = ExternalTodoListBuilder.Create()
			.WithId("1")
			.WithName("List 1")
			.WithUpdatedAt(DateTime.UtcNow.AddHours(-2))
			.Build();
		var externalTodoList2 = ExternalTodoListBuilder.Create()
			.WithId("2")
			.WithName("List 2")
			.WithUpdatedAt(DateTime.UtcNow.AddHours(-1))
			.Build();

		var expectedTodoLists = new List<ExternalTodoList> { externalTodoList1, externalTodoList2 };

		var jsonResponse = JsonSerializer.Serialize(expectedTodoLists, _jsonOptions);

        SetupHttpResponse(HttpStatusCode.OK, jsonResponse);
        _mockSyncStateService.Setup(x => x.IsDeltaSyncAvailable()).ReturnsAsync(false);

        // Act
        var result = (await _client.GetTodoListsPendingSync()).ToList();

        // Assert
        Assert.Equal(2, result.Count);
        Assert.Equal("List 1", result[0].Name);
        Assert.Equal("List 2", result[1].Name);

        VerifyHttpRequest(HttpMethod.Get, "/todolists");
        _mockSyncStateService.Verify(x => x.IsDeltaSyncAvailable(), Times.Once);
        _mockSyncStateService.Verify(x => x.GetLastSyncTimestamp(), Times.Never);
    }

    [Fact]
    public async Task GetTodoListsPendingSync_WhenDeltaSyncAvailable_ShouldFilterByLastSyncTimestamp()
    {
        // Arrange
        var lastSyncTimestamp = DateTime.UtcNow.AddHours(-2);

		var externalTodoList1 = ExternalTodoListBuilder.Create()
			.WithId("1")
			.WithName("Old List")
			.WithUpdatedAt(DateTime.UtcNow.AddHours(-3))
			.Build();
		var externalTodoList2 = ExternalTodoListBuilder.Create()
			.WithId("2")
			.WithName("Recent List")
			.WithUpdatedAt(DateTime.UtcNow.AddHours(-1))
			.Build();
		var externalTodoList3 = ExternalTodoListBuilder.Create()
			.WithId("3")
			.WithName("New List")
			.WithUpdatedAt(DateTime.UtcNow)
			.Build();

		var allTodoLists = new List<ExternalTodoList> { externalTodoList1, externalTodoList2, externalTodoList3 };

        var jsonResponse = JsonSerializer.Serialize(allTodoLists, _jsonOptions);

        SetupHttpResponse(HttpStatusCode.OK, jsonResponse);
        _mockSyncStateService.Setup(x => x.IsDeltaSyncAvailable()).ReturnsAsync(true);
        _mockSyncStateService.Setup(x => x.GetLastSyncTimestamp()).ReturnsAsync(lastSyncTimestamp);

        // Act
        var result = (await _client.GetTodoListsPendingSync()).ToList();

        // Assert
        Assert.Equal(2, result.Count);
        Assert.Equal("Recent List", result[0].Name);
        Assert.Equal("New List", result[1].Name);
        Assert.All(result, tl => Assert.True(tl.UpdatedAt >= lastSyncTimestamp));

        VerifyHttpRequest(HttpMethod.Get, "/todolists");
        _mockSyncStateService.Verify(x => x.IsDeltaSyncAvailable(), Times.Once);
        _mockSyncStateService.Verify(x => x.GetLastSyncTimestamp(), Times.Once);
    }

    [Fact]
    public async Task GetTodoListsPendingSync_WhenDeltaSyncAvailableButNoLastSyncTimestamp_ShouldReturnAllTodoLists()
    {
        // Arrange
		var externalTodoList1 = ExternalTodoListBuilder.Create()
			.WithId("1")
			.WithName("List 1")
			.WithUpdatedAt(DateTime.UtcNow.AddHours(-2))
			.Build();
		var externalTodoList2 = ExternalTodoListBuilder.Create()
			.WithId("2")
			.WithName("List 2")
			.WithUpdatedAt(DateTime.UtcNow.AddHours(-1))
			.Build();

		var expectedTodoLists = new List<ExternalTodoList> { externalTodoList1, externalTodoList2 };

        var jsonResponse = JsonSerializer.Serialize(expectedTodoLists, _jsonOptions);

        SetupHttpResponse(HttpStatusCode.OK, jsonResponse);
        _mockSyncStateService.Setup(x => x.IsDeltaSyncAvailable()).ReturnsAsync(true);
        _mockSyncStateService.Setup(x => x.GetLastSyncTimestamp()).ReturnsAsync((DateTime?)null);

        // Act
        var result = (await _client.GetTodoListsPendingSync()).ToList();

        // Assert
        Assert.Equal(2, result.Count);
        Assert.Equal("List 1", result[0].Name);
        Assert.Equal("List 2", result[1].Name);

        VerifyHttpRequest(HttpMethod.Get, "/todolists");
        _mockSyncStateService.Verify(x => x.IsDeltaSyncAvailable(), Times.Once);
        _mockSyncStateService.Verify(x => x.GetLastSyncTimestamp(), Times.Once);
    }

    [Fact]
    public async Task GetTodoListsPendingSync_WhenDeltaSyncAvailableAndEmptyResponse_ShouldReturnEmptyList()
    {
        // Arrange
        SetupHttpResponse(HttpStatusCode.OK, "[]");
        _mockSyncStateService.Setup(x => x.IsDeltaSyncAvailable()).ReturnsAsync(true);
        _mockSyncStateService.Setup(x => x.GetLastSyncTimestamp()).ReturnsAsync(DateTime.UtcNow.AddHours(-1));

        // Act
        var result = (await _client.GetTodoListsPendingSync()).ToList();

        // Assert
        Assert.Empty(result);

        VerifyHttpRequest(HttpMethod.Get, "/todolists");
        _mockSyncStateService.Verify(x => x.IsDeltaSyncAvailable(), Times.Once);
        _mockSyncStateService.Verify(x => x.GetLastSyncTimestamp(), Times.Once);
    }

    [Fact]
    public async Task GetTodoListsPendingSync_WhenDeltaSyncAvailableAndHttpError_ShouldThrowAndLog()
    {
        // Arrange
        SetupHttpResponse(HttpStatusCode.InternalServerError, "Internal Server Error");
        _mockSyncStateService.Setup(x => x.IsDeltaSyncAvailable()).ReturnsAsync(true);
        _mockSyncStateService.Setup(x => x.GetLastSyncTimestamp()).ReturnsAsync(DateTime.UtcNow.AddHours(-1));

        // Act & Assert
        var exception = await Assert.ThrowsAsync<HttpRequestException>(() => _client.GetTodoListsPendingSync());

        VerifyHttpRequest(HttpMethod.Get, "/todolists");
        _mockSyncStateService.Verify(x => x.IsDeltaSyncAvailable(), Times.Once);
        _mockSyncStateService.Verify(x => x.GetLastSyncTimestamp(), Times.Once);
    }

    [Fact]
    public async Task GetTodoListsPendingSync_WhenDeltaSyncAvailableAndFiltering_ShouldLogDeltaSyncInfo()
    {
        // Arrange
        var lastSyncTimestamp = DateTime.UtcNow.AddHours(-1);
		var externalTodoList1 = ExternalTodoListBuilder.Create()
			.WithId("1")
			.WithName("Recent List")
			.WithUpdatedAt(DateTime.UtcNow)
			.Build();
		var allTodoLists = new List<ExternalTodoList> { externalTodoList1 };

        var jsonResponse = JsonSerializer.Serialize(allTodoLists, _jsonOptions);

        SetupHttpResponse(HttpStatusCode.OK, jsonResponse);
        _mockSyncStateService.Setup(x => x.IsDeltaSyncAvailable()).ReturnsAsync(true);
        _mockSyncStateService.Setup(x => x.GetLastSyncTimestamp()).ReturnsAsync(lastSyncTimestamp);

        // Act
        var result = (await _client.GetTodoListsPendingSync()).ToList();

        // Assert
        Assert.Single(result);
        Assert.Equal("Recent List", result.First().Name);

        VerifyHttpRequest(HttpMethod.Get, "/todolists");
        _mockSyncStateService.Verify(x => x.IsDeltaSyncAvailable(), Times.Once);
        _mockSyncStateService.Verify(x => x.GetLastSyncTimestamp(), Times.Once);
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

    public void Dispose()
    {
        _httpClient?.Dispose();
    }
}
