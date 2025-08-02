namespace TodoApi.Models;

public class TodoItem
{
    public long Id { get; set; }
    public string Description { get; set; } = string.Empty;
    public bool IsCompleted { get; set; }
    public long TodoListId { get; set; }
    public TodoList TodoList { get; set; } = null!;
}
