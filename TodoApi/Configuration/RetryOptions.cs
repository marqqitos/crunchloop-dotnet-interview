namespace TodoApi.Configuration;

/// <summary>
/// Configuration options for retry policies
/// </summary>
public class RetryOptions
{
    public const string SectionName = "RetryPolicy";
    
    /// <summary>
    /// Maximum number of retry attempts for external API calls
    /// </summary>
    public int MaxRetryAttempts { get; set; } = 3;
    
    /// <summary>
    /// Base delay for exponential backoff in milliseconds
    /// </summary>
    public int BaseDelayMs { get; set; } = 1000;
    
    /// <summary>
    /// Maximum delay between retries in milliseconds
    /// </summary>
    public int MaxDelayMs { get; set; } = 30000;
    
    /// <summary>
    /// Jitter factor to add randomness to retry delays (0.0 to 1.0)
    /// </summary>
    public double JitterFactor { get; set; } = 0.1;
    
    /// <summary>
    /// Timeout for individual HTTP requests in seconds
    /// </summary>
    public int RequestTimeoutSeconds { get; set; } = 30;
    
    /// <summary>
    /// Whether to enable retry logic
    /// </summary>
    public bool EnableRetries { get; set; } = true;
}