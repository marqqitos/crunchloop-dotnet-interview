using Microsoft.EntityFrameworkCore;
using TodoApi.Dtos;
using TodoApi.Models;

namespace TodoApi.Services;

public class TodoListService : ITodoListService
{
	private readonly TodoContext _context;

	public TodoListService(TodoContext context)
	{
		_context = context;
	}

	public async Task<IList<TodoListResponse>> GetTodoListsAsync()
		=> await _context.TodoList
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

	public async Task<TodoListResponse?> GetTodoListAsync(long id)
		=> await _context.TodoList
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

	public async Task<TodoListResponse?> UpdateTodoListAsync(long id, UpdateTodoList payload)
	{
		var todoList = await _context.TodoList
			.Include(tl => tl.Items)
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
		var todoList = await _context.TodoList.FindAsync(id);

		if (todoList is null)
			return false;

		_context.TodoList.Remove(todoList);
		await _context.SaveChangesAsync();

		return true;
	}
}


