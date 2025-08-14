using Microsoft.Extensions.Options;
using TodoApi.Configuration;
using TodoApi.Dtos.External;
using TodoApi.Models;
using TodoApi.Services.ExternalTodoApiClient;
using TodoApi.Services.SyncService;
using TodoApi.Services.TodoItemService;
using TodoApi.Services.TodoListService;

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

				// Check if there are pending changes before performing sync
				var todoLists = await GetLocalChangesPendingSync(scope.ServiceProvider);

				if (todoLists.Any())
				{
					_logger.LogInformation("Found pending local changes, performing sync");
					await syncService.SyncTodoListsToExternal(todoLists);
				}

				var externalTodoLists = await GetExternalChangesPendingSync(scope.ServiceProvider);

				if (externalTodoLists.Any())
				{
					await syncService.SyncTodoListsFromExternal(externalTodoLists);
					_logger.LogInformation("Found pending external changes, performing sync");
				}

				await syncService.DetectAndHandleExternalDeletions();
				
				if (!todoLists.Any() && !externalTodoLists.Any())
					_logger.LogDebug("No pending changes found, skipping sync");

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

	private async Task<IEnumerable<TodoList>> GetLocalChangesPendingSync(IServiceProvider serviceProvider)
	{
		var todoListService = serviceProvider.GetRequiredService<ITodoListService>();
		var pendingTodoLists = await todoListService.GetPendingSyncTodoLists();

		return pendingTodoLists;
	}

	private async Task<IEnumerable<ExternalTodoList>> GetExternalChangesPendingSync(IServiceProvider serviceProvider)
	{
		var externalApiClient = serviceProvider.GetRequiredService<IExternalTodoApiClient>();
		var externalTodoLists = await externalApiClient.GetTodoListsPendingSync();

		return externalTodoLists;
	}

	public override async Task StopAsync(CancellationToken cancellationToken)
	{
		_logger.LogInformation("Sync background service is stopping");
		await base.StopAsync(cancellationToken);
	}
}
