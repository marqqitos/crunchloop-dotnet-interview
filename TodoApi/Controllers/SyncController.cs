using Microsoft.AspNetCore.Mvc;
using TodoApi.Services.SyncService;

namespace TodoApi.Controllers;

[Route("api/sync")]
[ApiController]
public class SyncController : ControllerBase
{
    private readonly ISyncService _syncService;
    private readonly ILogger<SyncController> _logger;

    public SyncController(ISyncService syncService, ILogger<SyncController> logger)
    {
        _syncService = syncService;
        _logger = logger;
    }

    /// <summary>
    /// Manually trigger sync of TodoLists to external API (outbound: Local → External)
    /// </summary>
    [HttpPost("todolists")]
    public async Task<IActionResult> SyncTodoLists()
    {
        try
        {
            _logger.LogInformation("Manual outbound sync triggered via API");

            await _syncService.SyncTodoListsToExternal();

            return Ok(new { message = "Outbound TodoLists sync completed successfully" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Manual outbound sync failed");
            return StatusCode(500, new { error = "Outbound sync failed", details = ex.Message });
        }
    }

    /// <summary>
    /// Manually trigger sync of TodoLists from external API (inbound: External → Local)
    /// </summary>
    [HttpPost("todolists/inbound")]
    public async Task<IActionResult> SyncTodoListsFromExternal()
    {
        try
        {
            _logger.LogInformation("Manual inbound sync triggered via API");

            await _syncService.SyncTodoListsFromExternal();

            return Ok(new { message = "Inbound TodoLists sync completed successfully" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Manual inbound sync failed");
            return StatusCode(500, new { error = "Inbound sync failed", details = ex.Message });
        }
    }

    /// <summary>
    /// Manually trigger bidirectional sync of TodoLists (Local ↔ External)
    /// </summary>
    [HttpPost("todolists/bidirectional")]
    public async Task<IActionResult> PerformFullSync()
    {
        try
        {
            _logger.LogInformation("Manual bidirectional sync triggered via API");

            await _syncService.PerformFullSync();

            return Ok(new { message = "Bidirectional TodoLists sync completed successfully" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Manual bidirectional sync failed");
            return StatusCode(500, new { error = "Bidirectional sync failed", details = ex.Message });
        }
    }
}
