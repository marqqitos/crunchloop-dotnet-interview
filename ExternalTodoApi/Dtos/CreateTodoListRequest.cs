using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace ExternalTodoApi.Dtos;

public class CreateTodoListRequest
{
    [Required]
    [JsonPropertyName("source_id")]
    public string SourceId { get; set; } = string.Empty;

    [Required]
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("items")]
    public List<CreateTodoItemRequest> Items { get; set; } = new();
}