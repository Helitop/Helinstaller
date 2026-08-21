using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text;
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
            // 2. Winget / MS Store
            if (downloadUrl.StartsWith("winget:", StringComparison.OrdinalIgnoreCase) ||
                downloadUrl.StartsWith("msstore:", StringComparison.OrdinalIgnoreCase) ||
                downloadUrl.StartsWith("ms-windows-store:", StringComparison.OrdinalIgnoreCase))
            {
                string appId = downloadUrl.Replace("winget:", "", StringComparison.OrdinalIgnoreCase)
                                          .Replace("msstore:", "", StringComparison.OrdinalIgnoreCase)
                                          .Replace("ms-windows-store:", "", StringComparison.OrdinalIgnoreCase)
                                          .Replace("//pdp/?productid=", "", StringComparison.OrdinalIgnoreCase)
                                          .Trim('/', ' ');

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
        public static async Task<string> GetInstallerSizeAsync(string downloadUrl)
        {
            if (string.IsNullOrWhiteSpace(downloadUrl)) return "Размер не указан";

            try
            {
                // 1. Для GitHub репозиториев (берем размер из API релизов)
                if (downloadUrl.StartsWith("github:", StringComparison.OrdinalIgnoreCase))
                {
                    string repoPath = downloadUrl.Replace("github:", "").Replace("https://github.com/", "").Trim('/');
                    var response = await _client.GetFromJsonAsync<JsonElement>($"https://api.github.com/repos/{repoPath}/releases/latest");
                    if (response.TryGetProperty("assets", out var assets) && assets.GetArrayLength() > 0)
                    {
                        var priorityExtensions = new[] { ".appinstaller", ".exe", ".msi", ".zip" };
                        foreach (var ext in priorityExtensions)
                        {
                            foreach (var asset in assets.EnumerateArray())
                            {
                                string name = asset.GetProperty("name").GetString() ?? "";
                                if (name.EndsWith(ext, StringComparison.OrdinalIgnoreCase))
                                {
                                    long bytes = asset.GetProperty("size").GetInt64();
                                    return FormatBytes(bytes);
                                }
                            }
                        }
                    }
                }

                // 2. Для Microsoft Store пакетов (через Display Catalog v7.0)
                if (downloadUrl.StartsWith("msstore:", StringComparison.OrdinalIgnoreCase) ||
                    downloadUrl.StartsWith("ms-windows-store:", StringComparison.OrdinalIgnoreCase))
                {
                    string productId = downloadUrl.Replace("msstore:", "", StringComparison.OrdinalIgnoreCase)
                                                  .Replace("ms-windows-store:", "", StringComparison.OrdinalIgnoreCase)
                                                  .Replace("//pdp/?productid=", "", StringComparison.OrdinalIgnoreCase)
                                                  .Trim('/', ' ');

                    string storeSize = await FetchMsStoreSizeAsync(productId);
                    if (!string.IsNullOrEmpty(storeSize)) return storeSize;
                }

                // 3. Для WinGet пакетов
                if (downloadUrl.StartsWith("winget:", StringComparison.OrdinalIgnoreCase))
                {
                    string appId = downloadUrl.Replace("winget:", "").Trim();

                    // Если это Store ID из 9-12 символов (например: 9N97ZCKPD60Q)
                    if (Regex.IsMatch(appId, @"^[A-Za-z0-9]{9,12}$"))
                    {
                        string storeSize = await FetchMsStoreSizeAsync(appId);
                        if (!string.IsNullOrEmpty(storeSize)) return storeSize;
                    }

                    // Иначе вытаскиваем прямую ссылку на инсталлер из winget show
                    string? installerUrl = await Task.Run(() =>
                    {
                        try
                        {
                            var psi = new ProcessStartInfo
                            {
                                FileName = "winget",
                                Arguments = $"show --id {appId}",
                                RedirectStandardOutput = true,
                                UseShellExecute = false,
                                CreateNoWindow = true,
                                StandardOutputEncoding = Encoding.UTF8
                            };
                            using var proc = Process.Start(psi);
                            string output = proc.StandardOutput.ReadToEnd();
                            proc.WaitForExit();

                            var match = Regex.Match(output, @"(?:Installer Url|URL установщика|Ссылка на установщик|InstallerUrl):\s*(https?://[^\r\n\s]+)", RegexOptions.IgnoreCase);
                            if (match.Success) return match.Groups[1].Value.Trim();
                        }
                        catch { }
                        return null;
                    });

                    if (!string.IsNullOrEmpty(installerUrl))
                    {
                        string sizeFromUrl = await FetchSizeFromHttpHeaderAsync(installerUrl);
                        if (!string.IsNullOrEmpty(sizeFromUrl)) return sizeFromUrl;
                    }
                }

                // 4. Для обычных прямых HTTP ссылок
                if (downloadUrl.StartsWith("http", StringComparison.OrdinalIgnoreCase))
                {
                    string size = await FetchSizeFromHttpHeaderAsync(downloadUrl);
                    if (!string.IsNullOrEmpty(size)) return size;
                }
            }
            catch { }

            return "Пакет WinGet";
        }

        // Запрос размера напрямую из каталога Microsoft Store
        private static async Task<string> FetchMsStoreSizeAsync(string productId)
        {
            try
            {
                // Запрашиваем полный каталог Display Catalog v7.0
                string apiUrl = $"https://displaycatalog.mp.microsoft.com/v7.0/products?bigIds={productId}&market=US&languages=en-us";
                using var response = await _client.GetAsync(apiUrl);
                if (response.IsSuccessStatusCode)
                {
                    string json = await response.Content.ReadAsStringAsync();
                    using var doc = JsonDocument.Parse(json);

                    // Ищем любое поле с размером в байтах по всему дереву ответа
                    long? sizeInBytes = FindSizeInJson(doc.RootElement);
                    if (sizeInBytes.HasValue && sizeInBytes.Value > 0)
                    {
                        return FormatBytes(sizeInBytes.Value);
                    }
                }
            }
            catch { }
            return string.Empty;
        }

        // Умный поиск любого поля с весом в байтах (MaxDownloadSizeInBytes, PackageSize и т.д.)
        private static long? FindSizeInJson(JsonElement element)
        {
            if (element.ValueKind == JsonValueKind.Object)
            {
                foreach (var prop in element.EnumerateObject())
                {
                    string name = prop.Name;
                    if (name.Contains("SizeInBytes", StringComparison.OrdinalIgnoreCase) ||
                        name.Equals("FileSize", StringComparison.OrdinalIgnoreCase) ||
                        name.Equals("PackageSize", StringComparison.OrdinalIgnoreCase) ||
                        name.Equals("DownloadSizeInBytes", StringComparison.OrdinalIgnoreCase))
                    {
                        if (prop.Value.ValueKind == JsonValueKind.Number && prop.Value.TryGetInt64(out long size) && size > 0)
                        {
                            return size;
                        }
                    }

                    var nested = FindSizeInJson(prop.Value);
                    if (nested.HasValue && nested.Value > 0)
                        return nested.Value;
                }
            }
            else if (element.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in element.EnumerateArray())
                {
                    var nested = FindSizeInJson(item);
                    if (nested.HasValue && nested.Value > 0)
                        return nested.Value;
                }
            }

            return null;
        }

        // Запрос заголовка Content-Length по HTTP
        private static async Task<string> FetchSizeFromHttpHeaderAsync(string url)
        {
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, url);
                using var response = await _client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);

                if (response.Content.Headers.ContentLength.HasValue && response.Content.Headers.ContentLength.Value > 0)
                {
                    return FormatBytes(response.Content.Headers.ContentLength.Value);
                }
            }
            catch { }
            return string.Empty;
        }

        private static string FormatBytes(long bytes)
        {
            if (bytes <= 0) return "Размер не указан";
            string[] sizes = { "Б", "КБ", "МБ", "ГБ", "ТБ" };
            double len = bytes;
            int order = 0;
            while (len >= 1024 && order < sizes.Length - 1)
            {
                order++;
                len /= 1024;
            }
            return $"{len:0.#} {sizes[order]}";
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