using Microsoft.EntityFrameworkCore;
using TodoApi.Dtos;
using TodoApi.Models;

namespace TodoApi.Services.TodoListService;

public class TodoListService : ITodoListService
{
	private readonly TodoContext _context;
	private readonly ILogger<TodoListService> _logger;

	public TodoListService(TodoContext context, ILogger<TodoListService> logger)
	{
		_context = context;
		_logger = logger;
	}

	public async Task<IList<TodoListResponse>> GetTodoListsAsync()
		=> await _context.TodoList
			.Where(tl => !tl.IsDeleted)
			.Select(tl => new TodoListResponse
			{
				Id = tl.Id,
				Name = tl.Name,
				Items = tl.Items
					.Where(item => !item.IsDeleted)
					.Select(item => new TodoItemResponse
					{
						Id = item.Id,
						Description = item.Description,
						Completed = item.IsCompleted,
						TodoListId = item.TodoListId
					}).ToList()
			})
			.ToListAsync();

	public async Task<TodoListResponse?> GetTodoListAsync(long id)
		=> await _context.TodoList
			.Where(tl => !tl.IsDeleted && tl.Id == id)
			.Select(tl => new TodoListResponse
			{
				Id = tl.Id,
				Name = tl.Name,
				Items = tl.Items
					.Where(item => !item.IsDeleted)
					.Select(item => new TodoItemResponse
					{
						Id = item.Id,
						Description = item.Description,
						Completed = item.IsCompleted,
						TodoListId = item.TodoListId
					}).ToList()
			})
			.FirstOrDefaultAsync();

	public async Task<TodoListResponse?> UpdateTodoListAsync(long id, UpdateTodoList payload)
	{
		var todoList = await _context.TodoList
			.Where(tl => !tl.IsDeleted)
			.Include(tl => tl.Items.Where(item => !item.IsDeleted))
			.FirstOrDefaultAsync(tl => tl.Id == id);

		if (todoList is null)
			return null;

		todoList.Name = payload.Name;
		todoList.LastModified = DateTime.UtcNow;
		todoList.IsSyncPending = true;

		await _context.SaveChangesAsync();

		return new TodoListResponse
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
	}

	public async Task<TodoListResponse> CreateTodoListAsync(CreateTodoList payload)
	{
		var todoList = new TodoList
		{
			Name = payload.Name,
			LastModified = DateTime.UtcNow,
			IsSyncPending = true
		};

		_context.TodoList.Add(todoList);
		await _context.SaveChangesAsync();

		return new TodoListResponse
		{
			Id = todoList.Id,
			Name = todoList.Name,
			Items = new List<TodoItemResponse>()
		};
	}

	public async Task<bool> DeleteTodoListAsync(long id)
	{
		var todoList = await _context.TodoList
			.Include(tl => tl.Items)
			.FirstOrDefaultAsync(tl => tl.Id == id && !tl.IsDeleted);

		if (todoList is null)
			return false;

		// Soft delete the TodoList and all its items
		var now = DateTime.UtcNow;
		todoList.IsDeleted = true;
		todoList.DeletedAt = now;
		todoList.IsSyncPending = true;
		todoList.LastModified = now;

		// Also soft delete all items in the list
		foreach (var item in todoList.Items.Where(i => !i.IsDeleted))
		{
			item.IsDeleted = true;
			item.DeletedAt = now;
			item.IsSyncPending = true;
			item.LastModified = now;
		}

		await _context.SaveChangesAsync();
		_logger.LogInformation("Soft deleted TodoList {TodoListId} and {ItemCount} items", id, todoList.Items.Count(i => !i.IsDeleted));

		return true;
	}

    public async Task MarkAsPendingAsync(long todoListId)
    {
        var todoList = await _context.TodoList
			.FirstOrDefaultAsync(tl => tl.Id == todoListId && !tl.IsDeleted);

		if (todoList is not null)
        {
            todoList.IsSyncPending = true;
            todoList.LastModified = DateTime.UtcNow;
            await _context.SaveChangesAsync();

            _logger.LogDebug("Marked TodoList {TodoListId} as pending sync", todoListId);
        }
        else
        {
            _logger.LogWarning("Attempted to mark non-existent TodoList {TodoListId} as pending", todoListId);
        }
    }

	public async Task<int> GetPendingChangesCountAsync()
		=> await _context.TodoList.CountAsync(tl => tl.IsSyncPending && !tl.IsDeleted);

	public async Task ClearPendingFlagAsync(long todoListId)
	{
		var todoList = await _context.TodoList
			.FirstOrDefaultAsync(tl => tl.Id == todoListId && !tl.IsDeleted);
		if (todoList is not null)
		{
			todoList.IsSyncPending = false;
			todoList.LastSyncedAt = DateTime.UtcNow;
			await _context.SaveChangesAsync();

			_logger.LogDebug("Cleared pending flag for TodoList {TodoListId}", todoListId);
		}
	}
}


