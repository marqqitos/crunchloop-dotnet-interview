using Microsoft.Extensions.Options;
using Polly;
using Polly.Retry;
using Polly.CircuitBreaker;
using System.Net;
using TodoApi.Configuration;

namespace TodoApi.Services;

/// <summary>
/// Implementation of retry policy service using Polly
/// </summary>
public class RetryPolicyService : IRetryPolicyService
{
    private readonly RetryOptions _retryOptions;
    private readonly ILogger<RetryPolicyService> _logger;
    private readonly ResiliencePipeline _httpRetryPolicy;
    private readonly ResiliencePipeline _databaseRetryPolicy;
    private readonly ResiliencePipeline _syncRetryPolicy;

    public RetryPolicyService(IOptions<RetryOptions> retryOptions, ILogger<RetryPolicyService> logger)
    {
        _retryOptions = retryOptions.Value;
        _logger = logger;

        _httpRetryPolicy = CreateHttpRetryPolicy();
        _databaseRetryPolicy = CreateDatabaseRetryPolicy();
        _syncRetryPolicy = CreateSyncRetryPolicy();
    }

    public ResiliencePipeline GetHttpRetryPolicy() => _httpRetryPolicy;
    public ResiliencePipeline GetDatabaseRetryPolicy() => _databaseRetryPolicy;
    public ResiliencePipeline GetSyncRetryPolicy() => _syncRetryPolicy;

    private ResiliencePipeline CreateHttpRetryPolicy()
    {
        if (!_retryOptions.EnableRetries)
        {
            return ResiliencePipeline.Empty;
        }

        var builder = new ResiliencePipelineBuilder()
            .AddRetry(new RetryStrategyOptions
            {
                MaxRetryAttempts = _retryOptions.MaxRetryAttempts,
                BackoffType = DelayBackoffType.Exponential,
                Delay = TimeSpan.FromMilliseconds(_retryOptions.BaseDelayMs),
                MaxDelay = TimeSpan.FromMilliseconds(_retryOptions.MaxDelayMs),
                UseJitter = true,
                ShouldHandle = new PredicateBuilder()
                    .Handle<HttpRequestException>()
                    .Handle<TaskCanceledException>()
                    .HandleResult((HttpResponseMessage response) =>
                        !response.IsSuccessStatusCode &&
                        ShouldRetryHttpStatusCode(response.StatusCode)),
                OnRetry = args =>
                {
                    var exception = args.Outcome.Exception;
                    var result = args.Outcome.Result as HttpResponseMessage;

                    if (exception != null)
                    {
                        _logger.LogWarning("HTTP retry attempt {AttemptNumber} due to exception: {Exception}. " +
                            "Retrying in {Delay}ms",
                            args.AttemptNumber + 1, exception.Message, args.RetryDelay.TotalMilliseconds);
                    }
                    else if (result != null)
                    {
                        _logger.LogWarning("HTTP retry attempt {AttemptNumber} due to status code: {StatusCode}. " +
                            "Retrying in {Delay}ms",
                            args.AttemptNumber + 1, result.StatusCode, args.RetryDelay.TotalMilliseconds);
                    }

                    return ValueTask.CompletedTask;
                }
            });

        if (_retryOptions.EnableCircuitBreaker)
        {
            builder.AddCircuitBreaker(new CircuitBreakerStrategyOptions
            {
                FailureRatio = _retryOptions.CircuitBreakerFailureRatio,
                MinimumThroughput = _retryOptions.CircuitBreakerMinimumThroughput,
                SamplingDuration = TimeSpan.FromSeconds(_retryOptions.CircuitBreakerSamplingDurationSeconds),
                BreakDuration = TimeSpan.FromSeconds(_retryOptions.CircuitBreakerBreakDurationSeconds),
                ShouldHandle = new PredicateBuilder()
                    .Handle<HttpRequestException>()
                    .Handle<TaskCanceledException>()
                    .HandleResult((HttpResponseMessage response) => !response.IsSuccessStatusCode),
                OnOpened = args =>
                {
                    _logger.LogWarning("HTTP circuit breaker opened for {BreakDuration}s due to high failure rate.", args.BreakDuration.TotalSeconds);
                    return default;
                },
                OnClosed = _ =>
                {
                    _logger.LogInformation("HTTP circuit breaker closed. Traffic resumes.");
                    return default;
                },
                OnHalfOpened = _ =>
                {
                    _logger.LogInformation("HTTP circuit breaker half-open. Trial calls allowed.");
                    return default;
                }
            });
        }

        return builder
            .AddTimeout(TimeSpan.FromSeconds(_retryOptions.RequestTimeoutSeconds))
            .Build();
    }

    private ResiliencePipeline CreateDatabaseRetryPolicy()
    {
        if (!_retryOptions.EnableRetries)
        {
            return ResiliencePipeline.Empty;
        }

        return new ResiliencePipelineBuilder()
            .AddRetry(new RetryStrategyOptions
            {
                MaxRetryAttempts = 2, // Fewer retries for database operations
                BackoffType = DelayBackoffType.Exponential,
                Delay = TimeSpan.FromMilliseconds(500),
                MaxDelay = TimeSpan.FromMilliseconds(5000),
                UseJitter = true,
                ShouldHandle = new PredicateBuilder()
                    .Handle<InvalidOperationException>(ex =>
                        ex.Message.Contains("timeout", StringComparison.OrdinalIgnoreCase) ||
                        ex.Message.Contains("connection", StringComparison.OrdinalIgnoreCase))
                    .Handle<TimeoutException>(),
                OnRetry = args =>
                {
                    _logger.LogWarning("Database retry attempt {AttemptNumber} due to: {Exception}. " +
                        "Retrying in {Delay}ms",
                        args.AttemptNumber + 1, args.Outcome.Exception?.Message,
                        args.RetryDelay.TotalMilliseconds);
                    return ValueTask.CompletedTask;
                }
            })
            .Build();
    }

    private ResiliencePipeline CreateSyncRetryPolicy()
    {
        if (!_retryOptions.EnableRetries)
        {
            return ResiliencePipeline.Empty;
        }

        return new ResiliencePipelineBuilder()
            .AddRetry(new RetryStrategyOptions
            {
                MaxRetryAttempts = _retryOptions.MaxRetryAttempts,
                BackoffType = DelayBackoffType.Exponential,
                Delay = TimeSpan.FromMilliseconds(_retryOptions.BaseDelayMs * 2), // Longer delays for sync operations
                MaxDelay = TimeSpan.FromMilliseconds(_retryOptions.MaxDelayMs),
                UseJitter = true,
                ShouldHandle = new PredicateBuilder()
                    .Handle<HttpRequestException>()
                    .Handle<TaskCanceledException>()
                    .Handle<InvalidOperationException>(ex =>
                        !ex.Message.Contains("conflict", StringComparison.OrdinalIgnoreCase)), // Don't retry conflicts
                OnRetry = args =>
                {
                    _logger.LogWarning("Sync operation retry attempt {AttemptNumber} due to: {Exception}. " +
                        "Retrying in {Delay}ms",
                        args.AttemptNumber + 1, args.Outcome.Exception?.Message,
                        args.RetryDelay.TotalMilliseconds);
                    return ValueTask.CompletedTask;
                }
            })
            .Build();
    }

    private static bool ShouldRetryHttpStatusCode(HttpStatusCode statusCode)
    {
        return statusCode switch
        {
            // Retry on server errors
            HttpStatusCode.InternalServerError => true,
            HttpStatusCode.BadGateway => true,
            HttpStatusCode.ServiceUnavailable => true,
            HttpStatusCode.GatewayTimeout => true,

            // Retry on some client errors that might be transient
            HttpStatusCode.RequestTimeout => true,
            HttpStatusCode.TooManyRequests => true,

            // Don't retry on other client errors
            HttpStatusCode.BadRequest => false,
            HttpStatusCode.Unauthorized => false,
            HttpStatusCode.Forbidden => false,
            HttpStatusCode.NotFound => false,
            HttpStatusCode.Conflict => false,

            // Default to not retry for other codes
            _ => false
        };
    }
}
