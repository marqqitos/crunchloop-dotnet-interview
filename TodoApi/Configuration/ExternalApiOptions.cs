namespace TodoApi.Configuration;

public class ExternalApiOptions
{
    public const string SectionName = "ExternalApi";
    
    public string BaseUrl { get; set; } = string.Empty;
    public int TimeoutSeconds { get; set; } = 30;
    public string SourceId { get; set; } = string.Empty;
}