using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Windows.Media.Imaging; // НУЖНО ДЛЯ PNG ENCODER

namespace Helinstaller.Helpers
{
    public static class MetadataService
    {
        private static readonly HttpClient _client = new HttpClient();

        static MetadataService() { _client.DefaultRequestHeaders.UserAgent.ParseAdd("HelinstallerApp/1.0"); }

        public static async Task<(string Title, string Description, string IconUrl)> GetMetadataAsync(string downloadUrl)
        {
            if (string.IsNullOrWhiteSpace(downloadUrl)) return ("Приложение", "", "");

            // 1. GitHub
            if (downloadUrl.Contains("github.com") || downloadUrl.StartsWith("github:"))
            {
                try
                {
                    var repoPath = downloadUrl.Replace("github:", "").Replace("https://github.com/", "");
                    var apiUri = $"https://api.github.com/repos/{repoPath.Trim('/')}";
                    var response = await _client.GetFromJsonAsync<JsonElement>(apiUri);

                    return (
                        response.GetProperty("name").GetString() ?? "",
                        response.GetProperty("description").GetString() ?? "Описание из GitHub",
                        response.GetProperty("owner").GetProperty("avatar_url").GetString() ?? ""
                    );
                }
                catch { }
            }

            // 2. Winget
            if (downloadUrl.StartsWith("winget:", StringComparison.OrdinalIgnoreCase))
            {
                string appId = downloadUrl.Replace("winget:", "").Trim();
                return await GetWingetMetadata(appId);
            }

            // 3. Обычные сайты (Clearbit - топ качество, если есть)
            if (Uri.TryCreate(downloadUrl, UriKind.Absolute, out var uri))
            {
                string highResIcon = $"https://logo.clearbit.com/{uri.Host}?size=256";
                return (uri.Host, $"Программа с сайта {uri.Host}", highResIcon);
            }

            return ("Приложение", "Описание отсутствует", "");
        }

        // ПЕРЕДЕЛАННЫЙ МЕТОД СКАЧИВАНИЯ (ПРИНУДИТЕЛЬНАЯ КОНВЕРТАЦИЯ В PNG)
        public static async Task<string?> DownloadIconAsync(string url, string fileName)
        {
            try
            {
                string assetsDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Assets");
                if (!Directory.Exists(assetsDir)) Directory.CreateDirectory(assetsDir);

                string safeName = string.Join("_", fileName.Split(Path.GetInvalidFileNameChars()));
                string filePath = Path.Combine(assetsDir, safeName + ".png");

                // Если уже есть — не качаем
                if (File.Exists(filePath)) return $"Assets/{safeName}.png";

                using var response = await _client.GetAsync(url);
                if (response.IsSuccessStatusCode)
                {
                    byte[] data = await response.Content.ReadAsByteArrayAsync();

                    // Магия WPF: Читаем ЛЮБОЙ формат и перекодируем в чистый прозрачный PNG
                    var bitmap = new BitmapImage();
                    using (var mem = new MemoryStream(data))
                    {
                        bitmap.BeginInit();
                        bitmap.CreateOptions = BitmapCreateOptions.PreservePixelFormat;
                        bitmap.CacheOption = BitmapCacheOption.OnLoad;
                        bitmap.StreamSource = mem;
                        bitmap.EndInit();
                        bitmap.Freeze(); // Замораживаем для использования в фоновом потоке
                    }

                    var encoder = new PngBitmapEncoder();
                    encoder.Frames.Add(BitmapFrame.Create(bitmap));

                    using (var fileStream = new FileStream(filePath, FileMode.Create))
                    {
                        encoder.Save(fileStream);
                    }

                    return $"Assets/{safeName}.png";
                }
            }
            catch { }
            return null;
        }

        private static async Task<(string Title, string Description, string IconUrl)> GetWingetMetadata(string appId)
        {
            return await Task.Run(() =>
            {
                try
                {
                    ProcessStartInfo psi = new ProcessStartInfo
                    {
                        FileName = "winget",
                        Arguments = $"show --id {appId}",
                        RedirectStandardOutput = true,
                        UseShellExecute = false,
                        CreateNoWindow = true,
                        StandardOutputEncoding = System.Text.Encoding.GetEncoding(866)
                    };

                    using var process = Process.Start(psi);
                    string output = process.StandardOutput.ReadToEnd();
                    process.WaitForExit();

                    string title = Regex.Match(output, @"(?:Название|Name):\s*(.*)", RegexOptions.IgnoreCase).Groups[1].Value.Trim();
                    string home = Regex.Match(output, @"(?:Домашняя страница|Homepage):\s*(.*)", RegexOptions.IgnoreCase).Groups[1].Value.Trim();

                    if (string.IsNullOrEmpty(title)) title = appId;
                    string iconUrl = "";

                    // Заменили Google Favicons на Icon.Horse (лучше работает с прозрачностью)
                    if (Uri.TryCreate(home, UriKind.Absolute, out var uri))
                        iconUrl = $"https://icon.horse/icon/{uri.Host}";

                    return (title, "Приложение из Winget.", iconUrl);
                }
                catch { return (appId, "Ошибка получения данных", ""); }
            });
        }
    }
}