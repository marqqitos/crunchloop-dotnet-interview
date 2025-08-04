using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace ExternalTodoApi.Dtos;

public class UpdateTodoListRequest
{
    [Required]
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;
}
