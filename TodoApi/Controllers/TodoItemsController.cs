using Microsoft.AspNetCore.Mvc;
using TodoApi.Dtos;
using TodoApi.Services;

namespace TodoApi.Controllers
{
    [Route("api/todolists/{todoListId}/items")]
    [ApiController]
    public class TodoItemsController : ControllerBase
    {
        private readonly ITodoItemService _todoItemService;

        public TodoItemsController(ITodoItemService todoItemService)
        {
            _todoItemService = todoItemService;
        }

        [HttpGet]
        public async Task<ActionResult<IList<TodoItemResponse>>> GetTodoItems(long todoListId)
        {
            var items = await _todoItemService.GetTodoItemsAsync(todoListId);

			if (items is null)
				return NotFound($"TodoList with id {todoListId} not found");

			return Ok(items);
        }

        [HttpGet("{id}")]
        public async Task<ActionResult<TodoItemResponse>> GetTodoItem(long todoListId, long id)
        {
            var listExists = await _todoItemService.TodoListExistsAsync(todoListId);
            var todoItem = await _todoItemService.GetTodoItemAsync(todoListId, id);

			if (todoItem is null)
            {
                if (!listExists)
                    return NotFound($"TodoList with id {todoListId} not found");

                return NotFound($"TodoItem with id {id} not found in TodoList {todoListId}");
            }

            return Ok(todoItem);
        }

        [HttpPut("{id}")]
        public async Task<ActionResult<TodoItemResponse>> PutTodoItem(long todoListId, long id, UpdateTodoItem payload)
        {
            var listExists = await _todoItemService.TodoListExistsAsync(todoListId);
            var updated = await _todoItemService.UpdateTodoItemAsync(todoListId, id, payload);

			if (updated is null)
            {
                if (!listExists)
                    return NotFound($"TodoList with id {todoListId} not found");

                return NotFound($"TodoItem with id {id} not found in TodoList {todoListId}");
            }

            return Ok(updated);
        }

        [HttpPost]
        public async Task<ActionResult<TodoItemResponse>> PostTodoItem(long todoListId, CreateTodoItem payload)
        {
            var created = await _todoItemService.CreateTodoItemAsync(todoListId, payload);

			if (created is null)
				return NotFound($"TodoList with id {todoListId} not found");

			return CreatedAtAction("GetTodoItem", new { todoListId = todoListId, id = created.Id }, created);
        }

        [HttpDelete("{id}")]
        public async Task<ActionResult> DeleteTodoItem(long todoListId, long id)
        {
            var listExists = await _todoItemService.TodoListExistsAsync(todoListId);
            var found = await _todoItemService.DeleteTodoItemAsync(todoListId, id);

			if (!found)
            {
                if (!listExists)
                    return NotFound($"TodoList with id {todoListId} not found");

                return NotFound($"TodoItem with id {id} not found in TodoList {todoListId}");
            }

            return NoContent();
        }
    }
}
