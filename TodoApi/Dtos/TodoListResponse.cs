namespace TodoApi.Dtos;

public class TodoListResponse
{
    public long Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public IList<TodoItemResponse> Items { get; set; } = new List<TodoItemResponse>();
}