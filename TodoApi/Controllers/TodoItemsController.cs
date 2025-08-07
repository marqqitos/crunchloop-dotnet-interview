using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TodoApi.Dtos;
using TodoApi.Models;
using TodoApi.Services;

namespace TodoApi.Controllers
{
    [Route("api/todolists/{todoListId}/items")]
    [ApiController]
    public class TodoItemsController : ControllerBase
    {
        private readonly TodoContext _context;
        private readonly IChangeDetectionService _changeDetectionService;

        public TodoItemsController(TodoContext context, IChangeDetectionService changeDetectionService)
        {
            _context = context;
            _changeDetectionService = changeDetectionService;
        }

        // GET: api/todolists/5/items
        [HttpGet]
        public async Task<ActionResult<IList<TodoItemResponse>>> GetTodoItems(long todoListId)
        {
            var todoList = await _context.TodoList.FindAsync(todoListId);
            if (todoList == null)
            {
                return NotFound($"TodoList with id {todoListId} not found");
            }

            var todoItems = await _context.TodoItem
                .Where(item => item.TodoListId == todoListId)
                .Select(item => new TodoItemResponse
                {
                    Id = item.Id,
                    Description = item.Description,
                    Completed = item.IsCompleted,
                    TodoListId = item.TodoListId
                })
                .ToListAsync();

            return Ok(todoItems);
        }

        // GET: api/todolists/5/items/3
        [HttpGet("{id}")]
        public async Task<ActionResult<TodoItemResponse>> GetTodoItem(long todoListId, long id)
        {
            var todoList = await _context.TodoList.FindAsync(todoListId);
            if (todoList == null)
            {
                return NotFound($"TodoList with id {todoListId} not found");
            }

            var todoItem = await _context.TodoItem
                .Where(item => item.Id == id && item.TodoListId == todoListId)
                .Select(item => new TodoItemResponse
                {
                    Id = item.Id,
                    Description = item.Description,
                    Completed = item.IsCompleted,
                    TodoListId = item.TodoListId
                })
                .FirstOrDefaultAsync();

            if (todoItem == null)
            {
                return NotFound($"TodoItem with id {id} not found in TodoList {todoListId}");
            }

            return Ok(todoItem);
        }

        // PUT: api/todolists/5/items/3
        // To protect from over-posting attacks, see https://go.microsoft.com/fwlink/?linkid=2123754
        [HttpPut("{id}")]
        public async Task<ActionResult<TodoItemResponse>> PutTodoItem(long todoListId, long id, UpdateTodoItem payload)
        {
            var todoList = await _context.TodoList.FindAsync(todoListId);
            if (todoList == null)
            {
                return NotFound($"TodoList with id {todoListId} not found");
            }

            var todoItem = await _context.TodoItem
                .FirstOrDefaultAsync(item => item.Id == id && item.TodoListId == todoListId);

            if (todoItem == null)
            {
                return NotFound($"TodoItem with id {id} not found in TodoList {todoListId}");
            }

            todoItem.Description = payload.Description;
            todoItem.IsCompleted = payload.Completed;
            todoItem.LastModified = DateTime.UtcNow;
            todoItem.IsSyncPending = true; // Mark as pending for sync
            
            await _context.SaveChangesAsync();

            var response = new TodoItemResponse
            {
                Id = todoItem.Id,
                Description = todoItem.Description,
                Completed = todoItem.IsCompleted,
                TodoListId = todoItem.TodoListId
            };

            return Ok(response);
        }

        // POST: api/todolists/5/items
        // To protect from over-posting attacks, see https://go.microsoft.com/fwlink/?linkid=2123754
        [HttpPost]
        public async Task<ActionResult<TodoItemResponse>> PostTodoItem(long todoListId, CreateTodoItem payload)
        {
            var todoList = await _context.TodoList.FindAsync(todoListId);
            if (todoList == null)
            {
                return NotFound($"TodoList with id {todoListId} not found");
            }

            var todoItem = new TodoItem 
            { 
                Description = payload.Description,
                IsCompleted = payload.Completed,
                TodoListId = todoListId,
                LastModified = DateTime.UtcNow,
                IsSyncPending = true // Mark as pending for sync
            };

            _context.TodoItem.Add(todoItem);
            await _context.SaveChangesAsync();

            var response = new TodoItemResponse
            {
                Id = todoItem.Id,
                Description = todoItem.Description,
                Completed = todoItem.IsCompleted,
                TodoListId = todoItem.TodoListId
            };

            return CreatedAtAction("GetTodoItem", new { todoListId = todoListId, id = todoItem.Id }, response);
        }

        // DELETE: api/todolists/5/items/3
        [HttpDelete("{id}")]
        public async Task<ActionResult> DeleteTodoItem(long todoListId, long id)
        {
            var todoList = await _context.TodoList.FindAsync(todoListId);
            if (todoList == null)
            {
                return NotFound($"TodoList with id {todoListId} not found");
            }

            var todoItem = await _context.TodoItem
                .FirstOrDefaultAsync(item => item.Id == id && item.TodoListId == todoListId);

            if (todoItem == null)
            {
                return NotFound($"TodoItem with id {id} not found in TodoList {todoListId}");
            }

            _context.TodoItem.Remove(todoItem);
            await _context.SaveChangesAsync();

            return NoContent();
        }
    }
}