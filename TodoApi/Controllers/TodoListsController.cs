using Microsoft.AspNetCore.Mvc;
using TodoApi.Dtos;
using TodoApi.Services.TodoListService;

namespace TodoApi.Controllers
{
    [Route("api/todolists")]
    [ApiController]
    public class TodoListsController : ControllerBase
    {
        private readonly ITodoListService _todoListService;

        public TodoListsController(ITodoListService todoListService)
        {
            _todoListService = todoListService;
        }

        [HttpGet]
        public async Task<ActionResult<IList<TodoListResponse>>> GetTodoLists()
        {
            var todoLists = await _todoListService.GetTodoLists();
            return Ok(todoLists);
        }

        [HttpGet("{id}")]
        public async Task<ActionResult<TodoListResponse>> GetTodoList(long id)
        {
            var todoList = await _todoListService.GetTodoListById(id);

			if (todoList is null)
				return NotFound();

            return Ok(todoList);
        }

        [HttpPut("{id}")]
        public async Task<ActionResult<TodoListResponse>> PutTodoList(long id, UpdateTodoList payload)
        {
            var updated = await _todoListService.UpdateTodoList(id, payload);

			if (updated is null)
				return NotFound();

            return Ok(updated);
        }

        [HttpPost]
        public async Task<ActionResult<TodoListResponse>> PostTodoList(CreateTodoList payload)
        {
            var created = await _todoListService.CreateTodoList(payload);
            return CreatedAtAction("GetTodoList", new { id = created.Id }, created);
        }

        [HttpDelete("{id}")]
        public async Task<ActionResult> DeleteTodoList(long id)
        {
            var found = await _todoListService.DeleteTodoList(id);

			if (!found)
				return NotFound();

            return NoContent();
        }

        private bool TodoListExists(long id) => throw new NotImplementedException();
    }
}
