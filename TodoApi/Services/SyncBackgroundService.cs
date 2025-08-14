using Microsoft.Extensions.Options;
using TodoApi.Configuration;
using TodoApi.Services.SyncService;

namespace TodoApi.Services;

/// <summary>
/// Background service that performs periodic synchronization between local and external APIs
/// </summary>
public class SyncBackgroundService : BackgroundService
{
	private readonly IServiceScopeFactory _serviceScopeFactory;
	private readonly ILogger<SyncBackgroundService> _logger;
	private readonly SyncOptions _syncOptions;

	public SyncBackgroundService(
		IServiceScopeFactory serviceScopeFactory,
		ILogger<SyncBackgroundService> logger,
		IOptions<SyncOptions> syncOptions)
	{
		_serviceScopeFactory = serviceScopeFactory;
		_logger = logger;
		_syncOptions = syncOptions.Value;
	}

	protected override async Task ExecuteAsync(CancellationToken stoppingToken)
	{
		_logger.LogInformation("Sync background service started. Sync interval: {IntervalMinutes} minutes",
			_syncOptions.SyncIntervalSeconds);

		while (!stoppingToken.IsCancellationRequested)
		{
			try
			{
				_logger.LogDebug("Starting periodic sync at {Timestamp}", DateTime.UtcNow);

				using var scope = _serviceScopeFactory.CreateScope();
				var syncService = scope.ServiceProvider.GetRequiredService<ISyncService>();

				await syncService.PerformFullSync();

				await syncService.DetectAndHandleExternalDeletions();

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
				var interval = TimeSpan.FromSeconds(_syncOptions.SyncIntervalSeconds);
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
