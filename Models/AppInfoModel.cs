// FILE [CS]: .\Models\AppInfoModel.cs

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

        [JsonPropertyName("category")]
        public string? Category { get; set; }

        [JsonPropertyName("checkPattern")]
        public string? CheckPattern { get; set; }

        // Новое свойство: "carousel" (по умолчанию) или "list" (список во всю ширину)
        [JsonPropertyName("layout")]
        public string? Layout { get; set; }
    }
}