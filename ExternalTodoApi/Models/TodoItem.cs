using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace ExternalTodoApi.Models;

public class TodoItem
{
    [Key]
    [JsonPropertyName("id")]
    public string Id { get; set; } = Guid.NewGuid().ToString();

    [Required]
    [JsonPropertyName("source_id")]
    public string SourceId { get; set; } = string.Empty;

    [Required]
    [JsonPropertyName("description")]
    public string Description { get; set; } = string.Empty;

    [JsonPropertyName("completed")]
    public bool Completed { get; set; } = false;

    [JsonPropertyName("created_at")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    [JsonPropertyName("updated_at")]
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    // Foreign key and navigation property for EF Core
    [JsonIgnore]
    public string TodoListId { get; set; } = string.Empty;

    [JsonIgnore]
    public TodoList TodoList { get; set; } = null!;
}
