using System.Text.Json.Serialization;

public class AppInfo
{
    [JsonPropertyName("name")]
    public string Name { get; set; }

    [JsonPropertyName("title")]
    public string Title { get; set; }

    [JsonPropertyName("description")]
    public string Description { get; set; }

    [JsonPropertyName("iconPath")]
    public string IconPath { get; set; }

    [JsonPropertyName("previewPath")]
    public string PreviewPath { get; set; }

    [JsonPropertyName("downloadUrl")]
    public string DownloadUrl { get; set; }
}
