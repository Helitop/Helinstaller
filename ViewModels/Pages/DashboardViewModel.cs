using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Helinstaller.Helpers;
using Helinstaller.Models;
using Helinstaller.Services;
using Helinstaller.Views.Windows;
using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Wpf.Ui.Abstractions.Controls;
using Wpf.Ui.Appearance;

namespace Helinstaller.ViewModels.Pages
{
    public partial class DashboardViewModel : ObservableObject, INavigationAware
    {
        private bool _isInitialized = false;
        private List<AppInfo> _applications = new List<AppInfo>();
        private readonly IWingetService _wingetService;

        public DashboardViewModel(IWingetService wingetService)
        {
            _wingetService = wingetService;
        }
        [ObservableProperty] private ApplicationTheme _currentTheme = ApplicationTheme.Unknown;
        [ObservableProperty] private string _appTitle = string.Empty;
        [ObservableProperty] private string _appDescription = string.Empty;
        [ObservableProperty] private string _appIconPath = string.Empty;
        [ObservableProperty] private bool _isInstalling = false;
        [ObservableProperty] private bool _isInstalled = false;
        [ObservableProperty] private bool _isChecking = false;
        [ObservableProperty] private double _progressValue = 0;
        [ObservableProperty] private string _downloadUrl = string.Empty;

        [ObservableProperty] private bool _isForceInstall = false;

        public async Task OnNavigatedToAsync()
        {
            if (!_isInitialized) await InitializeViewModel();
        }

        public Task OnNavigatedFromAsync() => Task.CompletedTask;

        private async Task InitializeViewModel()
        {
            if (_isInitialized) return;
            await LoadApplicationData("apps.json");
            _isInitialized = true;
        }

        private async Task LoadApplicationData(string filePath)
        {
            try
            {
                string jsonString = await File.ReadAllTextAsync(filePath);
                var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                var loadedApps = JsonSerializer.Deserialize<List<AppInfo>>(jsonString, options);
                if (loadedApps != null) _applications = loadedApps;
            }
            catch { }
        }

        public async Task OnNavigateToApp(string appName)
        {
            var selectedApp = _applications.FirstOrDefault(a => a.Name == appName);
            if (selectedApp != null)
            {
                AppTitle = selectedApp.Title;
                AppDescription = selectedApp.Description ?? string.Empty;
                AppIconPath = selectedApp.IconPath ?? string.Empty;
                DownloadUrl = selectedApp.DownloadUrl;

                IsInstalling = false;
                IsInstalled = false;
                ProgressValue = 0;
                IsForceInstall = false;

                await CheckCommand.ExecuteAsync(null);
            }
        }

        [RelayCommand]
        private async Task Check()
        {
            IsChecking = true;
            await AutoFillMetadata();
            IsInstalled = await CheckInstallViaRegistry(AppTitle);
            IsChecking = false;
        }

        private async Task<bool> CheckInstallViaRegistry(string appName)
        {
            return await Task.Run(() =>
            {
                if (string.IsNullOrWhiteSpace(appName)) return false;
                string searchName = appName.ToLowerInvariant();
                string[] registryKeys = {
                    @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall",
                    @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall",
                    @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall"
                };
                RegistryKey[] baseKeys = { Registry.LocalMachine, Registry.LocalMachine, Registry.CurrentUser };

                for (int i = 0; i < baseKeys.Length; i++)
                {
                    using var baseKey = baseKeys[i].OpenSubKey(registryKeys[i]);
                    if (baseKey == null) continue;
                    foreach (string subKeyName in baseKey.GetSubKeyNames())
                    {
                        using var appKey = baseKey.OpenSubKey(subKeyName);
                        var displayName = appKey?.GetValue("DisplayName") as string;
                        if (!string.IsNullOrEmpty(displayName) && displayName.ToLowerInvariant().Contains(searchName))
                            return true;
                    }
                }
                return false;
            });
        }

        [RelayCommand]
        private async Task Install()
        {
            var task = new DownloadTask { Title = this.AppTitle, AppName = this.AppTitle, IconPath = this.AppIconPath };
            DownloadTaskManager.Instance.AddTask(task);

            IsInstalling = true;
            task.Status = "Подготовка...";

            try
            {
                if (AppTitle == "Office")
                {
                    task.Status = "Настройка Office...";
                    task.IsIndeterminate = true;
                    await InstallOffice();
                }
                else if (DownloadUrl.StartsWith("ms-windows-store:", StringComparison.OrdinalIgnoreCase))
                {
                    task.Status = "Открытие в Microsoft Store...";
                    task.IsIndeterminate = true;
                    await InstallViaStore(DownloadUrl, task);
                }
                else if (DownloadUrl.StartsWith("winget:", StringComparison.OrdinalIgnoreCase))
                {
                    task.Status = "Установка через WinGet...";
                    task.IsIndeterminate = false; // Выключаем бесконечный режим для плавного заполнения
                    string appId = DownloadUrl.Replace("winget:", "").Trim();

                    // Объявляем текстовый прогресс
                    var statusProgress = new Progress<string>(line =>
                    {
                        string cleanLine = Regex.Replace(line, @"[█░▄▀■►─\-|=+*#•·]|\[|\]", "").Trim();
                        if (!string.IsNullOrWhiteSpace(cleanLine) && cleanLine.Length > 3)
                        {
                            task.Status = cleanLine;
                        }
                    });

                    // Объявляем числовой прогресс
                    var percentProgress = new Progress<double>(pct =>
                    {
                        task.Progress = pct;
                        this.ProgressValue = pct;
                    });

                    bool success = await _wingetService.InstallPackageAsync(appId, statusProgress, percentProgress, IsForceInstall);
                    if (!success) throw new Exception("Установка через WinGet завершилась неудачно.");
                }
                else
                {
                    string urlToInstall = DownloadUrl;
                    if (DownloadUrl.StartsWith("github:", StringComparison.OrdinalIgnoreCase))
                    {
                        task.Status = "Поиск релиза GitHub...";
                        urlToInstall = await GetGithubInstallerDownloadUrlAsync(DownloadUrl.Replace("github:", "")) ?? "";
                    }

                    if (string.IsNullOrEmpty(urlToInstall)) throw new Exception("URL не найден");

                    task.Status = "Скачивание...";
                    await InstallFromUrlAsync(urlToInstall, task);
                }

                task.Status = "Установка завершена";
                task.Progress = 100;
                task.IsCompleted = true;
                IsInstalled = await CheckInstallViaRegistry(AppTitle);
            }
            catch (Exception ex)
            {
                task.Status = "Ошибка";
                task.IsError = true;
                task.ErrorMessage = ex.Message;
                task.IsIndeterminate = false;
            }
            finally
            {
                IsInstalling = false;
                ProgressValue = 0;
                IsForceInstall = false; // Сбрасываем флаг здесь!
            }
        }

        private async Task InstallFromUrlAsync(string url, DownloadTask task)
        {
            string tempFile = Path.Combine(Path.GetTempPath(), Path.GetFileName(url));
            using (HttpClient client = new HttpClient())
            using (var response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead))
            {
                response.EnsureSuccessStatusCode();
                long? totalBytes = response.Content.Headers.ContentLength;
                using (var remoteStream = await response.Content.ReadAsStreamAsync())
                using (var localStream = new FileStream(tempFile, FileMode.Create, FileAccess.Write, FileShare.None))
                {
                    byte[] buffer = new byte[81920];
                    long totalRead = 0; int bytesRead;
                    while ((bytesRead = await remoteStream.ReadAsync(buffer, 0, buffer.Length)) > 0)
                    {
                        await localStream.WriteAsync(buffer, 0, bytesRead);
                        totalRead += bytesRead;
                        if (totalBytes.HasValue)
                        {
                            double prog = (double)totalRead / totalBytes.Value * 100.0;
                            task.Progress = prog; this.ProgressValue = prog;
                        }
                    }
                }
            }
            task.Status = "Запуск установщика...";
            task.IsIndeterminate = true;
            var psi = new ProcessStartInfo { FileName = tempFile, UseShellExecute = true };
            if (tempFile.EndsWith(".exe")) ;
            if (tempFile.EndsWith(".msi")) ;

            using var p = Process.Start(psi);
            if (p != null) await p.WaitForExitAsync();
        }

        private async Task InstallViaWinget(string appId, DownloadTask task)
        {
            task.Status = "Установка через Winget...";
            task.IsIndeterminate = true;
            string args = $"install --id {appId} --silent --accept-package-agreements --accept-source-agreements";

            ProcessStartInfo psi = new ProcessStartInfo
            {
                FileName = "winget",
                Arguments = args,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8
            };

            using (var process = Process.Start(psi))
            {
                if (process != null)
                {
                    string output = await process.StandardOutput.ReadToEndAsync();
                    string error = await process.StandardError.ReadToEndAsync();
                    await process.WaitForExitAsync();
                    string fullLog = string.IsNullOrEmpty(error) ? output : $"Output:\n{output}\n\nErrors:\n{error}";

                    var msg = new Wpf.Ui.Controls.MessageBox();
                    msg.Title = $"Результат Winget (Code: {process.ExitCode})";
                    msg.Content = new System.Windows.Controls.TextBox
                    {
                        Text = fullLog,
                        IsReadOnly = true,
                        TextWrapping = System.Windows.TextWrapping.Wrap,
                        MaxHeight = 400,
                        VerticalScrollBarVisibility = System.Windows.Controls.ScrollBarVisibility.Auto
                    };
                    await msg.ShowDialogAsync();

                    if (process.ExitCode != 0) throw new Exception($"Winget вернул ошибку: {process.ExitCode}");
                }
            }
        }

        private bool IsWingetInstalled()
        {
            try
            {
                using var process = new Process { StartInfo = new ProcessStartInfo { FileName = "winget", Arguments = "--version", UseShellExecute = false, CreateNoWindow = true } };
                process.Start(); process.WaitForExit(2000); return process.ExitCode == 0;
            }
            catch { return false; }
        }

        private async Task<bool> TryRepairWinget()
        {
            try
            {
                string psScript = "Add-AppxPackage -RegisterByFamilyName -MainPackage Microsoft.DesktopAppInstaller_8wekyb3d8bbwe";
                ProcessStartInfo psi = new ProcessStartInfo { FileName = "powershell", Arguments = $"-Command \"{psScript}\"", UseShellExecute = false, CreateNoWindow = true };
                using var p = Process.Start(psi); if (p != null) await p.WaitForExitAsync();
                return IsWingetInstalled();
            }
            catch { return false; }
        }

        public async Task AutoFillMetadata()
        {
            bool hasLocalIcon = false;
            if (!string.IsNullOrEmpty(AppIconPath) && !AppIconPath.StartsWith("http"))
            {
                string fullPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, AppIconPath.TrimStart('/', '\\'));
                hasLocalIcon = File.Exists(fullPath);
            }
            if (!hasLocalIcon && (string.IsNullOrEmpty(AppIconPath) || AppIconPath.Contains("ADD_NEW")))
            {
                IsChecking = true;
                var data = await Helpers.MetadataService.GetMetadataAsync(DownloadUrl);
                if (string.IsNullOrEmpty(AppDescription)) AppDescription = data.Description;
                AppIconPath = data.IconUrl;
                IsChecking = false;
            }
        }

        private async Task<string?> GetGithubInstallerDownloadUrlAsync(string apiUrl)
        {
            var priorityExtensions = new[] { ".appinstaller", ".exe", ".msi", ".zip", ".rar" };
            try
            {
                using HttpClient client = new HttpClient();
                client.DefaultRequestHeaders.UserAgent.ParseAdd("Helinstaller-App-v1.0");
                var response = await client.GetAsync("https://api.github.com/repos/" + apiUrl.Trim('/') + "/releases/latest");

                if (response.StatusCode == System.Net.HttpStatusCode.Forbidden)
                {
                    Debug.WriteLine("GitHub API: Rate limit exceeded.");
                    return "ERROR_LIMIT";
                }

                response.EnsureSuccessStatusCode();
                string json = await response.Content.ReadAsStringAsync();
                var root = JsonSerializer.Deserialize<GithubReleaseResponse>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                if (root?.Assets == null) return null;

                foreach (var ext in priorityExtensions)
                {
                    var asset = root.Assets.FirstOrDefault(a => a.Name.EndsWith(ext, StringComparison.OrdinalIgnoreCase));
                    if (asset != null) return asset.BrowserDownloadUrl;
                }
            }
            catch (Exception ex) { Debug.WriteLine($"GitHub API Error: {ex.Message}"); }
            return null;
        }

        private async Task InstallViaStore(string storeUrl, DownloadTask task)
        {
            await Task.Run(() =>
            {
                var psi = new ProcessStartInfo { FileName = storeUrl, UseShellExecute = true };
                Process.Start(psi);
            });
            await Task.Delay(2000);
        }

        public class GithubReleaseResponse { public List<GithubAsset> Assets { get; set; } }
        public class GithubAsset { public string Name { get; set; } [JsonPropertyName("browser_download_url")] public string BrowserDownloadUrl { get; set; } }

        private async Task InstallOffice()
        {
            var configWindow = new OfficeConfigWindow();
            if (configWindow.ShowDialog() != true) throw new Exception("Установка отменена пользователем");

            string officeDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Office");
            string setupPath = Path.Combine(officeDir, "setup.exe");
            string configPath = Path.Combine(officeDir, "Configuration.xml");

            if (!Directory.Exists(officeDir)) Directory.CreateDirectory(officeDir);
            if (!File.Exists(setupPath)) throw new Exception("Файл Office/setup.exe не найден. Поместите оригинальный установщик (ODT) в папку программы.");

            string xmlContent = configWindow.Configuration.GenerateXml();
            await File.WriteAllTextAsync(configPath, xmlContent, Encoding.UTF8);

            ProcessStartInfo psi = new ProcessStartInfo
            {
                FileName = setupPath,
                Arguments = $"/configure \"{configPath}\"",
                WorkingDirectory = officeDir,
                UseShellExecute = true,
                Verb = "runas",
                WindowStyle = ProcessWindowStyle.Hidden
            };

            try
            {
                using var process = Process.Start(psi);
                if (process != null) await process.WaitForExitAsync();
            }
            catch (System.ComponentModel.Win32Exception) { throw new Exception("Установка требует прав администратора."); }
        }
    }
}