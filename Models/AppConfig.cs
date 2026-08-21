using System.Text.Json;
using System.IO;

namespace Helinstaller.Models
{
    public class SettingsData
    {
        public bool IsVisualizerEnabled { get; set; } = true;
        public bool IsMusicAutoPlayEnabled { get; set; } = true;
        public int InstallTimeoutSeconds { get; set; } = 900;
        public bool IsOnboardingCompleted { get; set; } = false;

        // Новые настройки прокси:
        public int ProxyPingTimeoutMs { get; set; } = 2000;
        public int ProxyMaxParallelism { get; set; } = 30;
    }

    public static class AppSettings
    {
        private static readonly string FileName = "settings.json";

        // Текущие значения в памяти
        public static bool IsVisualizerEnabled { get; set; } = true;
        public static bool IsMusicAutoPlayEnabled { get; set; } = true;
        public static int InstallTimeoutSeconds { get; set; } = 900;
        public static bool IsOnboardingCompleted { get; set; } = false;

        // Новые параметры
        public static int ProxyPingTimeoutMs { get; set; } = 2000;
        public static int ProxyMaxParallelism { get; set; } = 30;

        // Сохранить в файл
        public static void Save()
        {
            var data = new SettingsData
            {
                IsVisualizerEnabled = IsVisualizerEnabled,
                IsMusicAutoPlayEnabled = IsMusicAutoPlayEnabled,
                InstallTimeoutSeconds = InstallTimeoutSeconds,
                IsOnboardingCompleted = IsOnboardingCompleted,
                ProxyPingTimeoutMs = ProxyPingTimeoutMs,
                ProxyMaxParallelism = ProxyMaxParallelism
            };

            try
            {
                string json = JsonSerializer.Serialize(data, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(FileName, json);
            }
            catch { }
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
                    InstallTimeoutSeconds = data.InstallTimeoutSeconds >= 15 ? data.InstallTimeoutSeconds : 900;
                    IsOnboardingCompleted = data.IsOnboardingCompleted;
                    ProxyPingTimeoutMs = data.ProxyPingTimeoutMs >= 500 && data.ProxyPingTimeoutMs <= 5000 ? data.ProxyPingTimeoutMs : 2000;
                    ProxyMaxParallelism = data.ProxyMaxParallelism >= 5 && data.ProxyMaxParallelism <= 100 ? data.ProxyMaxParallelism : 30;
                }
            }
            catch { }
        }
    }
}