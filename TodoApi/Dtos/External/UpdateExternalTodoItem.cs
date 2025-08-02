using System.Text.Json.Serialization;

namespace TodoApi.Dtos.External;

public class UpdateExternalTodoItem
{
    [JsonPropertyName("description")]
    public string Description { get; set; } = string.Empty;
    
    [JsonPropertyName("completed")]
    public bool Completed { get; set; }
}