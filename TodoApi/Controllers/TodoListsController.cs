using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TodoApi.Dtos;
using TodoApi.Models;

namespace TodoApi.Controllers
{
    [Route("api/todolists")]
    [ApiController]
    public class TodoListsController : ControllerBase
    {
        private readonly TodoContext _context;

        public TodoListsController(TodoContext context)
        {
            _context = context;
        }

        // GET: api/todolists
        [HttpGet]
        public async Task<ActionResult<IList<TodoListResponse>>> GetTodoLists()
        {
            var todoLists = await _context.TodoList
                .Include(tl => tl.Items)
                .Select(tl => new TodoListResponse
                {
                    Id = tl.Id,
                    Name = tl.Name,
                    Items = tl.Items.Select(item => new TodoItemResponse
                    {
                        Id = item.Id,
                        Description = item.Description,
                        Completed = item.IsCompleted,
                        TodoListId = item.TodoListId
                    }).ToList()
                })
                .ToListAsync();

            return Ok(todoLists);
        }

        // GET: api/todolists/5
        [HttpGet("{id}")]
        public async Task<ActionResult<TodoListResponse>> GetTodoList(long id)
        {
            var todoList = await _context.TodoList
                .Include(tl => tl.Items)
                .Where(tl => tl.Id == id)
                .Select(tl => new TodoListResponse
                {
                    Id = tl.Id,
                    Name = tl.Name,
                    Items = tl.Items.Select(item => new TodoItemResponse
                    {
                        Id = item.Id,
                        Description = item.Description,
                        Completed = item.IsCompleted,
                        TodoListId = item.TodoListId
                    }).ToList()
                })
                .FirstOrDefaultAsync();

            if (todoList == null)
            {
                return NotFound();
            }

            return Ok(todoList);
        }

        // PUT: api/todolists/5
        // To protect from over-posting attacks, see https://go.microsoft.com/fwlink/?linkid=2123754
        [HttpPut("{id}")]
        public async Task<ActionResult<TodoListResponse>> PutTodoList(long id, UpdateTodoList payload)
        {
            var todoList = await _context.TodoList
                .Include(tl => tl.Items)
                .FirstOrDefaultAsync(tl => tl.Id == id);

            if (todoList == null)
            {
                return NotFound();
            }

            todoList.Name = payload.Name;
            await _context.SaveChangesAsync();

            var response = new TodoListResponse
            {
                Id = todoList.Id,
                Name = todoList.Name,
                Items = todoList.Items.Select(item => new TodoItemResponse
                {
                    Id = item.Id,
                    Description = item.Description,
                    Completed = item.IsCompleted,
                    TodoListId = item.TodoListId
                }).ToList()
            };

            return Ok(response);
        }

        // POST: api/todolists
        // To protect from over-posting attacks, see https://go.microsoft.com/fwlink/?linkid=2123754
        [HttpPost]
        public async Task<ActionResult<TodoListResponse>> PostTodoList(CreateTodoList payload)
        {
            var todoList = new TodoList { Name = payload.Name };

            _context.TodoList.Add(todoList);
            await _context.SaveChangesAsync();

            var response = new TodoListResponse
            {
                Id = todoList.Id,
                Name = todoList.Name,
                Items = new List<TodoItemResponse>()
            };

            return CreatedAtAction("GetTodoList", new { id = todoList.Id }, response);
        }

        // DELETE: api/todolists/5
        [HttpDelete("{id}")]
        public async Task<ActionResult> DeleteTodoList(long id)
        {
            var todoList = await _context.TodoList.FindAsync(id);
            if (todoList == null)
            {
                return NotFound();
            }

            _context.TodoList.Remove(todoList);
            await _context.SaveChangesAsync();

            return NoContent();
        }

        private bool TodoListExists(long id)
        {
            return (_context.TodoList?.Any(e => e.Id == id)).GetValueOrDefault();
        }
    }
}
