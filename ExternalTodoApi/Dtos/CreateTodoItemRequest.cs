using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace ExternalTodoApi.Dtos;

public class CreateTodoItemRequest
{
    [Required]
    [JsonPropertyName("source_id")]
    public string SourceId { get; set; } = string.Empty;

    [Required]
    [JsonPropertyName("description")]
    public string Description { get; set; } = string.Empty;

    [JsonPropertyName("completed")]
    public bool Completed { get; set; } = false;
}
