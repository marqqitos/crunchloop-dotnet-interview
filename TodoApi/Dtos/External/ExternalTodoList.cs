using System.Text.Json.Serialization;

namespace TodoApi.Dtos.External;

public class ExternalTodoList
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;
    
    [JsonPropertyName("source_id")]
    public string SourceId { get; set; } = string.Empty;
    
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;
    
    [JsonPropertyName("created_at")]
    public DateTime CreatedAt { get; set; }
    
    [JsonPropertyName("updated_at")]
    public DateTime UpdatedAt { get; set; }
    
    [JsonPropertyName("items")]
    public List<ExternalTodoItem> Items { get; set; } = new();
}