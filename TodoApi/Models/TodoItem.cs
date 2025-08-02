namespace TodoApi.Models;

public class TodoItem
{
    public long Id { get; set; }
    public string Description { get; set; } = string.Empty;
    public bool IsCompleted { get; set; }
    public long TodoListId { get; set; }
    public TodoList TodoList { get; set; } = null!;
    
    // Basic sync tracking fields
    public string? ExternalId { get; set; }           // Maps to external API string ID
    public DateTime LastModified { get; set; } = DateTime.UtcNow;  // Track when locally modified
}
