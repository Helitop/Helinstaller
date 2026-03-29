using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
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
using System.Threading.Tasks;
using Wpf.Ui.Abstractions.Controls;

namespace Helinstaller.ViewModels.Pages
{
    public partial class AppPageViewmodel : ObservableObject, INavigationAware
    {
        [ObservableProperty] private string _title = "Загрузка...";
        [ObservableProperty] private string _description = string.Empty;
        [ObservableProperty] private string _iconPath = string.Empty;
        [ObservableProperty] private string _previewPath = string.Empty;
        [ObservableProperty] private string _downloadUrl = string.Empty;
        [ObservableProperty] private bool _isInstalling = false;
        [ObservableProperty] private bool _isInstalled = false;
        [ObservableProperty] private bool _isChecking = false;
        [ObservableProperty] private double _progressValue = 0;

        [RelayCommand]
        private async Task Check()
        {
            IsChecking = true;
            await AutoFillMetadata();
            // Используем быстрый метод вместо PowerShell
            IsInstalled = await CheckInstallViaRegistry(Title);
            IsChecking = false;
        }

        // БЫСТРЫЙ МЕТОД ПРОВЕРКИ УСТАНОВКИ (Через реестр)
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
            var task = new DownloadTask { Title = this.Title, AppName = this.Title, IconPath = this.IconPath };
            DownloadService.Instance.AddTask(task);

            IsInstalling = true;
            task.Status = "Подготовка...";

            try
            {
                if (Title == "Office")
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
                    task.Status = "Очередь Winget...";
                    task.IsIndeterminate = true;
                    string appId = DownloadUrl.Replace("winget:", "").Trim();
                    await InstallViaWinget(appId, task);
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
                IsInstalled = await CheckInstallViaRegistry(Title);
            }
            catch (Exception ex)
            {
                task.Status = "Ошибка";
                task.IsError = true;
                task.ErrorMessage = ex.Message;
                task.IsIndeterminate = false;
            }
            finally { IsInstalling = false; ProgressValue = 0; }
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
            if (tempFile.EndsWith(".exe"));
            if (tempFile.EndsWith(".msi"));
            
            using var p = Process.Start(psi);
            if (p != null) await p.WaitForExitAsync();
        }

        private async Task InstallViaWinget(string appId, DownloadTask task)
        {
            task.Status = "Установка через Winget...";
            string args = $"install --id {appId} --silent --accept-package-agreements --accept-source-agreements";
            ProcessStartInfo psi = new ProcessStartInfo { FileName = "winget", Arguments = args, UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true };
            using var process = Process.Start(psi);
            await process.WaitForExitAsync();
        }

        // --- Остальные методы (InitializeAsync, AutoFillMetadata, GitHub и т.д.) оставляешь как были ---
        public async Task InitializeAsync(string title, string desc, string icon, string preview, string url)
        {
            Title = title;
            Description = desc;
            DownloadUrl = url;
            IconPath = icon?.TrimStart('/', '\\');
            PreviewPath = preview?.TrimStart('/', '\\');
            IsInstalled = false;
            await AutoFillMetadata();
        }

        public async Task AutoFillMetadata()
        {
            bool hasLocalIcon = false;
            if (!string.IsNullOrEmpty(IconPath) && !IconPath.StartsWith("http"))
            {
                string fullPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, IconPath.TrimStart('/', '\\'));
                hasLocalIcon = File.Exists(fullPath);
            }
            if (!hasLocalIcon && (string.IsNullOrEmpty(IconPath) || IconPath.Contains("ADD_NEW")))
            {
                IsChecking = true;
                var data = await Helpers.MetadataService.GetMetadataAsync(DownloadUrl);
                if (string.IsNullOrEmpty(Description)) Description = data.Description;
                IconPath = data.IconUrl;
                IsChecking = false;
            }
        }

        private async Task<string?> GetGithubInstallerDownloadUrlAsync(string apiUrl)
        {
            var priorityExtensions = new[] { ".appinstaller", ".exe", ".msi", ".zip", ".rar" };
            try
            {
                using HttpClient client = new HttpClient();

                // ВАЖНО: GitHub ОЧЕНЬ не любит общие User-Agent. 
                // Поставь название своего проекта, так лимиты будут мягче.
                client.DefaultRequestHeaders.UserAgent.ParseAdd("Helinstaller-App-v1.0");

                var response = await client.GetAsync("https://api.github.com/repos/" + apiUrl.Trim('/') + "/releases/latest");

                // Проверяем на лимит запросов (403)
                if (response.StatusCode == System.Net.HttpStatusCode.Forbidden)
                {
                    Debug.WriteLine("GitHub API: Rate limit exceeded.");
                    // Вместо ошибки возвращаем "LIMIT", чтобы обработать это в Install()
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
            catch (Exception ex)
            {
                Debug.WriteLine($"GitHub API Error: {ex.Message}");
            }
            return null;
        }
        private async Task InstallViaStore(string storeUrl, DownloadTask task)
        {
            await Task.Run(() =>
            {
                // Просто запускаем ссылку через оболочку Windows, она сама откроет Магазин
                var psi = new ProcessStartInfo
                {
                    FileName = storeUrl,
                    UseShellExecute = true
                };
                Process.Start(psi);
            });

            // Даем пользователю время понять, что произошло
            await Task.Delay(2000);
        }
        public class GithubReleaseResponse { public List<GithubAsset> Assets { get; set; } }
        public class GithubAsset { public string Name { get; set; } [JsonPropertyName("browser_download_url")] public string BrowserDownloadUrl { get; set; } }

        private async Task InstallOffice() { /* Твой старый код офиса */ }
        public async Task OnNavigatedToAsync() { await Check(); }
        public Task OnNavigatedFromAsync() => Task.CompletedTask;
    }
}