using System.Text.Json;
using System.IO;

namespace Helinstaller.Models
{
        public class SettingsData
        {
            public bool IsVisualizerEnabled { get; set; } = true;
            public bool IsMusicAutoPlayEnabled { get; set; } = true;
        }

        public static class AppSettings
        {
            private static readonly string FileName = "settings.json";

            // Текущие значения в памяти
            public static bool IsVisualizerEnabled { get; set; } = true;
            public static bool IsMusicAutoPlayEnabled { get; set; } = true;

            // Сохранить в файл
            public static void Save()
            {
                var data = new SettingsData
                {
                    IsVisualizerEnabled = IsVisualizerEnabled,
                    IsMusicAutoPlayEnabled = IsMusicAutoPlayEnabled
                };

                try
                {
                    string json = JsonSerializer.Serialize(data, new JsonSerializerOptions { WriteIndented = true });
                    File.WriteAllText(FileName, json);
                }
                catch { /* Игнорируем ошибки записи */ }
            }

            // Загрузить из файла
            public static void Load()
            {
                if (!File.Exists(FileName)) return;

                try
                {
                    string json = File.ReadAllText(FileName);
                    var data = JsonSerializer.Deserialize<SettingsData>(json);
                    if (data != null)
                    {
                        IsVisualizerEnabled = data.IsVisualizerEnabled;
                        IsMusicAutoPlayEnabled = data.IsMusicAutoPlayEnabled;
                    }
                }
                catch { /* Если файл битый — используем дефолты */ }
            }
        }
    }
