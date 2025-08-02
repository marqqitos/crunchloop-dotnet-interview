namespace TodoApi.Dtos;

public class TodoItemResponse
{
    public long Id { get; set; }
    public string Description { get; set; } = string.Empty;
    public bool IsCompleted { get; set; }
    public long TodoListId { get; set; }
}