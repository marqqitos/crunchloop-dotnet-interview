using Microsoft.AspNetCore.Mvc;
using TodoApi.Services;

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
    /// Manually trigger sync of TodoLists to external API
    /// </summary>
    [HttpPost("todolists")]
    public async Task<IActionResult> SyncTodoLists()
    {
        try
        {
            _logger.LogInformation("Manual sync triggered via API");
            
            await _syncService.SyncTodoListsToExternalAsync();
            
            return Ok(new { message = "TodoLists sync completed successfully" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Manual sync failed");
            return StatusCode(500, new { error = "Sync failed", details = ex.Message });
        }
    }
}