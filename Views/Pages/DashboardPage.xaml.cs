using Helinstaller.Helpers;
using Helinstaller.Models;
using Helinstaller.Services;
using Helinstaller.ViewModels.Pages;
using Helinstaller.Views.Windows;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using Wpf.Ui;
using Wpf.Ui.Abstractions.Controls;
using Wpf.Ui.Controls;
using Path = System.IO.Path;

namespace Helinstaller.Views.Pages
{
    public class AppCategoryGroup : ObservableObject
    {
        public string Name { get; set; } = string.Empty;
        public string Layout { get; set; } = "carousel";
        public bool IsGrid => Layout.Equals("grid", StringComparison.OrdinalIgnoreCase) || Layout.Equals("list", StringComparison.OrdinalIgnoreCase);
        public bool IsCarousel => !IsGrid;

        // Стрелки скролла видны ТОЛЬКО если элементов больше 3
        public bool CanScroll => IsCarousel && Items != null && Items.Count > 3;

        public ObservableCollection<AppItemViewModel> Items { get; set; } = new();
    }

    public partial class AppItemViewModel : ObservableObject
    {
        public string Name { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public string OriginalDescription { get; set; } = string.Empty;

        [ObservableProperty] private string _displayDescription = string.Empty;
        [ObservableProperty] private string _iconPath = string.Empty;
        [ObservableProperty] private string _downloadUrl = string.Empty;
        [ObservableProperty] private string _category = string.Empty;
        [ObservableProperty] private string _layout = "carousel";

        [ObservableProperty] private double _progress;
        [ObservableProperty] private bool _isIndeterminate;
        [ObservableProperty] private bool _isProgressVisible;
        [ObservableProperty] private bool _hasUpdate;

        public string SourceText
        {
            get
            {
                if (string.IsNullOrEmpty(DownloadUrl)) return "Local";
                if (DownloadUrl.StartsWith("github:", StringComparison.OrdinalIgnoreCase)) return "GitHub";
                if (DownloadUrl.StartsWith("msstore:", StringComparison.OrdinalIgnoreCase) || DownloadUrl.StartsWith("ms", StringComparison.OrdinalIgnoreCase)) return "Store";
                if (DownloadUrl.StartsWith("winget:", StringComparison.OrdinalIgnoreCase)) return "WinGet";
                if (DownloadUrl.StartsWith("http", StringComparison.OrdinalIgnoreCase)) return "Web";
                return "Local";
            }
        }
    }

    public partial class DashboardPage : INavigableView<DashboardViewModel>
    {
        // Свойство для кинетической анимации горизонтального скролла
        public static readonly DependencyProperty AnimatedHorizontalOffsetProperty =
            DependencyProperty.RegisterAttached("AnimatedHorizontalOffset", typeof(double), typeof(DashboardPage),
                new PropertyMetadata(0.0, (d, e) =>
                {
                    if (d is System.Windows.Controls.ScrollViewer sv)
                    {
                        sv.ScrollToHorizontalOffset((double)e.NewValue);
                    }
                }));

        private static void SmoothScrollTo(System.Windows.Controls.ScrollViewer sv, double targetOffset, double durationMs = 280)
        {
            if (sv == null) return;

            targetOffset = Math.Max(0, Math.Min(sv.ScrollableWidth, targetOffset));
            sv.SetValue(AnimatedHorizontalOffsetProperty, sv.HorizontalOffset);

            var anim = new DoubleAnimation
            {
                From = sv.HorizontalOffset,
                To = targetOffset,
                Duration = TimeSpan.FromMilliseconds(durationMs),
                EasingFunction = new QuarticEase { EasingMode = EasingMode.EaseOut }
            };

            sv.BeginAnimation(AnimatedHorizontalOffsetProperty, anim);
        }

        private const string JsonPath = "apps.json";
        private List<AppInfo>? _loadedApps;
        private bool _isAppsUiGenerated = false;

        public ObservableCollection<AppCategoryGroup> CategoryGroups { get; } = new();
        public DashboardViewModel ViewModel { get; }

        private readonly IWingetService _wingetService;
        private readonly INavigationService _navigationService;

        public DashboardPage(DashboardViewModel viewModel, INavigationService navigationService, IWingetService wingetService)
        {
            ViewModel = viewModel;
            _navigationService = navigationService;
            _wingetService = wingetService;
            DataContext = this;
            InitializeComponent();

            ViewModel.UpgradesRefreshed += () => Dispatcher.InvokeAsync(RefreshUpdateBadges);

            DownloadTaskManager.Instance.Tasks.CollectionChanged += Tasks_CollectionChanged;
            foreach (var task in DownloadTaskManager.Instance.Tasks)
            {
                task.PropertyChanged += Task_PropertyChanged;
            }
        }

        private void CarouselScrollLeft_Click(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement el && el.Tag is System.Windows.Controls.ScrollViewer scv)
            {
                SmoothScrollTo(scv, scv.HorizontalOffset - 242);
            }
        }

        private void CarouselScrollRight_Click(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement el && el.Tag is System.Windows.Controls.ScrollViewer scv)
            {
                SmoothScrollTo(scv, scv.HorizontalOffset + 242);
            }
        }

        private void CategoryScroller_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            if (sender is System.Windows.Controls.ScrollViewer scv && scv.ScrollableWidth > 0)
            {
                double step = e.Delta > 0 ? -242 : 242;
                SmoothScrollTo(scv, scv.HorizontalOffset + step);
                e.Handled = true;
            }
        }

        private void MainPageScroller_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            if (sender is System.Windows.Controls.ScrollViewer scv)
            {
                scv.ScrollToVerticalOffset(scv.VerticalOffset - (e.Delta * 0.6));
                e.Handled = true;
            }
        }

        private void Tasks_CollectionChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
        {
            if (e.NewItems != null)
            {
                foreach (DownloadTask task in e.NewItems)
                {
                    task.PropertyChanged += Task_PropertyChanged;
                    Dispatcher.InvokeAsync(() => UpdateAppItemForTask(task));
                }
            }
            if (e.OldItems != null)
            {
                foreach (DownloadTask task in e.OldItems)
                {
                    task.PropertyChanged -= Task_PropertyChanged;
                }
                Dispatcher.InvokeAsync(UpdateAllAppItemsProgress);
            }
        }

        private void Task_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (sender is DownloadTask task)
            {
                Dispatcher.InvokeAsync(() => UpdateAppItemForTask(task));
            }
        }

        private void UpdateAppItemForTask(DownloadTask task)
        {
            var matchingItems = CategoryGroups
                .SelectMany(g => g.Items)
                .Where(b => b.Title.Equals(task.AppName, StringComparison.OrdinalIgnoreCase) ||
                            b.Name.Equals(task.AppName, StringComparison.OrdinalIgnoreCase) ||
                            b.Title.Equals(task.Title, StringComparison.OrdinalIgnoreCase))
                .ToList();

            foreach (var item in matchingItems)
            {
                if (!task.IsCompleted && !task.IsError)
                {
                    item.IsProgressVisible = true;
                    item.IsIndeterminate = task.IsIndeterminate || task.Progress <= 0;
                    item.Progress = task.Progress;

                    string pctText = task.Progress > 0 ? $"{task.Progress:F0}% • " : "";
                    item.DisplayDescription = $"{pctText}{task.Status}";
                }
                else if (task.IsCompleted)
                {
                    item.IsProgressVisible = true;
                    item.IsIndeterminate = false;
                    item.Progress = 100;
                    item.DisplayDescription = "✅ Установлено";

                    _ = Task.Delay(3500).ContinueWith(_ =>
                    {
                        Dispatcher.InvokeAsync(() =>
                        {
                            var active = DownloadTaskManager.Instance.Tasks.FirstOrDefault(t =>
                                (t.AppName == item.Title || t.AppName == item.Name) && !t.IsCompleted && !t.IsError);
                            if (active == null)
                            {
                                item.IsProgressVisible = false;
                                item.DisplayDescription = item.OriginalDescription;
                            }
                        });
                    });
                }
                else if (task.IsError)
                {
                    item.IsProgressVisible = false;
                    item.DisplayDescription = "❌ Ошибка установки";
                }
            }
        }

        private void UpdateAllAppItemsProgress()
        {
            foreach (var item in CategoryGroups.SelectMany(g => g.Items))
            {
                var activeTask = DownloadTaskManager.Instance.Tasks.FirstOrDefault(t =>
                    (t.AppName == item.Title || t.AppName == item.Name) && !t.IsCompleted && !t.IsError);

                if (activeTask != null)
                {
                    UpdateAppItemForTask(activeTask);
                }
                else
                {
                    item.IsProgressVisible = false;
                    item.DisplayDescription = item.OriginalDescription;
                }
            }
        }

        private void RefreshUpdateBadges()
        {
            foreach (var item in CategoryGroups.SelectMany(g => g.Items))
            {
                string downloadId = "";
                if (!string.IsNullOrEmpty(item.DownloadUrl))
                {
                    downloadId = item.DownloadUrl.Replace("winget:", "", StringComparison.OrdinalIgnoreCase)
                                                 .Replace("msstore:", "", StringComparison.OrdinalIgnoreCase).Trim();
                }

                item.HasUpdate = ViewModel.UpgradablePackageIds.Contains(item.Title) ||
                                 ViewModel.UpgradablePackageIds.Contains(item.Name) ||
                                 (!string.IsNullOrEmpty(downloadId) && ViewModel.UpgradablePackageIds.Contains(downloadId));
            }
        }

        private async void Page_Loaded(object sender, RoutedEventArgs e)
        {
            BackgroundDimmer.Opacity = 0;

            if (!_isAppsUiGenerated)
            {
                _isAppsUiGenerated = true;

                await Task.Delay(100);
                await LoadAppsAsync();

                AppsSkeletonContainer.Visibility = Visibility.Collapsed;
                MainPageScroller.Visibility = Visibility.Visible;
            }
        }

        private async Task LoadAppsAsync()
        {
            try
            {
                CategoryGroups.Clear();

                if (!File.Exists(JsonPath))
                {
                    _loadedApps = new List<AppInfo>();
                    await File.WriteAllTextAsync(JsonPath, "[]");
                }
                else
                {
                    string json = await File.ReadAllTextAsync(JsonPath);
                    _loadedApps = JsonSerializer.Deserialize<List<AppInfo>>(json) ?? new List<AppInfo>();
                }

                var groups = _loadedApps.GroupBy(a => a.Category ?? "Разное").ToList();

                foreach (var group in groups)
                {
                    var items = group.ToList();
                    string layoutType = items.Any(a => string.Equals(a.Layout, "list", StringComparison.OrdinalIgnoreCase) ||
                                                       string.Equals(a.Layout, "grid", StringComparison.OrdinalIgnoreCase))
                        ? "grid"
                        : "carousel";

                    var categoryGroup = new AppCategoryGroup
                    {
                        Name = group.Key,
                        Layout = layoutType
                    };

                    foreach (var app in items)
                    {
                        var appVm = new AppItemViewModel
                        {
                            Name = app.Name,
                            Title = app.Title,
                            OriginalDescription = app.Description ?? string.Empty,
                            DisplayDescription = app.Description ?? string.Empty,
                            IconPath = app.IconPath ?? string.Empty,
                            DownloadUrl = app.DownloadUrl,
                            Category = app.Category ?? string.Empty,
                            Layout = layoutType
                        };

                        categoryGroup.Items.Add(appVm);

                        if (string.IsNullOrEmpty(app.IconPath) || !File.Exists(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, app.IconPath.TrimStart('/', '\\'))))
                        {
                            _ = LoadIconInBackgroundAsync(app, appVm);
                        }
                    }

                    CategoryGroups.Add(categoryGroup);
                }

                UpdateAllAppItemsProgress();
                RefreshUpdateBadges();
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show($"Ошибка загрузки каталога: {ex.Message}");
            }
        }

        private async Task LoadIconInBackgroundAsync(AppInfo app, AppItemViewModel appVm)
        {
            try
            {
                var meta = await MetadataService.GetMetadataAsync(app.DownloadUrl);
                if (!string.IsNullOrEmpty(meta.IconUrl))
                {
                    string? savedPath = await MetadataService.DownloadIconAsync(meta.IconUrl, app.Name);
                    if (!string.IsNullOrEmpty(savedPath))
                    {
                        app.IconPath = savedPath;
                        await Dispatcher.InvokeAsync(() => appVm.IconPath = savedPath);
                    }
                }
            }
            catch { }
        }

        private async void AppItem_Click(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement element && element.Tag is string tag)
            {
                var selectedApp = _loadedApps?.FirstOrDefault(a => a.Name == tag);
                if (selectedApp == null) return;

                UpdateOverlaySource(selectedApp);

                try
                {
                    string? pathForBg = selectedApp.IconPath;
                    var imgSource = new StringToImageSourceConverter().Convert(pathForBg, null, null, null) as ImageSource;
                    if (imgSource != null)
                    {
                        ((Storyboard)this.Resources["FadeInBg"]).Begin();
                        BackgroundDimmer.IsHitTestVisible = true;
                    }
                }
                catch { }

                LoadingOverlay.Visibility = Visibility.Visible;
                ((Storyboard)this.Resources["FadeInLoading"]).Begin();

                try
                {
                    await ViewModel.OnNavigateToApp(tag);

                    AppOverlayPanel.Visibility = Visibility.Visible;
                    AppOverlayPanel.Opacity = 1;

                    var bounceEase = new BackEase { Amplitude = 0.22, EasingMode = EasingMode.EaseOut };
                    var smoothEase = new CubicEase { EasingMode = EasingMode.EaseOut };

                    var slideIn = new ThicknessAnimation
                    {
                        From = new Thickness(0, -180, 0, 0),
                        To = new Thickness(0, 20, 0, 0),
                        Duration = TimeSpan.FromSeconds(0.35),
                        EasingFunction = bounceEase
                    };

                    var scaleYIn = new DoubleAnimation
                    {
                        From = 0.45,
                        To = 1.0,
                        Duration = TimeSpan.FromSeconds(0.38),
                        EasingFunction = bounceEase
                    };

                    var scaleXIn = new DoubleAnimation
                    {
                        From = 0.94,
                        To = 1.0,
                        Duration = TimeSpan.FromSeconds(0.32),
                        EasingFunction = smoothEase
                    };

                    AppOverlayPanel.BeginAnimation(MarginProperty, slideIn);
                    AppOverlayScale.BeginAnimation(ScaleTransform.ScaleYProperty, scaleYIn);
                    AppOverlayScale.BeginAnimation(ScaleTransform.ScaleXProperty, scaleXIn);
                }
                finally
                {
                    ((Storyboard)this.Resources["FadeInLoading"]).Stop();
                    LoadingOverlay.Opacity = 0;
                    LoadingOverlay.Visibility = Visibility.Collapsed;
                }
            }
        }

        private void CloseOverlay_Click(object sender, RoutedEventArgs e)
        {
            var smoothIn = new CubicEase { EasingMode = EasingMode.EaseIn };

            var slideOut = new ThicknessAnimation
            {
                To = new Thickness(0, -180, 0, 0),
                Duration = TimeSpan.FromSeconds(0.22),
                EasingFunction = smoothIn
            };

            var scaleYOut = new DoubleAnimation
            {
                To = 0.45,
                Duration = TimeSpan.FromSeconds(0.22),
                EasingFunction = smoothIn
            };

            var scaleXOut = new DoubleAnimation
            {
                To = 0.94,
                Duration = TimeSpan.FromSeconds(0.22),
                EasingFunction = smoothIn
            };

            slideOut.Completed += (s, ev) =>
            {
                AppOverlayPanel.Visibility = Visibility.Collapsed;
            };

            AppOverlayPanel.BeginAnimation(MarginProperty, slideOut);
            AppOverlayScale.BeginAnimation(ScaleTransform.ScaleYProperty, scaleYOut);
            AppOverlayScale.BeginAnimation(ScaleTransform.ScaleXProperty, scaleXOut);

            ((Storyboard)this.Resources["FadeOutBg"]).Begin();
            BackgroundDimmer.IsHitTestVisible = false;
        }

        private void BackgroundDimmer_MouseDown(object sender, MouseButtonEventArgs e)
        {
            CloseOverlay_Click(sender, e);
            BackgroundDimmer.IsHitTestVisible = false;
        }

        private void UpdateOverlaySource(AppInfo app)
        {
            string sourceText = "Local";
            if (!string.IsNullOrEmpty(app.DownloadUrl))
            {
                if (app.DownloadUrl.StartsWith("github:", StringComparison.OrdinalIgnoreCase)) sourceText = "GitHub";
                else if (app.DownloadUrl.StartsWith("msstore:", StringComparison.OrdinalIgnoreCase) || app.DownloadUrl.StartsWith("ms", StringComparison.OrdinalIgnoreCase)) sourceText = "MS Store";
                else if (app.DownloadUrl.StartsWith("winget:", StringComparison.OrdinalIgnoreCase)) sourceText = "WinGet";
                else if (app.DownloadUrl.StartsWith("http", StringComparison.OrdinalIgnoreCase)) sourceText = "Web";
            }

            OverlaySourceText.Text = sourceText;

            Color accentColor = SystemColors.AccentColor;
            Color badgeColor = Color.FromArgb(120, accentColor.R, accentColor.G, accentColor.B);
            OverlaySourceBadge.Background = new SolidColorBrush(badgeColor);
        }

        private void ShowHelpButton_Click(object sender, RoutedEventArgs e)
        {
            HelpDialog.ShowHelp(Window.GetWindow(this), "dashboard");
        }

        private async Task PerformWinGetSearch()
        {
            string query = SearchTextBox.Text.Trim();
            if (string.IsNullOrEmpty(query)) return;

            SearchTextBox.IsEnabled = false;
            WinGetSearchProgress.Visibility = Visibility.Visible;
            WinGetResultsList.ItemsSource = null;

            try
            {
                var searchResults = await _wingetService.SearchPackageAsync(query);

                if (searchResults == null || searchResults.Count == 0)
                {
                    WinGetResultsList.ItemsSource = new List<WinGetSearchResultViewModel>();
                    CapsuleToastService.Show($"По запросу «{query}» ничего не найдено", ToastType.Warning);
                    return;
                }

                var viewModels = searchResults.Select(result => new WinGetSearchResultViewModel
                {
                    Package = result,
                    Name = result.Name,
                    Id = result.Id,
                    Version = result.VersionString,
                    IsInstalled = false,
                    HasUpdate = false
                }).ToList();

                WinGetResultsList.ItemsSource = viewModels;

                _ = Task.Run(async () =>
                {
                    try
                    {
                        var installedList = await _wingetService.GetInstalledPackagesAsync();
                        if (installedList == null || installedList.Count == 0) return;

                        foreach (var vm in viewModels)
                        {
                            var installed = installedList.FirstOrDefault(p =>
                                p.Id.Equals(vm.Id, StringComparison.OrdinalIgnoreCase) ||
                                p.Name.Equals(vm.Name, StringComparison.OrdinalIgnoreCase));

                            if (installed != null)
                            {
                                Dispatcher.Invoke(() =>
                                {
                                    vm.IsInstalled = true;
                                    vm.HasUpdate = !string.IsNullOrEmpty(vm.Version) &&
                                                   !string.IsNullOrEmpty(installed.VersionString) &&
                                                   !vm.Version.Equals(installed.VersionString, StringComparison.OrdinalIgnoreCase);
                                });
                            }
                        }
                    }
                    catch { }
                });
            }
            catch (Exception ex)
            {
                Logger.LogError($"Dashboard: Сбой при поиске '{query}'", ex);
                CapsuleToastService.Show($"Ошибка поиска: {ex.Message}", ToastType.Error);
            }
            finally
            {
                WinGetSearchProgress.Visibility = Visibility.Collapsed;
                SearchTextBox.IsEnabled = true;
            }
        }

        private async Task ExecuteWinGetAction(object sender, string actionName, Func<string, Task<bool>> action)
        {
            if (sender is FrameworkElement el && el.DataContext is WinGetSearchResultViewModel vm)
            {
                if (vm.IsProcessing) return;
                vm.IsProcessing = true;

                var task = new DownloadTask
                {
                    Title = $"{actionName}: {vm.Name}",
                    AppName = vm.Name,
                    IconPath = "",
                    IsIndeterminate = true,
                    Status = $"Подготовка к {actionName.ToLower()}..."
                };

                DownloadTaskManager.Instance.AddTask(task);

                try
                {
                    task.Status = "Выполнение WinGet...";
                    bool success = await action(vm.Id);

                    if (success)
                    {
                        task.Status = "Завершено успешно";
                        task.Progress = 100;
                        task.IsCompleted = true;
                        task.IsIndeterminate = false;

                        CapsuleToastService.Show($"{actionName}: {vm.Name} — успешно!", ToastType.Success);
                    }
                    else
                    {
                        task.Status = "Ошибка WinGet";
                        task.IsError = true;
                        task.IsIndeterminate = false;
                        task.ErrorMessage = "WinGet вернул код ошибки.";

                        CapsuleToastService.Show($"Сбой ({actionName}): {vm.Name}", ToastType.Error);
                    }
                }
                catch (Exception ex)
                {
                    task.Status = "Критическая ошибка";
                    task.IsError = true;
                    task.IsIndeterminate = false;
                    task.ErrorMessage = ex.Message;

                    CapsuleToastService.Show($"Ошибка ({actionName}): {ex.Message}", ToastType.Error);
                }
                finally
                {
                    vm.IsProcessing = false;
                    await PerformWinGetSearch();
                }
            }
        }

        private async void InstallApp_Click(object sender, RoutedEventArgs e) =>
            await ExecuteWinGetAction(sender, "Установка", (id) => _wingetService.InstallPackageAsync(id, new Progress<string>(_ => { })));

        private async void UninstallApp_Click(object sender, RoutedEventArgs e) =>
            await ExecuteWinGetAction(sender, "Удаление", (id) => _wingetService.UninstallPackageAsync(id));

        private async void UpdateApp_Click(object sender, RoutedEventArgs e) =>
            await ExecuteWinGetAction(sender, "Обновление", (id) => _wingetService.UpgradePackageAsync(id));

        private async void SearchWinGetButton_Click(object sender, RoutedEventArgs e) => await PerformWinGetSearch();

        private async void SearchTextBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter) await PerformWinGetSearch();
        }
    }

    public class WinGetSearchResultViewModel : INotifyPropertyChanged
    {
        public WGetNET.WinGetPackage? Package { get; set; }

        private string _name = string.Empty;
        public string Name
        {
            get => _name;
            set { _name = value; OnPropertyChanged(); }
        }

        private string _id = string.Empty;
        public string Id
        {
            get => _id;
            set { _id = value; OnPropertyChanged(); }
        }

        private string _version = string.Empty;
        public string Version
        {
            get => _version;
            set { _version = value; OnPropertyChanged(); }
        }

        private bool _isInstalled;
        public bool IsInstalled
        {
            get => _isInstalled;
            set
            {
                _isInstalled = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(InstallBtnVis));
                OnPropertyChanged(nameof(UninstallBtnVis));
            }
        }

        private bool _hasUpdate;
        public bool HasUpdate
        {
            get => _hasUpdate;
            set
            {
                _hasUpdate = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(UpdateBtnVis));
            }
        }

        private bool _isProcessing;
        public bool IsProcessing
        {
            get => _isProcessing;
            set
            {
                _isProcessing = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(ActionButtonsVis));
                OnPropertyChanged(nameof(ProgressVis));
                OnPropertyChanged(nameof(InstallBtnVis));
                OnPropertyChanged(nameof(UninstallBtnVis));
                OnPropertyChanged(nameof(UpdateBtnVis));
            }
        }

        public Visibility ActionButtonsVis => IsProcessing ? Visibility.Collapsed : Visibility.Visible;
        public Visibility ProgressVis => IsProcessing ? Visibility.Visible : Visibility.Collapsed;

        public Visibility InstallBtnVis => (!IsInstalled && !IsProcessing) ? Visibility.Visible : Visibility.Collapsed;
        public Visibility UninstallBtnVis => (IsInstalled && !IsProcessing) ? Visibility.Visible : Visibility.Collapsed;
        public Visibility UpdateBtnVis => (HasUpdate && !IsProcessing) ? Visibility.Visible : Visibility.Collapsed;

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([System.Runtime.CompilerServices.CallerMemberName] string? name = null) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}