using System.Text.Json.Serialization;

namespace TodoApi.Dtos.External;

public class CreateExternalTodoList
{
    [JsonPropertyName("source_id")]
    public string SourceId { get; set; } = string.Empty;
    
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;
    
    [JsonPropertyName("items")]
    public List<CreateExternalTodoItem> Items { get; set; } = new();
}