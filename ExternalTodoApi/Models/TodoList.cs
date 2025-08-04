using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace ExternalTodoApi.Models;

public class TodoList
{
    [Key]
    [JsonPropertyName("id")]
    public string Id { get; set; } = Guid.NewGuid().ToString();

    [Required]
    [JsonPropertyName("source_id")]
    public string SourceId { get; set; } = string.Empty;

    [Required]
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("created_at")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    [JsonPropertyName("updated_at")]
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    [JsonPropertyName("items")]
    public ICollection<TodoItem> Items { get; set; } = new List<TodoItem>();
}