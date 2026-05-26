using System.Text.Json.Serialization;

namespace Helinstaller.Models
{
    public class AppInfo
    {
        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;

        [JsonPropertyName("title")]
        public string Title { get; set; } = string.Empty;

        [JsonPropertyName("description")]
        public string? Description { get; set; }

        [JsonPropertyName("iconPath")]
        public string? IconPath { get; set; }

        [JsonPropertyName("downloadUrl")]
        public string DownloadUrl { get; set; } = string.Empty;
    }
}