using System.Text.Json.Serialization;

namespace TodoApi.Dtos.External;

public class UpdateExternalTodoList
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;
}