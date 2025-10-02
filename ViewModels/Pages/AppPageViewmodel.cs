using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using Wpf.Ui.Abstractions.Controls;


namespace Helinstaller.ViewModels.Pages
{
    public partial class AppPageViewmodel : ObservableObject, INavigationAware
    {
        [ObservableProperty]
        private string _title = string.Empty;

        [ObservableProperty]
        private string _description = string.Empty;

        [ObservableProperty]
        private string _iconPath = string.Empty;

        [ObservableProperty]
        private string _previewPath = string.Empty;

        [ObservableProperty]
        private bool _isInstalling = false;

        [ObservableProperty]
        private double _progressValue = 0;

        [RelayCommand]
        private async Task Install()
        {
            IsInstalling = true;
            ProgressValue = 0;
            try
            {
                switch (Title)
                {
                    case "WinRAR":
                        await InstallWinRAR();
                        break;
                    case "Discord":
                        await InstallDiscord();
                        break;
                    case "Steam":
                        await InstallSteam();
                        break;
                    case "µTorrent":
                        await InstallTorrent();
                        break;
                    case "Zapret":
                        await InstallZapret();
                        break;
                    case "ExplorerPatcher":
                        await InstallEP();
                        break;
                    case "PowerToys":
                        await InstallPT();
                        break;
                    case "Meridius":
                        await InstallMeridius();
                        break;
                    case "TranslucentTB":
                        await InstallTB();
                        break;
                    case "Office":
                        await InstallOffice();
                        break;
                    case "Nvidia App":
                        await InstallNvidia();
                        break;
                    case "AMD Adrenalin":
                        await InstallAMD();
                        break;
                }
            }
            finally
            {
                IsInstalling = false;
                ProgressValue = 0;
            }
        }

        public Task OnNavigatedToAsync() => Task.CompletedTask;
        public Task OnNavigatedFromAsync() => Task.CompletedTask;

        private async Task InstallWinRAR()
        {
            IsInstalling = true;
            ProgressValue = 0;
            string downloadUrl = "https://www.win-rar.com/fileadmin/winrar-versions/winrar/winrar-x64-624ru.exe";
            string tempFilePath = Path.Combine(Path.GetTempPath(), "WinRAR_Installer.exe");

            try
            {
                using (HttpClient client = new HttpClient())
                {
                    using (HttpResponseMessage response = await client.GetAsync(downloadUrl, HttpCompletionOption.ResponseHeadersRead))
                    {
                        response.EnsureSuccessStatusCode();
                        long? totalBytes = response.Content.Headers.ContentLength;

                        using (Stream contentStream = await response.Content.ReadAsStreamAsync())
                        using (FileStream fileStream = new FileStream(tempFilePath, FileMode.Create, FileAccess.Write, FileShare.None))
                        {
                            byte[] buffer = new byte[8192];
                            long bytesRead = 0;
                            int bytesReadThisChunk;

                            while ((bytesReadThisChunk = await contentStream.ReadAsync(buffer, 0, buffer.Length)) > 0)
                            {
                                await fileStream.WriteAsync(buffer, 0, bytesReadThisChunk);
                                bytesRead += bytesReadThisChunk;

                                if (totalBytes.HasValue)
                                {
                                    ProgressValue = (double)bytesRead / totalBytes.Value * 100;
                                }
                            }
                        }
                    }
                }
                Process.Start(new ProcessStartInfo(tempFilePath) { UseShellExecute = true });
            }
            catch
            {
                // Handle exceptions here
            }
            finally
            {
                IsInstalling = false;
                ProgressValue = 0;
            }
        }

        private async Task InstallDiscord()
        {
            IsInstalling = true;
            ProgressValue = 0;
            string downloadUrl = "https://discord.com/api/download?platform=win";
            string tempFilePath = Path.Combine(Path.GetTempPath(), "Discord_Installer.exe");

            try
            {
                using (HttpClient client = new HttpClient())
                {
                    using (HttpResponseMessage response = await client.GetAsync(downloadUrl, HttpCompletionOption.ResponseHeadersRead))
                    {
                        response.EnsureSuccessStatusCode();
                        long? totalBytes = response.Content.Headers.ContentLength;

                        using (Stream contentStream = await response.Content.ReadAsStreamAsync())
                        using (FileStream fileStream = new FileStream(tempFilePath, FileMode.Create, FileAccess.Write, FileShare.None))
                        {
                            byte[] buffer = new byte[8192];
                            long bytesRead = 0;
                            int bytesReadThisChunk;

                            while ((bytesReadThisChunk = await contentStream.ReadAsync(buffer, 0, buffer.Length)) > 0)
                            {
                                await fileStream.WriteAsync(buffer, 0, bytesReadThisChunk);
                                bytesRead += bytesReadThisChunk;

                                if (totalBytes.HasValue)
                                {
                                    ProgressValue = (double)bytesRead / totalBytes.Value * 100;
                                }
                            }
                        }
                    }
                }
                Process.Start(new ProcessStartInfo(tempFilePath) { UseShellExecute = true });
            }
            catch
            {
                // Handle exceptions here
            }
            finally
            {
                IsInstalling = false;
                ProgressValue = 0;
            }
        }

        private async Task InstallTorrent()
        {
            IsInstalling = true;
            ProgressValue = 0;
            string downloadUrl = "https://download-hr.utorrent.com/track/stable/endpoint/utorrent/os/riserollout?filename=utorrent_installer.exe";
            string tempFilePath = Path.Combine(Path.GetTempPath(), "utorrent_installer.exe");

            try
            {
                using (HttpClient client = new HttpClient())
                {
                    using (HttpResponseMessage response = await client.GetAsync(downloadUrl, HttpCompletionOption.ResponseHeadersRead))
                    {
                        response.EnsureSuccessStatusCode();
                        long? totalBytes = response.Content.Headers.ContentLength;

                        using (Stream contentStream = await response.Content.ReadAsStreamAsync())
                        using (FileStream fileStream = new FileStream(tempFilePath, FileMode.Create, FileAccess.Write, FileShare.None))
                        {
                            byte[] buffer = new byte[8192];
                            long bytesRead = 0;
                            int bytesReadThisChunk;

                            while ((bytesReadThisChunk = await contentStream.ReadAsync(buffer, 0, buffer.Length)) > 0)
                            {
                                await fileStream.WriteAsync(buffer, 0, bytesReadThisChunk);
                                bytesRead += bytesReadThisChunk;

                                if (totalBytes.HasValue)
                                {
                                    ProgressValue = (double)bytesRead / totalBytes.Value * 100;
                                }
                            }
                        }
                    }
                }
                Process.Start(new ProcessStartInfo(tempFilePath) { UseShellExecute = true });
            }
            catch
            {
                // Handle exceptions here
            }
            finally
            {
                IsInstalling = false;
                ProgressValue = 0;
            }
        }

        private async Task InstallSteam()
        {
            IsInstalling = true;
            ProgressValue = 0;
            string downloadUrl = "https://cdn.akamai.steamstatic.com/client/installer/SteamSetup.exe";
            string tempFilePath = Path.Combine(Path.GetTempPath(), "SteamSetup.exe");

            try
            {
                using (HttpClient client = new HttpClient())
                {
                    using (HttpResponseMessage response = await client.GetAsync(downloadUrl, HttpCompletionOption.ResponseHeadersRead))
                    {
                        response.EnsureSuccessStatusCode();
                        long? totalBytes = response.Content.Headers.ContentLength;

                        using (Stream contentStream = await response.Content.ReadAsStreamAsync())
                        using (FileStream fileStream = new FileStream(tempFilePath, FileMode.Create, FileAccess.Write, FileShare.None))
                        {
                            byte[] buffer = new byte[8192];
                            long bytesRead = 0;
                            int bytesReadThisChunk;

                            while ((bytesReadThisChunk = await contentStream.ReadAsync(buffer, 0, buffer.Length)) > 0)
                            {
                                await fileStream.WriteAsync(buffer, 0, bytesReadThisChunk);
                                bytesRead += bytesReadThisChunk;

                                if (totalBytes.HasValue)
                                {
                                    ProgressValue = (double)bytesRead / totalBytes.Value * 100;
                                }
                            }
                        }
                    }
                }
                Process.Start(new ProcessStartInfo(tempFilePath) { UseShellExecute = true });
            }
            catch
            {
                // Handle exceptions here
            }
            finally
            {
                IsInstalling = false;
                ProgressValue = 0;
            }
        }

        private async Task InstallZapret()
        {
            IsInstalling = true;
            ProgressValue = 0;
            string tempZipPath = Path.Combine(Path.GetTempPath(), "zapret.zip");
            string installPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Zapret");

            try
            {
                using (HttpClient client = new HttpClient())
                {
                    client.DefaultRequestHeaders.UserAgent.ParseAdd("C# App");
                    string apiUrl = "https://api.github.com/repos/Flowseal/zapret-discord-youtube/releases/latest";
                    var apiResponse = await client.GetStringAsync(apiUrl);
                    var jsonDoc = JsonDocument.Parse(apiResponse);
                    string downloadUrl = null;

                    if (jsonDoc.RootElement.TryGetProperty("assets", out var assets))
                    {
                        foreach (var asset in assets.EnumerateArray())
                        {
                            if (asset.TryGetProperty("browser_download_url", out var urlElement))
                            {
                                string url = urlElement.GetString();
                                if (url.EndsWith(".zip"))
                                {
                                    downloadUrl = url;
                                    break;
                                }
                            }
                        }
                    }

                    if (string.IsNullOrEmpty(downloadUrl))
                        throw new Exception("Не удалось найти ссылку на zip-файл в API-ответе.");

                    using (HttpResponseMessage response = await client.GetAsync(downloadUrl, HttpCompletionOption.ResponseHeadersRead))
                    {
                        response.EnsureSuccessStatusCode();
                        long? totalBytes = response.Content.Headers.ContentLength;

                        using (Stream contentStream = await response.Content.ReadAsStreamAsync())
                        using (FileStream fileStream = new FileStream(tempZipPath, FileMode.Create, FileAccess.Write, FileShare.None))
                        {
                            byte[] buffer = new byte[8192];
                            long bytesRead = 0;
                            int read;

                            while ((read = await contentStream.ReadAsync(buffer, 0, buffer.Length)) > 0)
                            {
                                await fileStream.WriteAsync(buffer, 0, read);
                                bytesRead += read;

                                if (totalBytes.HasValue)
                                {
                                    ProgressValue = (double)bytesRead / totalBytes.Value * 100;
                                }
                            }
                        }
                    }
                }

                if (Directory.Exists(installPath))
                    Directory.Delete(installPath, true);
                System.IO.Compression.ZipFile.ExtractToDirectory(tempZipPath, installPath);

                string[] batFiles = Directory.GetFiles(installPath, "service.bat", SearchOption.AllDirectories);
                if (batFiles.Length == 0)
                    throw new Exception("service.bat не найден в архиве.");

                Process.Start(new ProcessStartInfo(batFiles[0]) { UseShellExecute = true });
            }
            catch
            {
                CustomMessageBox MB = new CustomMessageBox("Вероятно, Zapret уже запущен. Попробуй отключить сервис и попробовать заново.", "Ошибка установки Zapret", System.Windows.MessageBoxButton.OK);
                MB.ShowDialog();
            }
            finally
            {
                IsInstalling = false;
                ProgressValue = 0;
            }
        }

        private async Task InstallEP()
        {
            IsInstalling = true;
            ProgressValue = 0;
            string tempFilePath = Path.Combine(Path.GetTempPath(), "ep_setup.exe");

            try
            {
                using (HttpClient client = new HttpClient())
                {
                    client.DefaultRequestHeaders.UserAgent.ParseAdd("C# App");
                    string apiUrl = "https://api.github.com/repos/valinet/ExplorerPatcher/releases/latest";
                    var apiResponse = await client.GetStringAsync(apiUrl);
                    var jsonDoc = JsonDocument.Parse(apiResponse);
                    string downloadUrl = null;

                    if (jsonDoc.RootElement.TryGetProperty("assets", out var assets))
                    {
                        foreach (var asset in assets.EnumerateArray())
                        {
                            if (asset.TryGetProperty("browser_download_url", out var urlElement))
                            {
                                string url = urlElement.GetString();
                                if (url.EndsWith(".exe"))
                                {
                                    downloadUrl = url;
                                    break;
                                }
                            }
                        }
                    }

                    if (string.IsNullOrEmpty(downloadUrl))
                        throw new Exception("Не удалось найти ссылку на exe-файл в API-ответе.");

                    using (HttpResponseMessage response = await client.GetAsync(downloadUrl, HttpCompletionOption.ResponseHeadersRead))
                    {
                        response.EnsureSuccessStatusCode();
                        long? totalBytes = response.Content.Headers.ContentLength;

                        using (Stream contentStream = await response.Content.ReadAsStreamAsync())
                        using (FileStream fileStream = new FileStream(tempFilePath, FileMode.Create, FileAccess.Write, FileShare.None))
                        {
                            byte[] buffer = new byte[8192];
                            long bytesRead = 0;
                            int read;

                            while ((read = await contentStream.ReadAsync(buffer, 0, buffer.Length)) > 0)
                            {
                                await fileStream.WriteAsync(buffer, 0, read);
                                bytesRead += read;

                                if (totalBytes.HasValue)
                                {
                                    ProgressValue = (double)bytesRead / totalBytes.Value * 100;
                                }
                            }
                        }
                    }
                }
                Process.Start(new ProcessStartInfo(tempFilePath) { UseShellExecute = true });
            }
            catch
            {
                // Handle exceptions here
            }
            finally
            {
                IsInstalling = false;
                ProgressValue = 0;
            }
        }

        private async Task InstallPT()
        {
            IsInstalling = true;
            ProgressValue = 0;
            string tempFilePath = Path.Combine(Path.GetTempPath(), "pt_setup.exe");

            try
            {
                using (HttpClient client = new HttpClient())
                {
                    client.DefaultRequestHeaders.UserAgent.ParseAdd("C# App");
                    string apiUrl = "https://api.github.com/repos/microsoft/PowerToys/releases/latest";
                    var apiResponse = await client.GetStringAsync(apiUrl);
                    var jsonDoc = JsonDocument.Parse(apiResponse);
                    string downloadUrl = null;

                    if (jsonDoc.RootElement.TryGetProperty("assets", out var assets))
                    {
                        foreach (var asset in assets.EnumerateArray())
                        {
                            if (asset.TryGetProperty("browser_download_url", out var urlElement))
                            {
                                string url = urlElement.GetString();
                                if (url.EndsWith("x64.exe"))
                                {
                                    downloadUrl = url;
                                    break;
                                }
                            }
                        }
                    }

                    if (string.IsNullOrEmpty(downloadUrl))
                        throw new Exception("Не удалось найти ссылку на файл в API-ответе.");

                    using (HttpResponseMessage response = await client.GetAsync(downloadUrl, HttpCompletionOption.ResponseHeadersRead))
                    {
                        response.EnsureSuccessStatusCode();
                        long? totalBytes = response.Content.Headers.ContentLength;

                        using (Stream contentStream = await response.Content.ReadAsStreamAsync())
                        using (FileStream fileStream = new FileStream(tempFilePath, FileMode.Create, FileAccess.Write, FileShare.None))
                        {
                            byte[] buffer = new byte[8192];
                            long bytesRead = 0;
                            int read;

                            while ((read = await contentStream.ReadAsync(buffer, 0, buffer.Length)) > 0)
                            {
                                await fileStream.WriteAsync(buffer, 0, read);
                                bytesRead += read;

                                if (totalBytes.HasValue)
                                {
                                    ProgressValue = (double)bytesRead / totalBytes.Value * 100;
                                }
                            }
                        }
                    }
                }
                Process.Start(new ProcessStartInfo(tempFilePath) { UseShellExecute = true });
            }
            catch
            {
                // Handle exceptions here
            }
            finally
            {
                IsInstalling = false;
                ProgressValue = 0;
            }
        }

        private async Task InstallMeridius()
        {
            IsInstalling = true;
            ProgressValue = 0;
            string tempFilePath = Path.Combine(Path.GetTempPath(), "meridius_setup.exe");

            try
            {
                using (HttpClient client = new HttpClient())
                {
                    client.DefaultRequestHeaders.UserAgent.ParseAdd("C# App");
                    string apiUrl = "https://api.github.com/repos/PurpleHorrorRus/Meridius/releases/latest";
                    var apiResponse = await client.GetStringAsync(apiUrl);
                    var jsonDoc = JsonDocument.Parse(apiResponse);
                    string downloadUrl = null;

                    if (jsonDoc.RootElement.TryGetProperty("assets", out var assets))
                    {
                        foreach (var asset in assets.EnumerateArray())
                        {
                            if (asset.TryGetProperty("browser_download_url", out var urlElement))
                            {
                                string url = urlElement.GetString();
                                if (url.EndsWith(".exe"))
                                {
                                    downloadUrl = url;
                                    break;
                                }
                            }
                        }
                    }

                    if (string.IsNullOrEmpty(downloadUrl))
                        throw new Exception("Не удалось найти ссылку на файл в API-ответе.");

                    using (HttpResponseMessage response = await client.GetAsync(downloadUrl, HttpCompletionOption.ResponseHeadersRead))
                    {
                        response.EnsureSuccessStatusCode();
                        long? totalBytes = response.Content.Headers.ContentLength;

                        using (Stream contentStream = await response.Content.ReadAsStreamAsync())
                        using (FileStream fileStream = new FileStream(tempFilePath, FileMode.Create, FileAccess.Write, FileShare.None))
                        {
                            byte[] buffer = new byte[8192];
                            long bytesRead = 0;
                            int read;

                            while ((read = await contentStream.ReadAsync(buffer, 0, buffer.Length)) > 0)
                            {
                                await fileStream.WriteAsync(buffer, 0, read);
                                bytesRead += read;

                                if (totalBytes.HasValue)
                                {
                                    ProgressValue = (double)bytesRead / totalBytes.Value * 100;
                                }
                            }
                        }
                    }
                }
                Process.Start(new ProcessStartInfo(tempFilePath) { UseShellExecute = true });
            }
            catch
            {
                // Handle exceptions here
            }
            finally
            {
                IsInstalling = false;
                ProgressValue = 0;
            }
        }

        private async Task InstallTB()
        {
            IsInstalling = true;
            ProgressValue = 0;
            string tempFilePath = Path.Combine(Path.GetTempPath(), "tb_setup.appinstaller");

            try
            {
                using (HttpClient client = new HttpClient())
                {
                    client.DefaultRequestHeaders.UserAgent.ParseAdd("C# App");
                    string apiUrl = "https://api.github.com/repos/TranslucentTB/TranslucentTB/releases/latest";
                    var apiResponse = await client.GetStringAsync(apiUrl);
                    var jsonDoc = JsonDocument.Parse(apiResponse);
                    string downloadUrl = null;

                    if (jsonDoc.RootElement.TryGetProperty("assets", out var assets))
                    {
                        foreach (var asset in assets.EnumerateArray())
                        {
                            if (asset.TryGetProperty("browser_download_url", out var urlElement))
                            {
                                string url = urlElement.GetString();
                                if (url.EndsWith(".appinstaller"))
                                {
                                    downloadUrl = url;
                                    break;
                                }
                            }
                        }
                    }

                    if (string.IsNullOrEmpty(downloadUrl))
                        throw new Exception("Не удалось найти ссылку на файл в API-ответе.");

                    using (HttpResponseMessage response = await client.GetAsync(downloadUrl, HttpCompletionOption.ResponseHeadersRead))
                    {
                        response.EnsureSuccessStatusCode();
                        long? totalBytes = response.Content.Headers.ContentLength;

                        using (Stream contentStream = await response.Content.ReadAsStreamAsync())
                        using (FileStream fileStream = new FileStream(tempFilePath, FileMode.Create, FileAccess.Write, FileShare.None))
                        {
                            byte[] buffer = new byte[8192];
                            long bytesRead = 0;
                            int read;

                            while ((read = await contentStream.ReadAsync(buffer, 0, buffer.Length)) > 0)
                            {
                                await fileStream.WriteAsync(buffer, 0, read);
                                bytesRead += read;

                                if (totalBytes.HasValue)
                                {
                                    ProgressValue = (double)bytesRead / totalBytes.Value * 100;
                                }
                            }
                        }
                    }
                }
                Process.Start(new ProcessStartInfo(tempFilePath) { UseShellExecute = true });
            }
            catch
            {
                // Handle exceptions here
            }
            finally
            {
                IsInstalling = false;
                ProgressValue = 0;
            }
        }
        private async Task InstallOffice()
        {
            string folderPath = AppDomain.CurrentDomain.BaseDirectory + "\\Office";
            string command = "reg add \"HKCU\\Software\\Microsoft\\Office\\16.0\\Common\\ExperimentConfigs\\Ecs\" /v \"CountryCode\" /t REG_SZ /d \"std::wstring|US\" /f && setup.exe /configure Configuration.xml";
            try
            {
                // Создаём новый процесс.
                ProcessStartInfo startInfo = new ProcessStartInfo();
                startInfo.WorkingDirectory = folderPath;
                startInfo.FileName = "cmd.exe"; // Имя исполняемого файла.
                startInfo.Arguments = "/C " + command; // "/C" - это аргумент, который выполняет команду и затем закрывает cmd.
                startInfo.RedirectStandardOutput = true; // Перенаправляем стандартный вывод.
                startInfo.RedirectStandardError = true;  // Перенаправляем стандартный вывод ошибок.
                startInfo.UseShellExecute = false;       // Не использовать оболочку, чтобы можно было перенаправить вывод.
                startInfo.CreateNoWindow = true;         // Не создавать окно командной строки.

                // Запускаем процесс.
                using (Process process = Process.Start(startInfo))
                {
                    // Читаем весь вывод из процесса.
                    string output = process.StandardOutput.ReadToEnd();
                    string error = process.StandardError.ReadToEnd();

                    // Ждём завершения процесса.
                    process.WaitForExit();

                    if (!string.IsNullOrEmpty(error))
                    {
                        CustomMessageBox MB = new CustomMessageBox($"Ошибка выполнения команды: {error}", "Ошибка установки Office", System.Windows.MessageBoxButton.OK);
                        MB.ShowDialog();
                    }

                }
            }
            catch (Exception ex)
            {
                CustomMessageBox MB = new CustomMessageBox($"Произошла ошибка: {ex.Message}", "Ошибка установки Office", System.Windows.MessageBoxButton.OK);
                MB.ShowDialog();
            }
        }
        private async Task InstallNvidia()
        {
            IsInstalling = true;
            ProgressValue = 0;
            string downloadUrl = "https://us.download.nvidia.com/nvapp/client/11.0.5.266/NVIDIA_app_v11.0.5.266.exe";
            string tempFilePath = Path.Combine(Path.GetTempPath(), "nvidia.exe");

            try
            {
                using (HttpClient client = new HttpClient())
                {
                    using (HttpResponseMessage response = await client.GetAsync(downloadUrl, HttpCompletionOption.ResponseHeadersRead))
                    {
                        response.EnsureSuccessStatusCode();
                        long? totalBytes = response.Content.Headers.ContentLength;

                        using (Stream contentStream = await response.Content.ReadAsStreamAsync())
                        using (FileStream fileStream = new FileStream(tempFilePath, FileMode.Create, FileAccess.Write, FileShare.None))
                        {
                            byte[] buffer = new byte[8192];
                            long bytesRead = 0;
                            int bytesReadThisChunk;

                            while ((bytesReadThisChunk = await contentStream.ReadAsync(buffer, 0, buffer.Length)) > 0)
                            {
                                await fileStream.WriteAsync(buffer, 0, bytesReadThisChunk);
                                bytesRead += bytesReadThisChunk;

                                if (totalBytes.HasValue)
                                {
                                    ProgressValue = (double)bytesRead / totalBytes.Value * 100;
                                }
                            }
                        }
                    }
                }
                Process.Start(new ProcessStartInfo(tempFilePath) { UseShellExecute = true });
            }
            catch
            {
                // Handle exceptions here
            }
            finally
            {
                IsInstalling = false;
                ProgressValue = 0;
            }
        }
        private async Task InstallAMD()
        {
            IsInstalling = true;
            ProgressValue = 0;
            string downloadUrl = "https://drivers.amd.com/drivers/installer/25.10/whql/amd-software-adrenalin-edition-25.9.1-minimalsetup-250901_web.exe";
            string tempFilePath = Path.Combine(Path.GetTempPath(), "amd.exe");

            try
            {
                using (HttpClient client = new HttpClient())
                {
                    using (HttpResponseMessage response = await client.GetAsync(downloadUrl, HttpCompletionOption.ResponseHeadersRead))
                    {
                        response.EnsureSuccessStatusCode();
                        long? totalBytes = response.Content.Headers.ContentLength;

                        using (Stream contentStream = await response.Content.ReadAsStreamAsync())
                        using (FileStream fileStream = new FileStream(tempFilePath, FileMode.Create, FileAccess.Write, FileShare.None))
                        {
                            byte[] buffer = new byte[8192];
                            long bytesRead = 0;
                            int bytesReadThisChunk;

                            while ((bytesReadThisChunk = await contentStream.ReadAsync(buffer, 0, buffer.Length)) > 0)
                            {
                                await fileStream.WriteAsync(buffer, 0, bytesReadThisChunk);
                                bytesRead += bytesReadThisChunk;

                                if (totalBytes.HasValue)
                                {
                                    ProgressValue = (double)bytesRead / totalBytes.Value * 100;
                                }
                            }
                        }
                    }
                }
                Process.Start(new ProcessStartInfo(tempFilePath) { UseShellExecute = true });
            }
            catch
            {
                // Handle exceptions here
            }
            finally
            {
                IsInstalling = false;
                ProgressValue = 0;
            }
        }
    }
}