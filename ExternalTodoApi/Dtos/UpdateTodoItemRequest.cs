using System.Text.Json.Serialization;

namespace ExternalTodoApi.Dtos;

public class UpdateTodoItemRequest
{
    [JsonPropertyName("description")]
    public string? Description { get; set; }

    [JsonPropertyName("completed")]
    public bool? Completed { get; set; }
}