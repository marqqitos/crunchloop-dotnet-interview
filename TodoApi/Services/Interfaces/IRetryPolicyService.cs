using Polly;

namespace TodoApi.Services;

/// <summary>
/// Service for creating and managing retry policies
/// </summary>
public interface IRetryPolicyService
{
    /// <summary>
    /// Get retry policy for HTTP operations
    /// </summary>
    ResiliencePipeline GetHttpRetryPolicy();
    
    /// <summary>
    /// Get retry policy for database operations
    /// </summary>
    ResiliencePipeline GetDatabaseRetryPolicy();
    
    /// <summary>
    /// Get retry policy for sync operations
    /// </summary>
    ResiliencePipeline GetSyncRetryPolicy();
}