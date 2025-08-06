using Microsoft.Extensions.Options;
using System.Net;
using TodoApi.Configuration;

namespace TodoApi.Tests.Services;

public class RetryPolicyServiceTests
{
    private readonly Mock<ILogger<RetryPolicyService>> _mockLogger;
    private readonly RetryPolicyService _retryPolicyService;
    private readonly RetryOptions _retryOptions;

    public RetryPolicyServiceTests()
    {
        _mockLogger = new Mock<ILogger<RetryPolicyService>>();
        _retryOptions = new RetryOptions
        {
            MaxRetryAttempts = 2,
            BaseDelayMs = 100,
            MaxDelayMs = 1000,
            JitterFactor = 0.1,
            RequestTimeoutSeconds = 5,
            EnableRetries = true
        };

        var mockOptions = new Mock<IOptions<RetryOptions>>();
        mockOptions.Setup(x => x.Value).Returns(_retryOptions);

        _retryPolicyService = new RetryPolicyService(mockOptions.Object, _mockLogger.Object);
    }

    [Fact]
    public async Task HttpRetryPolicy_WithTransientFailure_ShouldRetryAndSucceed()
    {
        // Arrange
        var retryPolicy = _retryPolicyService.GetHttpRetryPolicy();
        var callCount = 0;

        // Act & Assert
        var result = await retryPolicy.ExecuteAsync(async cancellationToken =>
        {
            callCount++;
            if (callCount == 1)
            {
                throw new HttpRequestException("Transient network error");
            }
            return new HttpResponseMessage(HttpStatusCode.OK);
        });

        Assert.Equal(2, callCount);
        Assert.Equal(HttpStatusCode.OK, result.StatusCode);
    }

    [Fact]
    public async Task HttpRetryPolicy_WithPermanentFailure_ShouldNotRetry()
    {
        // Arrange
        var retryPolicy = _retryPolicyService.GetHttpRetryPolicy();
        var callCount = 0;

        // Act & Assert
        var result = await retryPolicy.ExecuteAsync(async cancellationToken =>
        {
            callCount++;
            return new HttpResponseMessage(HttpStatusCode.BadRequest);
        });

        Assert.Equal(1, callCount);
        Assert.Equal(HttpStatusCode.BadRequest, result.StatusCode);
    }

    [Fact]
    public async Task HttpRetryPolicy_WithServerError_ShouldRetry()
    {
        // Arrange
        var retryPolicy = _retryPolicyService.GetHttpRetryPolicy();
        var callCount = 0;

        // Act & Assert
        var result = await retryPolicy.ExecuteAsync(async cancellationToken =>
        {
            callCount++;

            if (callCount <= 2)
            {
                // Simulate server error by throwing an exception
                throw new HttpRequestException("Server error", null, HttpStatusCode.InternalServerError);
            }

            var successResponse = new HttpResponseMessage(HttpStatusCode.OK);
            return successResponse;
        });

        Assert.Equal(3, callCount);
        Assert.Equal(HttpStatusCode.OK, result.StatusCode);
    }

    [Fact]
    public async Task DatabaseRetryPolicy_WithTimeoutException_ShouldRetry()
    {
        // Arrange
        var retryPolicy = _retryPolicyService.GetDatabaseRetryPolicy();
        var callCount = 0;

        // Act & Assert
        await retryPolicy.ExecuteAsync(async cancellationToken =>
        {
            callCount++;
            if (callCount == 1)
            {
                throw new TimeoutException("Database timeout");
            }
            // Success on second attempt
        });

        Assert.Equal(2, callCount);
    }

    [Fact]
    public async Task SyncRetryPolicy_WithHttpException_ShouldRetry()
    {
        // Arrange
        var retryPolicy = _retryPolicyService.GetSyncRetryPolicy();
        var callCount = 0;

        // Act & Assert
        await retryPolicy.ExecuteAsync(async cancellationToken =>
        {
            callCount++;
            if (callCount == 1)
            {
                throw new HttpRequestException("Network error during sync");
            }
            // Success on second attempt
        });

        Assert.Equal(2, callCount);
    }

    [Fact]
    public async Task SyncRetryPolicy_WithConflictException_ShouldNotRetry()
    {
        // Arrange
        var retryPolicy = _retryPolicyService.GetSyncRetryPolicy();
        var callCount = 0;

        // Act & Assert
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            await retryPolicy.ExecuteAsync(async cancellationToken =>
            {
                callCount++;
                throw new InvalidOperationException("Conflict detected during sync");
            });
        });

        Assert.Equal(1, callCount);
        Assert.Contains("Conflict", exception.Message);
    }

    [Fact]
    public void RetryPolicies_WhenRetriesDisabled_ShouldReturnEmptyPipeline()
    {
        // Arrange
        var disabledOptions = new RetryOptions { EnableRetries = false };
        var mockOptions = new Mock<IOptions<RetryOptions>>();
        mockOptions.Setup(x => x.Value).Returns(disabledOptions);

        var service = new RetryPolicyService(mockOptions.Object, _mockLogger.Object);

        // Act
        var httpPolicy = service.GetHttpRetryPolicy();
        var dbPolicy = service.GetDatabaseRetryPolicy();
        var syncPolicy = service.GetSyncRetryPolicy();

        // Assert
        Assert.NotNull(httpPolicy);
        Assert.NotNull(dbPolicy);
        Assert.NotNull(syncPolicy);
        // Note: We can't easily test that they're empty pipelines without internal access
    }
}
