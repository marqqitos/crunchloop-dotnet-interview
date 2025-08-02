namespace TodoApi.Models;

public class TodoList
{
    public long Id { get; set; }
    public required string Name { get; set; }
    public ICollection<TodoItem> Items { get; set; } = new List<TodoItem>();
    
    // Basic sync tracking fields
    public string? ExternalId { get; set; }           // Maps to external API string ID
    public DateTime LastModified { get; set; } = DateTime.UtcNow;  // Track when locally modified
}
