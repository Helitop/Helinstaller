using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Helinstaller.Views.Windows;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using System.Windows.Controls;
using Wpf.Ui.Abstractions.Controls;
using Wpf.Ui.Controls;

// Предполагаем, что CustomMessageBox - это ваш кастомный класс для отображения сообщений
// Если его нет в вашем коде, вам нужно будет его добавить или заменить на стандартный MessageBox
// В этом коде я оставил его как заглушку для компиляции

// Dummy class for CustomMessageBox - Replace with your actual implementation if needed
public class CustomMessageBox
{
    public CustomMessageBox(string message, string caption, System.Windows.MessageBoxButton button) { }
    public void ShowDialog() { }
}


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
        private bool _isInstalled = false;

        [ObservableProperty]
        private bool _isChecking = false;

        [ObservableProperty]
        private double _progressValue = 0;

        [ObservableProperty]
        private string _downloadUrl = string.Empty;


        [RelayCommand]
        private async Task Check()
        {
            IsChecking = true;
            await IsProgramInstalledByPowerShell(Title);
            IsChecking = false;
        }

        private async Task IsProgramInstalledByPowerShell(string programNamePart)
        {
            // 1. Предварительная проверка
            if (string.IsNullOrEmpty(programNamePart))
            {
                IsInstalled = false;
                return;
            }

            // Подготовка имени для поиска (экранирование не требуется, т.к. используется [regex]::new)
            string searchName = programNamePart.Trim().ToLowerInvariant();

            // 2. PowerShell-команда (Исправлена для точности поиска)
            string psCommand = $@"
$results = @();
# Создаем объект Regex для безопасного поиска (игнорируя регистр)
$searchRegex = [regex]::new('{searchName}', 'IgnoreCase');

# 1. Поиск UWP/Store приложений (Appx) для ВСЕХ пользователей
$results += Get-AppxPackage -AllUsers | 
    Where-Object {{$searchRegex.IsMatch($_.Name) -or $searchRegex.IsMatch($_.PackageFamilyName)}} | 
    Select-Object -ExpandProperty Name; 

# 2. Поиск классических приложений (Win32)
$results += Get-ItemProperty HKLM:\Software\Microsoft\Windows\CurrentVersion\Uninstall\*,
                        HKLM:\Software\Wow6432Node\Microsoft\Windows\CurrentVersion\Uninstall\*,
                        HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\* |
    # Фильтруем записи: 
    # - Должно быть DisplayName 
    # - Должен быть UninstallString (КРИТИЧЕСКИ ВАЖНО для исключения системных компонентов)
    # - Имя или Издатель должны соответствовать поисковому запросу
    Where-Object {{ 
        $_.DisplayName -ne $null -and $_.UninstallString -ne $null -and 
        ($searchRegex.IsMatch($_.DisplayName) -or $searchRegex.IsMatch($_.Publisher))
    }} |
    Select-Object -ExpandProperty DisplayName;

$results | Select-Object -Unique;
";

            // 3. Запуск процесса PowerShell
            try
            {
                // Экранирование двойных кавычек в команде PowerShell для передачи через командную строку
                string escapedPsCommand = psCommand.Replace("\"", "\"\"");

                ProcessStartInfo startInfo = new ProcessStartInfo
                {
                    FileName = "powershell.exe",
                    Arguments = $"-NoProfile -ExecutionPolicy Bypass -Command \"{escapedPsCommand}\"",
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                };

                using (Process process = Process.Start(startInfo))
                {
                    if (process == null)
                    {
                        IsInstalled = false;
                        return;
                    }

                    // Асинхронное чтение вывода и ожидание завершения процесса
                    Task<string> readOutputTask = process.StandardOutput.ReadToEndAsync();
                    Task waitForExitTask = process.WaitForExitAsync();
                    Task timeoutTask = Task.Delay(30000); // Таймаут 30 секунд

                    Task processCompleteTask = Task.WhenAll(readOutputTask, waitForExitTask);

                    // Ожидаем завершения процесса или таймаута
                    Task completedTask = await Task.WhenAny(processCompleteTask, timeoutTask);

                    // 4. Обработка таймаута
                    if (completedTask == timeoutTask)
                    {
                        try { process.Kill(); } catch { }
                        IsInstalled = false;
                        // Возможно, добавить лог таймаута
                        return;
                    }

                    // 5. Обработка результатов
                    await processCompleteTask;

                    string output = readOutputTask.Result.Trim();

                    // Приложение считается установленным, если вывод не пуст (т.е. найдено хотя бы одно совпадение)
                    IsInstalled = !string.IsNullOrEmpty(output);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Ошибка при проверке PowerShell: {ex.Message}");
                IsInstalled = false;
            }
        }

        [RelayCommand]
        private async Task Install()
        {
            IsInstalling = true;
            ProgressValue = 0;

            try
            {
                if (Title == "Office")
                {
                    await InstallOffice();
                    return;
                }

                if (string.IsNullOrWhiteSpace(DownloadUrl))
                    return;

                string urlToInstall = DownloadUrl;

                // GitHub: получаем реальный URL
                if (DownloadUrl.StartsWith("github:", StringComparison.OrdinalIgnoreCase))
                {
                    string api = DownloadUrl.Substring("github:".Length);
                    string? realInstallerUrl = await GetGithubInstallerDownloadUrlAsync(api);

                    if (realInstallerUrl == null)
                    {
                        new CustomMessageBox(
                            "Не удалось найти установочный файл (exe, msi, zip, rar) в релизах GitHub",
                            "Ошибка",
                            System.Windows.MessageBoxButton.OK
                        ).ShowDialog();
                        return;
                    }

                    urlToInstall = realInstallerUrl;
                }

                // Скачиваем и открываем любой файл
                await InstallFromUrlAsync(urlToInstall);
            }
            finally
            {
                IsInstalling = false;
                ProgressValue = 0;
            }
        }

        private async Task InstallFromUrlAsync(string url)
        {
            string tempFile = Path.Combine(Path.GetTempPath(), Path.GetFileName(url));

            try
            {
                using (HttpClient client = new HttpClient())
                using (HttpResponseMessage response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead))
                {
                    response.EnsureSuccessStatusCode();

                    long? totalBytes = response.Content.Headers.ContentLength;

                    using (Stream remoteStream = await response.Content.ReadAsStreamAsync())
                    using (FileStream localStream = new FileStream(tempFile, FileMode.Create, FileAccess.Write, FileShare.None))
                    {
                        byte[] buffer = new byte[81920];
                        long totalRead = 0;
                        int bytesRead;

                        while ((bytesRead = await remoteStream.ReadAsync(buffer, 0, buffer.Length)) > 0)
                        {
                            await localStream.WriteAsync(buffer, 0, bytesRead);
                            totalRead += bytesRead;

                            if (totalBytes.HasValue)
                                ProgressValue = (double)totalRead / totalBytes.Value * 100.0;
                            else
                                ProgressValue = (ProgressValue + 1) % 100;
                        }

                        await localStream.FlushAsync();
                    }
                }

                await EnsureFileUnlockedAsync(tempFile);

                // Открываем файл любым приложением, которое Windows предложит
                Process.Start(new ProcessStartInfo
                {
                    FileName = tempFile,
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                new CustomMessageBox(
                    $"Ошибка при открытии файла:\n{ex.Message}",
                    "Ошибка",
                    System.Windows.MessageBoxButton.OK
                ).ShowDialog();
            }
        }

        private async Task EnsureFileUnlockedAsync(string filePath, int retries = 100, int delayMs = 50)
        {
            for (int i = 0; i < retries; i++)
            {
                try
                {
                    using (FileStream fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.None))
                        return;
                }
                catch
                {
                    await Task.Delay(delayMs);
                }
            }
        }

        public class GithubReleaseResponse
        {
            [JsonPropertyName("assets")]
            public List<GithubAsset> Assets { get; set; } = new();
        }

        public class GithubAsset
        {
            [JsonPropertyName("name")]
            public string Name { get; set; } = string.Empty;

            [JsonPropertyName("browser_download_url")]
            public string BrowserDownloadUrl { get; set; } = string.Empty;
        }

        /// <summary>
        /// Ищет URL для скачивания файла с расширением .exe, .msi, .zip или .rar в указанном порядке приоритета.
        /// </summary>
        /// <param name="apiUrl">URL GitHub API для получения информации о релизе.</param>
        /// <returns>URL для скачивания файла-установщика или null, если не найден.</returns>
        private async Task<string?> GetGithubInstallerDownloadUrlAsync(string apiUrl)
        {
            // Установка приоритета поиска расширений
            var priorityExtensions = new[] { ".exe", ".msi", ".zip", ".rar" };

            try
            {
                using HttpClient client = new HttpClient();
                client.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0");

                string json = await client.GetStringAsync(apiUrl);

                var root = JsonSerializer.Deserialize<GithubReleaseResponse>(json);

                if (root?.Assets == null || root.Assets.Count == 0)
                    return null;

                // Поиск файла по приоритетным расширениям
                foreach (var ext in priorityExtensions)
                {
                    var installerAsset = root.Assets
                        .FirstOrDefault(a => a.Name.EndsWith(ext, StringComparison.OrdinalIgnoreCase));

                    if (installerAsset != null)
                    {
                        return installerAsset.BrowserDownloadUrl;
                    }
                }

                return null; // Ничего не найдено
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"GitHub parsing error: {ex.Message}");
                return null;
            }
        }

        private async Task InstallOffice()
        {
            // 1. Показать окно конфигурации и получить выбор пользователя
            var configWindow = new OfficeConfigWindow(); // Вы должны создать этот класс Window
            if (configWindow.ShowDialog() != true)
            {
                // Пользователь отменил установку
                return;
            }

            // Получаем выбранную конфигурацию из окна
            OfficeConfiguration config = configWindow.Configuration;

            string folderPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Office");
            string xmlFilePath = Path.Combine(folderPath, "Configuration.xml");

            try
            {
                // 2. Генерация и сохранение Configuration.xml
                if (!Directory.Exists(folderPath))
                {
                    Directory.CreateDirectory(folderPath);
                }

                string xmlContent = ConfigurationGenerator.GenerateXml(config);
                await File.WriteAllTextAsync(xmlFilePath, xmlContent, Encoding.UTF8);

                // 3. Запуск команды установки
                // Ваша ОБЯЗАТЕЛЬНАЯ команда: reg add ... && setup.exe /configure Configuration.xml
                string command =
                     "reg add \"HKCU\\Software\\Microsoft\\Office\\16.0\\Common\\ExperimentConfigs\\Ecs\" /v \"CountryCode\" /t REG_SZ /d \"std::wstring|US\" /f && setup.exe /configure Configuration.xml";

                ProcessStartInfo startInfo = new ProcessStartInfo
                {
                    // WorkingDirectory - путь, где находится setup.exe и Configuration.xml
                    WorkingDirectory = folderPath,
                    FileName = "cmd.exe",
                    Arguments = "/C " + command,
                    // Для установки Office через ODT /configure лучше запускать с
                    // UseShellExecute = true, Verb = "runas", чтобы получить права администратора
                    // Однако, если ваше приложение уже запущено с правами администратора,
                    // можно использовать ваш текущий подход:
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                using (Process process = Process.Start(startInfo))
                {
                    // Чтение вывода асинхронно
                    string output = await process.StandardOutput.ReadToEndAsync();
                    string error = await process.StandardError.ReadToEndAsync();

                    await process.WaitForExitAsync();

                    if (process.ExitCode != 0)
                    {
                        // Ошибка установки или выполнения команды reg add
                        new CustomMessageBox(
                            $"Установка завершилась с ошибкой (Код {process.ExitCode}).\nВывод ошибки:\n{error}\nВывод консоли:\n{output}",
                            "Ошибка установки Office",
                            System.Windows.MessageBoxButton.OK).ShowDialog();
                    }
                    else if (!string.IsNullOrWhiteSpace(error))
                    {
                        // Иногда ошибки могут быть в StandardError даже при ExitCode 0
                        new CustomMessageBox(
                            $"Команда выполнена, но обнаружены предупреждения/ошибки:\n{error}",
                            "Предупреждение установки Office",
                            System.Windows.MessageBoxButton.OK).ShowDialog();
                    }
                    else
                    {
                        new CustomMessageBox(
                           "Установка Office запущена успешно (или завершена, если она была скрытой).",
                           "Успех",
                           System.Windows.MessageBoxButton.OK).ShowDialog();
                    }
                }
            }
            catch (Exception ex)
            {
                new CustomMessageBox(
                    $"Произошла критическая ошибка: {ex.Message}",
                    "Ошибка установки Office",
                    System.Windows.MessageBoxButton.OK).ShowDialog();
            }
        }

        public async Task OnNavigatedToAsync() { }

        public Task OnNavigatedFromAsync() => Task.CompletedTask;
    }
}