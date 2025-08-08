using Microsoft.Extensions.Options;
using TodoApi.Configuration;

namespace TodoApi.Services;

/// <summary>
/// Background service that performs periodic synchronization between local and external APIs
/// </summary>
public class SyncBackgroundService : BackgroundService
{
    private readonly ILogger<SyncBackgroundService> _logger;
    private readonly SyncOptions _syncOptions;
    private readonly IServiceScopeFactory _serviceScopeFactory;

    public SyncBackgroundService(
        ILogger<SyncBackgroundService> logger,
        IOptions<SyncOptions> syncOptions,
        IServiceScopeFactory serviceScopeFactory)
    {
        _logger = logger;
        _syncOptions = syncOptions.Value;
        _serviceScopeFactory = serviceScopeFactory;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Sync background service started. Sync interval: {IntervalMinutes} minutes",
            _syncOptions.SyncIntervalMinutes);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                _logger.LogDebug("Starting periodic sync at {Timestamp}", DateTime.UtcNow);

                // Create a scope for this sync operation
                using var scope = _serviceScopeFactory.CreateScope();
                var syncService = scope.ServiceProvider.GetRequiredService<ISyncService>();
                var changeDetectionService = scope.ServiceProvider.GetService<IChangeDetectionService>();

                if (changeDetectionService is not null)
                {
                    // Check if there are pending changes before performing sync
                    var hasPendingChanges = await changeDetectionService.HasPendingChangesAsync();
                    var pendingCount = await changeDetectionService.GetPendingChangesCountAsync();

                    if (hasPendingChanges)
                    {
                        _logger.LogInformation("Found {PendingCount} pending changes, performing sync", pendingCount);
                        await syncService.PerformFullSyncAsync();
                    }
                    else
                    {
                        _logger.LogDebug("No pending changes found, skipping sync");
                    }
                }
                else
                {
                    // If change detection is not available, default to performing sync
                    _logger.LogDebug("Change detection service not available; performing sync by default");
                    await syncService.PerformFullSyncAsync();
                }

                _logger.LogInformation("Periodic sync completed successfully at {Timestamp}", DateTime.UtcNow);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Periodic sync failed at {Timestamp}", DateTime.UtcNow);

                // Don't let sync failures crash the background service
                // The service will continue running and try again on the next interval
            }

            // Wait for the configured interval before the next sync
            try
            {
                var interval = TimeSpan.FromMinutes(_syncOptions.SyncIntervalMinutes);
                await Task.Delay(interval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                // Application is shutting down
                _logger.LogInformation("Sync background service is shutting down");
                break;
            }
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Sync background service is stopping");
        await base.StopAsync(cancellationToken);
    }
}
