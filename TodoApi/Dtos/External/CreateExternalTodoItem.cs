using System.Text.Json.Serialization;

namespace TodoApi.Dtos.External;

public class CreateExternalTodoItem
{
    [JsonPropertyName("source_id")]
    public string SourceId { get; set; } = string.Empty;
    
    [JsonPropertyName("description")]
    public string Description { get; set; } = string.Empty;
    
    [JsonPropertyName("completed")]
    public bool Completed { get; set; }
}