using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ExternalTodoApi.Data;
using ExternalTodoApi.Models;
using ExternalTodoApi.Dtos;

namespace ExternalTodoApi.Controllers;

[ApiController]
[Route("todolists")]
public class TodoListsController : ControllerBase
{
    private readonly ExternalTodoContext _context;
    private readonly ILogger<TodoListsController> _logger;

    public TodoListsController(ExternalTodoContext context, ILogger<TodoListsController> logger)
    {
        _context = context;
        _logger = logger;
    }

    // GET /todolists
    [HttpGet]
    public async Task<ActionResult<IEnumerable<TodoList>>> GetTodoLists([FromQuery] DateTime? modified_since = null)
    {
        _logger.LogInformation("Fetching TodoLists with modified_since filter: {ModifiedSince}", modified_since);

        var query = _context.TodoLists.Include(tl => tl.Items).AsQueryable();

        // Apply delta sync filter if provided
        if (modified_since.HasValue)
        {
            query = query.Where(tl => tl.UpdatedAt >= modified_since.Value);
            _logger.LogInformation("Filtering TodoLists modified since {ModifiedSince}", modified_since.Value);
        }

        var todoLists = await query
            .OrderBy(tl => tl.CreatedAt)
            .ToListAsync();

        _logger.LogInformation("Retrieved {Count} TodoLists (filtered: {IsFiltered})",
            todoLists.Count, modified_since.HasValue);
        return Ok(todoLists);
    }

    // POST /todolists
    [HttpPost]
    public async Task<ActionResult<TodoList>> CreateTodoList([FromBody] CreateTodoListRequest request)
    {
        _logger.LogInformation("Creating new TodoList '{Name}' with {ItemCount} items",
            request.Name, request.Items.Count);

        var todoList = new TodoList
        {
            Id = Guid.NewGuid().ToString(),
            SourceId = request.SourceId,
            Name = request.Name,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        // Create associated items
        foreach (var itemRequest in request.Items)
        {
            var todoItem = new TodoItem
            {
                Id = Guid.NewGuid().ToString(),
                SourceId = itemRequest.SourceId,
                Description = itemRequest.Description,
                Completed = itemRequest.Completed,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow,
                TodoListId = todoList.Id
            };
            todoList.Items.Add(todoItem);
        }

        _context.TodoLists.Add(todoList);
        await _context.SaveChangesAsync();

        _logger.LogInformation("Created TodoList '{Id}' with {ItemCount} items",
            todoList.Id, todoList.Items.Count);

        return CreatedAtAction(nameof(CreateTodoList), new { id = todoList.Id }, todoList);
    }

    // PATCH /todolists/{id}
    [HttpPatch("{id}")]
    public async Task<ActionResult<TodoList>> UpdateTodoList(string id, [FromBody] UpdateTodoListRequest request)
    {
        _logger.LogInformation("Updating TodoList '{Id}'", id);

        var todoList = await _context.TodoLists
            .Include(tl => tl.Items)
            .FirstOrDefaultAsync(tl => tl.Id == id);

        if (todoList == null)
        {
            _logger.LogWarning("TodoList '{Id}' not found", id);
            return NotFound();
        }

        todoList.Name = request.Name;
        todoList.UpdatedAt = DateTime.UtcNow;

        await _context.SaveChangesAsync();

        _logger.LogInformation("Updated TodoList '{Id}'", id);
        return Ok(todoList);
    }

    // DELETE /todolists/{id}
    [HttpDelete("{id}")]
    public async Task<IActionResult> DeleteTodoList(string id)
    {
        _logger.LogInformation("Deleting TodoList '{Id}'", id);

        var todoList = await _context.TodoLists
            .Include(tl => tl.Items)
            .FirstOrDefaultAsync(tl => tl.Id == id);

        if (todoList == null)
        {
            _logger.LogWarning("TodoList '{Id}' not found", id);
            return NotFound();
        }

        _context.TodoLists.Remove(todoList);
        await _context.SaveChangesAsync();

        _logger.LogInformation("Deleted TodoList '{Id}' and {ItemCount} items", id, todoList.Items.Count);
        return NoContent();
    }

    // PATCH /todolists/{todolistId}/todoitems/{todoitemId}
    [HttpPatch("{todolistId}/todoitems/{todoitemId}")]
    public async Task<ActionResult<TodoItem>> UpdateTodoItem(
        string todolistId,
        string todoitemId,
        [FromBody] UpdateTodoItemRequest request)
    {
        _logger.LogInformation("Updating TodoItem '{ItemId}' in TodoList '{ListId}'", todoitemId, todolistId);

        var todoItem = await _context.TodoItems
            .FirstOrDefaultAsync(ti => ti.Id == todoitemId && ti.TodoListId == todolistId);

        if (todoItem == null)
        {
            _logger.LogWarning("TodoItem '{ItemId}' not found in TodoList '{ListId}'", todoitemId, todolistId);
            return NotFound();
        }

        if (request.Description != null)
        {
            todoItem.Description = request.Description;
        }

        if (request.Completed.HasValue)
        {
            todoItem.Completed = request.Completed.Value;
        }

        todoItem.UpdatedAt = DateTime.UtcNow;

        await _context.SaveChangesAsync();

        _logger.LogInformation("Updated TodoItem '{ItemId}'", todoitemId);
        return Ok(todoItem);
    }

    // DELETE /todolists/{todolistId}/todoitems/{todoitemId}
    [HttpDelete("{todolistId}/todoitems/{todoitemId}")]
    public async Task<IActionResult> DeleteTodoItem(string todolistId, string todoitemId)
    {
        _logger.LogInformation("Deleting TodoItem '{ItemId}' from TodoList '{ListId}'", todoitemId, todolistId);

        var todoItem = await _context.TodoItems
            .FirstOrDefaultAsync(ti => ti.Id == todoitemId && ti.TodoListId == todolistId);

        if (todoItem == null)
        {
            _logger.LogWarning("TodoItem '{ItemId}' not found in TodoList '{ListId}'", todoitemId, todolistId);
            return NotFound();
        }

        _context.TodoItems.Remove(todoItem);
        await _context.SaveChangesAsync();

        _logger.LogInformation("Deleted TodoItem '{ItemId}'", todoitemId);
        return NoContent();
    }
}
