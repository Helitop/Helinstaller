using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Helinstaller.Helpers;
using Helinstaller.Models;
using Helinstaller.Services;
using Helinstaller.Views.Windows;
using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.ComponentModel; // Необходим для отслеживания изменения свойств задач
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using Wpf.Ui;
using Wpf.Ui.Abstractions.Controls;
using Wpf.Ui.Appearance;

namespace Helinstaller.ViewModels.Pages
{
    public partial class DashboardViewModel : ObservableObject, INavigationAware
    {
        private bool _isInitialized = false;
        private List<AppInfo> _applications = new List<AppInfo>();
        private readonly IWingetService _wingetService;
        private readonly INavigationService _navigationService;
        [ObservableProperty] private bool _isTelegramApp = false;
        [ObservableProperty] private bool _isProgressIndeterminate = true;

        // СОСТОЯНИЯ WINGET:
        [ObservableProperty] private bool _isWingetAvailable = true;
        [ObservableProperty] private bool _isWingetUnavailable = false;
        [ObservableProperty] private bool _isInstallingWinget = false;
        [ObservableProperty] private string _wingetInstallStatus = string.Empty;
        [ObservableProperty] private double _wingetInstallProgress = 0;
        [ObservableProperty] private string _appCategory = string.Empty;
        [ObservableProperty] private string _appSizeText = "Размер: Запрос...";
        [ObservableProperty] private string _appSourceDetails = string.Empty;
        // СОСТОЯНИЯ ОБНОВЛЕНИЙ:
        [ObservableProperty] private bool _isCheckingUpdates = false;
        [ObservableProperty] private bool _hasAnyUpdates = false;
        [ObservableProperty] private int _availableUpdatesCount = 0;
        [ObservableProperty] private string _upgradeAllButtonText = "Обновить всё";
        [ObservableProperty] private bool _hasUpdate = false; // Для открытой карточки

        // Хранилище ID и названий пакетов, требующих обновления
        public HashSet<string> UpgradablePackageIds { get; } = new(StringComparer.OrdinalIgnoreCase);

        // Событие для оповещения DashboardPage о необходимости подсветить плашки
        public event Action? UpgradesRefreshed;

        [RelayCommand]
        public async Task CheckUpdates()
        {
            if (IsCheckingUpdates || !IsWingetAvailable) return;
            IsCheckingUpdates = true;
            Logger.LogInfo("Проверка обновлений программ через WinGet...");

            try
            {
                var upgradableList = await _wingetService.GetUpgradablePackageIdsAsync();
                UpgradablePackageIds.Clear();

                foreach (var item in upgradableList)
                {
                    if (!string.IsNullOrWhiteSpace(item))
                        UpgradablePackageIds.Add(item);
                }

                int count = 0;
                foreach (var app in _applications)
                {
                    string id = app.DownloadUrl.Replace("winget:", "", StringComparison.OrdinalIgnoreCase)
                                               .Replace("msstore:", "", StringComparison.OrdinalIgnoreCase).Trim();

                    if (UpgradablePackageIds.Contains(id) ||
                        UpgradablePackageIds.Contains(app.Title) ||
                        UpgradablePackageIds.Contains(app.Name))
                    {
                        count++;
                    }
                }

                AvailableUpdatesCount = count;
                HasAnyUpdates = count > 0;
                UpgradeAllButtonText = $"Обновить всё ({count})";

                UpdateCurrentAppHasUpdate();
                UpgradesRefreshed?.Invoke();

                Logger.LogInfo($"Поиск обновлений завершен. Найдено для наших программ: {count}");

                // УВЕДОМЛЕНИЕ В ОСТРОВОК
                if (count > 0)
                {
                    CapsuleToastService.Show($"Доступно обновлений: {count}", ToastType.Info);
                }
                else
                {
                    CapsuleToastService.Show("Все программы актуальны!", ToastType.Success);
                }
            }
            catch (Exception ex)
            {
                Logger.LogError("Ошибка при поиске обновлений", ex);
                CapsuleToastService.Show($"Ошибка проверки обновлений: {ex.Message}", ToastType.Error);
            }
            finally
            {
                IsCheckingUpdates = false;
            }
        }

        private void UpdateCurrentAppHasUpdate()
        {
            if (string.IsNullOrEmpty(DownloadUrl))
            {
                HasUpdate = false;
                return;
            }

            string id = DownloadUrl.Replace("winget:", "", StringComparison.OrdinalIgnoreCase)
                                   .Replace("msstore:", "", StringComparison.OrdinalIgnoreCase).Trim();

            HasUpdate = UpgradablePackageIds.Contains(id) ||
                        UpgradablePackageIds.Contains(AppTitle);
        }


        [RelayCommand]
        private async Task UpgradeAll()
        {
            if (!HasAnyUpdates) return;

            var appsToUpgrade = _applications.Where(app =>
            {
                string id = app.DownloadUrl.Replace("winget:", "", StringComparison.OrdinalIgnoreCase)
                                           .Replace("msstore:", "", StringComparison.OrdinalIgnoreCase).Trim();
                return UpgradablePackageIds.Contains(id) || UpgradablePackageIds.Contains(app.Title);
            }).ToList();

            CapsuleToastService.Show($"Запущено обновление ({appsToUpgrade.Count} прогр.)...", ToastType.Info);

            foreach (var app in appsToUpgrade)
            {
                var active = DownloadTaskManager.Instance.Tasks.FirstOrDefault(t => t.AppName == app.Title && !t.IsCompleted && !t.IsError);
                if (active != null) continue;

                var task = new DownloadTask
                {
                    Title = $"Обновление: {app.Title}",
                    AppName = app.Title,
                    IconPath = app.IconPath ?? ""
                };

                DownloadTaskManager.Instance.AddTask(task);

                _ = DownloadTaskManager.Instance.EnqueueAsync(task, async () =>
                {
                    task.Status = "Обновление через WinGet...";
                    string appId = app.DownloadUrl.Replace("winget:", "", StringComparison.OrdinalIgnoreCase)
                                                  .Replace("msstore:", "", StringComparison.OrdinalIgnoreCase).Trim();
                    string? source = app.DownloadUrl.StartsWith("msstore:", StringComparison.OrdinalIgnoreCase) ? "msstore" : null;

                    var statusProgress = new Progress<string>(l => task.Status = l);
                    var percentProgress = new Progress<double>(p => task.Progress = p);

                    bool ok = await _wingetService.InstallPackageAsync(appId, statusProgress, percentProgress, force: false, source: source);
                    if (!ok) throw new Exception("Сбой при обновлении");

                    task.Progress = 100;
                    task.IsCompleted = true;
                    task.Status = "Обновлено";

                    UpgradablePackageIds.Remove(appId);
                    UpgradablePackageIds.Remove(app.Title);
                    AvailableUpdatesCount = Math.Max(0, AvailableUpdatesCount - 1);
                    HasAnyUpdates = AvailableUpdatesCount > 0;
                    UpgradeAllButtonText = $"Обновить всё ({AvailableUpdatesCount})";
                    UpdateCurrentAppHasUpdate();
                    UpgradesRefreshed?.Invoke();

                    // УВЕДОМЛЕНИЕ В ОСТРОВОК ОБ УСПЕШНОМ ОБНОВЛЕНИИ
                    CapsuleToastService.Show($"{app.Title} успешно обновлен!", ToastType.Success);
                });
            }
        }

        // РАЗРЕШЕНИЕ НА УСТАНОВКУ КОНКРЕТНОГО ПРИЛОЖЕНИЯ:
        [ObservableProperty] private bool _isInstallAllowed = true;

        partial void OnIsWingetAvailableChanged(bool value)
        {
            IsWingetUnavailable = !value;
            UpdateInstallAllowed();
        }

        private void UpdateInstallAllowed()
        {
            if (string.IsNullOrEmpty(DownloadUrl))
            {
                IsInstallAllowed = true;
                return;
            }

            bool requiresWinget = DownloadUrl.StartsWith("winget:", StringComparison.OrdinalIgnoreCase) ||
                                  DownloadUrl.StartsWith("msstore:", StringComparison.OrdinalIgnoreCase) ||
                                  DownloadUrl.StartsWith("ms-windows-store:", StringComparison.OrdinalIgnoreCase);

            IsInstallAllowed = !requiresWinget || IsWingetAvailable;
        }

        public DashboardViewModel(IWingetService wingetService, INavigationService navigationService)
        {
            _wingetService = wingetService;
            _navigationService = navigationService; // <--- Сохраняем сервис навигации

            DownloadTaskManager.Instance.Tasks.CollectionChanged += (s, e) =>
            {
                if (e.NewItems != null)
                {
                    foreach (DownloadTask task in e.NewItems)
                        task.PropertyChanged += Task_PropertyChanged;
                }
                if (e.OldItems != null)
                {
                    foreach (DownloadTask task in e.OldItems)
                        task.PropertyChanged -= Task_PropertyChanged;
                }
                UpdateCurrentAppStatus();
            };
        }
        [RelayCommand]
        private void OpenProxySearcher()
        {
            _navigationService.Navigate(typeof(Views.Pages.ProxyPage));
        }
        private void Task_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (sender is DownloadTask task && task.AppName == AppTitle)
            {
                Application.Current.Dispatcher.Invoke(() =>
                {
                    if (e.PropertyName == nameof(DownloadTask.Progress) || e.PropertyName == nameof(DownloadTask.IsIndeterminate))
                    {
                        ProgressValue = task.Progress;
                        IsProgressIndeterminate = task.IsIndeterminate || task.Progress <= 0;
                    }
                    else if (e.PropertyName == nameof(DownloadTask.IsCompleted) || e.PropertyName == nameof(DownloadTask.IsError))
                    {
                        UpdateCurrentAppStatus();
                        _ = CheckInstallViaRegistry(AppTitle).ContinueWith(t =>
                        {
                            IsInstalled = t.Result;
                        }, TaskScheduler.FromCurrentSynchronizationContext());
                    }
                });
            }
        }

        private void UpdateCurrentAppStatus()
        {
            var activeTask = DownloadTaskManager.Instance.Tasks
                .FirstOrDefault(t => t.AppName == AppTitle && !t.IsCompleted && !t.IsError);

            if (activeTask != null)
            {
                IsInstalling = true;
                ProgressValue = activeTask.Progress;
                IsProgressIndeterminate = activeTask.IsIndeterminate || activeTask.Progress <= 0;
            }
            else
            {
                IsInstalling = false;
                ProgressValue = 0;
                IsProgressIndeterminate = true;
            }
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
            IsWingetAvailable = await _wingetService.IsWingetAvailableAsync();
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
            Logger.LogInfo($"Навигация к карточке приложения: {appName}");
            var selectedApp = _applications.FirstOrDefault(a => a.Name == appName);
            if (selectedApp != null)
            {
                AppTitle = selectedApp.Title;
                AppDescription = selectedApp.Description ?? string.Empty;
                AppIconPath = selectedApp.IconPath ?? string.Empty;
                DownloadUrl = selectedApp.DownloadUrl;

                // Определяем, относится ли приложение к Telegram
                IsTelegramApp = selectedApp.Title.Contains("Telegram", StringComparison.OrdinalIgnoreCase) ||
                                selectedApp.Name.Contains("Telegram", StringComparison.OrdinalIgnoreCase) ||
                                selectedApp.Name.Contains("Unigram", StringComparison.OrdinalIgnoreCase);

                AppCategory = !string.IsNullOrWhiteSpace(selectedApp.Category) ? selectedApp.Category : "Утилиты";

                if (DownloadUrl.StartsWith("winget:", StringComparison.OrdinalIgnoreCase))
                    AppSourceDetails = $"WinGet: {DownloadUrl.Replace("winget:", "").Trim()}";
                else if (DownloadUrl.StartsWith("msstore:", StringComparison.OrdinalIgnoreCase))
                    AppSourceDetails = $"MS Store: {DownloadUrl.Replace("msstore:", "").Trim()}";
                else if (DownloadUrl.StartsWith("github:", StringComparison.OrdinalIgnoreCase))
                    AppSourceDetails = "GitHub Релиз";
                else
                    AppSourceDetails = "Прямая ссылка";

                AppSizeText = "Размер: ...";
                _ = MetadataService.GetInstallerSizeAsync(DownloadUrl).ContinueWith(t =>
                {
                    Application.Current.Dispatcher.Invoke(() => AppSizeText = t.Result);
                });

                IsChecking = false;
                IsForceInstall = false;

                UpdateInstallAllowed();
                UpdateCurrentAppStatus();
                UpdateCurrentAppHasUpdate();

                await CheckCommand.ExecuteAsync(null);
            }
        }

        [RelayCommand]
        private async Task Check()
        {
            IsChecking = true;
            await AutoFillMetadata();
            IsInstalled = await CheckInstallViaRegistry(AppTitle);
            UpdateInstallAllowed();
            IsChecking = false;
        }

        [RelayCommand]
        private async Task InstallWinget()
        {
            if (IsInstallingWinget) return;
            IsInstallingWinget = true;
            WingetInstallStatus = "Подготовка к установке...";
            WingetInstallProgress = 0;

            var progress = new Progress<double>(p => WingetInstallProgress = p);
            var status = new Progress<string>(s => WingetInstallStatus = s);

            bool success = await _wingetService.InstallOrUpdateWingetAsync(progress, status);
            await Task.Delay(1000);

            if (success)
            {
                IsWingetAvailable = await _wingetService.IsWingetAvailableAsync();
                Application.Current.Dispatcher.Invoke(() =>
                {
                    UpdateInstallAllowed();
                });

                if (IsWingetAvailable)
                {
                    WingetInstallStatus = "Служба Winget успешно установлена и готова к работе!";
                    CapsuleToastService.Show("Служба WinGet успешно установлена!", ToastType.Success);
                }
                else
                {
                    WingetInstallStatus = "Установка завершена. Если поиск закрыт, включите 'Установщик пакетов' в Параметры -> Псевдонимы выполнения приложений.";
                    CapsuleToastService.Show("Включите псевдоним WinGet в параметрах Windows", ToastType.Warning);
                }
            }
            else
            {
                WingetInstallStatus = "Не удалось завершить установку автоматически. Попробуйте перезапустить приложение.";
                CapsuleToastService.Show("Не удалось установить WinGet", ToastType.Error);
            }

            IsInstallingWinget = false;
        }

        private async Task<bool> CheckInstallViaRegistry(string appName)
        {
            return await Task.Run(() =>
            {
                if (string.IsNullOrWhiteSpace(appName)) return false;

                // Находим текущее приложение в списке для получения CheckPattern и DownloadUrl
                var currentApp = _applications.FirstOrDefault(a => a.Title == appName || a.Name == appName);

                var searchTerms = new List<string>();

                if (currentApp != null && !string.IsNullOrWhiteSpace(currentApp.CheckPattern))
                {
                    searchTerms.AddRange(currentApp.CheckPattern.Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
                }
                else
                {
                    searchTerms.Add(appName);
                    if (currentApp != null && !string.IsNullOrWhiteSpace(currentApp.Name))
                        searchTerms.Add(currentApp.Name);
                }

                // Добавляем Store ID из downloadUrl (если это msstore)
                if (currentApp != null && !string.IsNullOrWhiteSpace(currentApp.DownloadUrl))
                {
                    string cleanUrl = currentApp.DownloadUrl.Replace("msstore:", "", StringComparison.OrdinalIgnoreCase)
                                                           .Replace("winget:", "", StringComparison.OrdinalIgnoreCase).Trim();
                    if (cleanUrl.Length >= 9) searchTerms.Add(cleanUrl);
                }

                var lowerTerms = searchTerms.Select(s => s.ToLowerInvariant()).Distinct().ToList();

                // 1. Проверка классических программ (Win32 / .exe / .msi)
                string[] registryKeys = {
                    @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall",
                    @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall"
                };
                RegistryKey[] baseKeys = { Registry.LocalMachine, Registry.CurrentUser };

                foreach (var baseKey in baseKeys)
                {
                    foreach (var regPath in registryKeys)
                    {
                        using var key = baseKey.OpenSubKey(regPath);
                        if (key == null) continue;

                        foreach (string subKeyName in key.GetSubKeyNames())
                        {
                            using var appKey = key.OpenSubKey(subKeyName);
                            var displayName = appKey?.GetValue("DisplayName")?.ToString()?.ToLowerInvariant();
                            if (string.IsNullOrEmpty(displayName)) continue;

                            if (lowerTerms.Any(term => displayName.Contains(term)))
                                return true;
                        }
                    }
                }

                // 2. Проверка приложений Microsoft Store (UWP / AppX / MSIX)
                try
                {
                    using var appxKey = Registry.CurrentUser.OpenSubKey(@"Software\Classes\Local Settings\Software\Microsoft\Windows\CurrentVersion\AppModel\Repository\Packages");
                    if (appxKey != null)
                    {
                        foreach (string pkgName in appxKey.GetSubKeyNames())
                        {
                            string pkgLower = pkgName.ToLowerInvariant();
                            if (lowerTerms.Any(term => pkgLower.Contains(term)))
                                return true;
                        }
                    }
                }
                catch { }

                return false;
            });
        }

        [RelayCommand(AllowConcurrentExecutions = true)]
        private async Task Install()
        {
            if (IsChecking) return;
            var activeTask = DownloadTaskManager.Instance.Tasks
                .FirstOrDefault(t => t.AppName == AppTitle && !t.IsCompleted && !t.IsError);
            if (activeTask != null) return;
            Logger.LogInfo($"Запрошена установка для '{AppTitle}'. URL: {DownloadUrl}");
            IsInstalling = true;

            bool isForce = IsForceInstall;
            string capturedUrl = DownloadUrl;
            string capturedTitle = AppTitle;
            string capturedIcon = AppIconPath;

            string? officeXmlConfig = null;
            if (capturedTitle == "Office")
            {
                var configWindow = new OfficeConfigWindow();
                if (configWindow.ShowDialog() != true)
                {
                    IsInstalling = false;
                    return;
                }
                officeXmlConfig = configWindow.Configuration.GenerateXml();
            }

            var task = new DownloadTask { Title = capturedTitle, AppName = capturedTitle, IconPath = capturedIcon };
            DownloadTaskManager.Instance.AddTask(task);

            try
            {
                await DownloadTaskManager.Instance.EnqueueAsync(task, async () =>
                {
                    task.Status = "Подготовка...";

                    if (capturedTitle == "Office" && officeXmlConfig != null)
                    {
                        task.Status = "Установка Office...";
                        task.IsIndeterminate = true;
                        await RunOfficeSetupAsync(officeXmlConfig);
                    }
                    else if (capturedUrl.StartsWith("winget:", StringComparison.OrdinalIgnoreCase) ||
                             capturedUrl.StartsWith("msstore:", StringComparison.OrdinalIgnoreCase) ||
                             capturedUrl.StartsWith("ms-windows-store:", StringComparison.OrdinalIgnoreCase))
                    {
                        task.IsIndeterminate = true;
                        string? source = null;
                        string appId;

                        if (capturedUrl.StartsWith("msstore:", StringComparison.OrdinalIgnoreCase))
                        {
                            appId = capturedUrl.Substring("msstore:".Length).Trim();
                            source = "msstore";
                            task.Status = "Установка из Microsoft Store...";
                        }
                        else if (capturedUrl.StartsWith("ms-windows-store:", StringComparison.OrdinalIgnoreCase))
                        {
                            appId = ExtractStoreId(capturedUrl);
                            source = "msstore";
                            task.Status = "Установка из Microsoft Store...";
                        }
                        else
                        {
                            appId = capturedUrl.Substring("winget:".Length).Trim();
                            task.Status = "Установка через WinGet...";
                        }

                        var statusProgress = new Progress<string>(line =>
                        {
                            string cleanLine = Regex.Replace(line, @"[█░▄▀■►─\-|=+*#•·]|\[|\]", "").Trim();
                            if (!string.IsNullOrWhiteSpace(cleanLine) && cleanLine.Length > 3)
                            {
                                task.Status = cleanLine;
                            }
                        });

                        var percentProgress = new Progress<double>(pct =>
                        {
                            task.Progress = pct;
                            if (AppTitle == task.AppName)
                            {
                                this.ProgressValue = pct;
                            }
                        });

                        bool success = await _wingetService.InstallPackageAsync(appId, statusProgress, percentProgress, isForce, source);
                        if (!success) throw new Exception($"Установка '{appId}' через WinGet завершилась неудачно.");
                    }
                    else
                    {
                        string urlToInstall = capturedUrl;
                        if (capturedUrl.StartsWith("github:", StringComparison.OrdinalIgnoreCase))
                        {
                            task.Status = "Поиск релиза GitHub...";
                            urlToInstall = await GetGithubInstallerDownloadUrlAsync(capturedUrl.Replace("github:", "")) ?? "";
                        }

                        if (string.IsNullOrEmpty(urlToInstall)) throw new Exception("URL не найден");

                        task.Status = "Скачивание...";
                        await InstallFromUrlAsync(urlToInstall, task);
                    }

                    task.Status = "Установка завершена";
                    task.Progress = 100;
                    task.IsCompleted = true;

                    // УВЕДОМЛЕНИЕ В ОСТРОВОК
                    CapsuleToastService.Show($"{capturedTitle} успешно установлено!", ToastType.Success);
                });

                Logger.LogInfo($"Процесс установки '{capturedTitle}' успешно завершен.");
                IsInstalled = await CheckInstallViaRegistry(capturedTitle);
            }
            catch (Exception ex)
            {
                Logger.LogError($"Сбой при установке приложения '{capturedTitle}'", ex);
                task.Status = "Ошибка";
                task.IsError = true;
                task.ErrorMessage = ex.Message;
                task.IsIndeterminate = false;

                // УВЕДОМЛЕНИЕ В ОСТРОВОК ОБ ОШИБКЕ
                CapsuleToastService.Show($"Ошибка установки {capturedTitle}: {ex.Message}", ToastType.Error);
            }
            finally
            {
                UpdateCurrentAppStatus();
                ProgressValue = 0;
                IsForceInstall = false;
            }
        }

        // Вспомогательный метод парсинга ID из ссылок магазина
        private static string ExtractStoreId(string url)
        {
            if (url.Contains("productid=", StringComparison.OrdinalIgnoreCase))
            {
                var match = Regex.Match(url, @"productid=([a-zA-Z0-9]+)", RegexOptions.IgnoreCase);
                if (match.Success) return match.Groups[1].Value;
            }

            return url.Replace("ms-windows-store:", "", StringComparison.OrdinalIgnoreCase)
                      .Replace("//pdp/?", "", StringComparison.OrdinalIgnoreCase)
                      .Trim('/', ' ');
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
                            task.Progress = prog;
                            if (AppTitle == task.AppName)
                            {
                                this.ProgressValue = prog;
                            }
                        }
                    }
                }
            }
            task.Status = "Запуск установщика...";
            task.IsIndeterminate = true;
            var psi = new ProcessStartInfo { FileName = tempFile, UseShellExecute = true };
            using var p = Process.Start(psi);
            if (p != null) await p.WaitForExitAsync();
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

        public class GithubReleaseResponse { public List<GithubAsset> Assets { get; set; } }
        public class GithubAsset { public string Name { get; set; } [JsonPropertyName("browser_download_url")] public string BrowserDownloadUrl { get; set; } }

        private async Task RunOfficeSetupAsync(string xmlContent)
        {
            string officeDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Office");
            string setupPath = Path.Combine(officeDir, "setup.exe");
            string configPath = Path.Combine(officeDir, "Configuration.xml");

            if (!Directory.Exists(officeDir)) Directory.CreateDirectory(officeDir);
            if (!File.Exists(setupPath)) throw new Exception("Файл Office/setup.exe не найден. Поместите оригинальный установщик (ODT) в папку программы.");

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