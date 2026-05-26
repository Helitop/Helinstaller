using Helinstaller.ViewModels.Pages;
using System;
using Helinstaller.Models;
using Helinstaller.Services;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using WGetNET;
using Wpf.Ui;
using Wpf.Ui.Abstractions.Controls;
using Wpf.Ui.Controls;

using Image = Wpf.Ui.Controls.Image;
using MenuItem = Wpf.Ui.Controls.MenuItem;
using Path = System.IO.Path;

namespace Helinstaller.Views.Pages
{
    public partial class DashboardPage : INavigableView<DashboardViewModel>
    {
        private const string AddAppTag = "ADD_NEW_APP_TILE";
        private const string JsonPath = "apps.json";

        private List<AppInfo> _loadedApps;
        private bool _isEditMode = false;

        private const double TileW = 230;
        private const double TileH = 110;

        public DashboardViewModel ViewModel { get; }
        private readonly IWingetService _wingetService;
        private readonly INavigationService _navigationService;

        public DashboardPage(DashboardViewModel viewModel, INavigationService navigationService, IWingetService wingetService)
        {
            ViewModel = viewModel;
            _navigationService = navigationService;
            _wingetService = wingetService; // Внедряем единый сервис
            DataContext = this;
            InitializeComponent();
        }

        private void Page_Loaded(object sender, RoutedEventArgs e)
        {

            BackgroundDimmer.Opacity = 0;

            LoadAppsAndGenerateButtons();
        }

        private readonly WinGetPackageManager _packageManager = new WinGetPackageManager();

        private async Task PerformWinGetSearch()
        {
            string query = SearchTextBox.Text.Trim();
            if (string.IsNullOrEmpty(query)) return;

            SearchTextBox.IsEnabled = false;
            WinGetSearchProgress.Visibility = Visibility.Visible;
            WinGetResultsList.ItemsSource = null;

            try
            {
                var searchResults = await _packageManager.SearchPackageAsync(query);
                var installedPackages = await _packageManager.GetInstalledPackagesAsync();

                var viewModels = searchResults.Select(result =>
                {
                    var installed = installedPackages.FirstOrDefault(p => p.Id == result.Id);
                    return new WinGetSearchResultViewModel
                    {
                        Package = result,
                        IsInstalled = installed != null,
                        HasUpdate = installed != null && installed.Version != result.Version
                    };
                }).ToList();

                WinGetResultsList.ItemsSource = viewModels;
            }
            catch (System.Exception ex) { System.Diagnostics.Debug.WriteLine(ex.Message); }
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
                var task = new DownloadTask
                {
                    Title = $"{actionName}: {vm.Name}",
                    AppName = vm.Name,
                    IconPath = "",
                    IsIndeterminate = true,
                    Status = $"Подготовка к {actionName.ToLower()}..."
                };

                DownloadTaskManager.Instance.AddTask(task);
                vm.IsProcessing = true;

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
                    }
                    else
                    {
                        task.Status = "Ошибка WinGet";
                        task.IsError = true;
                        task.IsIndeterminate = false;
                        task.ErrorMessage = "WinGet вернул код ошибки. Возможно, требуется ручное вмешательство.";
                    }
                }
                catch (Exception ex)
                {
                    task.Status = "Критическая ошибка";
                    task.IsError = true;
                    task.IsIndeterminate = false;
                    task.ErrorMessage = ex.Message;
                }
                finally
                {
                    vm.IsProcessing = false;
                    await PerformWinGetSearch();
                }
            }
        }

        private async void InstallApp_Click(object sender, RoutedEventArgs e) =>
    await ExecuteWinGetAction(sender, "Установка", (id) => _wingetService.InstallPackageAsync(id, new Progress<string>(line =>
    {
        // Перенаправляем лог установки в статус задачи
        if (sender is FrameworkElement el && el.DataContext is WinGetSearchResultViewModel vm)
        {
            // Находим и обновляем текст прогресса на плитке поиска
            var cleanLine = System.Text.RegularExpressions.Regex.Replace(line, @"[█░▄▀■►─\-|=+*#•·]|\[|\]", "").Trim();
            if (cleanLine.Length > 3)
            {
                // Если в строке есть проценты, можно выводить их
                System.Diagnostics.Debug.WriteLine($"WinGet Progress: {cleanLine}");
            }
        }
    })));

        private async void UninstallApp_Click(object sender, RoutedEventArgs e) =>
            await ExecuteWinGetAction(sender, "Удаление", (id) => _packageManager.UninstallPackageAsync(id));

        private async void UpdateApp_Click(object sender, RoutedEventArgs e) =>
            await ExecuteWinGetAction(sender, "Обновление", (id) => _packageManager.UpgradePackageAsync(id));

        private async void SearchWinGetButton_Click(object sender, RoutedEventArgs e) => await PerformWinGetSearch();
        private async void SearchTextBox_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            if (e.Key == System.Windows.Input.Key.Enter) await PerformWinGetSearch();
        }

        private void UpdateOverlaySource(AppInfo app)
        {
            string sourceText = "Local";
            if (!string.IsNullOrEmpty(app.DownloadUrl))
            {
                if (app.DownloadUrl.StartsWith("github:", StringComparison.OrdinalIgnoreCase)) sourceText = "GitHub";
                else if (app.DownloadUrl.StartsWith("winget:", StringComparison.OrdinalIgnoreCase)) sourceText = "WinGet";
                else if (app.DownloadUrl.StartsWith("http", StringComparison.OrdinalIgnoreCase)) sourceText = "Web";
                else if (app.DownloadUrl.StartsWith("ms")) sourceText = "MS Store";
            }

            OverlaySourceText.Text = sourceText;

            Color accentColor = SystemColors.AccentColor;
            Color badgeColor = Color.FromArgb(120, accentColor.R, accentColor.G, accentColor.B);

            OverlaySourceBadge.Background = new SolidColorBrush(badgeColor);
        }
        private void LoadAppsAndGenerateButtons()
        {
            try
            {
                if (_isEditMode) return;
                AppsWrapPanel.Children.Clear();

                if (!File.Exists(JsonPath))
                {
                    _loadedApps = new List<AppInfo>();
                    File.WriteAllText(JsonPath, "[]");
                }
                else
                {
                    string json = File.ReadAllText(JsonPath);
                    _loadedApps = JsonSerializer.Deserialize<List<AppInfo>>(json) ?? new List<AppInfo>();
                }

                foreach (var app in _loadedApps) AppsWrapPanel.Children.Add(CreateAppTile(app));
            }
            catch (Exception ex) { System.Windows.MessageBox.Show($"Ошибка загрузки: {ex.Message}"); }
        }

        private FrameworkElement CreateAppTile(AppInfo app)
        {
            var anchor = new Wpf.Ui.Controls.Button
            {
                Width = TileW,
                Height = TileH,
                Margin = new Thickness(8),
                Appearance = ControlAppearance.Secondary,
                CornerRadius = new CornerRadius(12),
                Padding = new Thickness(14),
                HorizontalContentAlignment = HorizontalAlignment.Stretch,
                VerticalContentAlignment = VerticalAlignment.Stretch,
                Tag = app.Name,
                AllowDrop = true
            };
            anchor.Drop += Button_Drop;
            anchor.Click += TileButton_Click;

            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var iconBorder = new Border { Width = 48, Height = 48, CornerRadius = new CornerRadius(8), ClipToBounds = true, Margin = new Thickness(0, 0, 14, 0), VerticalAlignment = VerticalAlignment.Center };
            var icon = new Image { Stretch = Stretch.UniformToFill };
            RenderOptions.SetBitmapScalingMode(icon, BitmapScalingMode.HighQuality);
            iconBorder.Child = icon;
            Grid.SetColumn(iconBorder, 0);
            grid.Children.Add(iconBorder);

            var infoStack = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            var headerGrid = new Grid { Margin = new Thickness(0, 0, 0, 6) };
            headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var title = new Wpf.Ui.Controls.TextBlock { Text = app.Title, FontSize = 14, FontWeight = FontWeights.Bold, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center };
            Grid.SetColumn(title, 0); headerGrid.Children.Add(title);

            string sourceText = "Local";
            if (!string.IsNullOrEmpty(app.DownloadUrl))
            {
                if (app.DownloadUrl.StartsWith("github:", StringComparison.OrdinalIgnoreCase)) sourceText = "GitHub";
                else if (app.DownloadUrl.StartsWith("winget:", StringComparison.OrdinalIgnoreCase)) sourceText = "WinGet";
                else if (app.DownloadUrl.StartsWith("http")) sourceText = "Web";
                else if (app.DownloadUrl.StartsWith("ms")) sourceText = "MS Store";
            }
            Color accentColor = SystemColors.AccentColor;
            Color semiTransparentColor = Color.FromArgb(100, accentColor.R, accentColor.G, accentColor.B);

            var badge = new Border { Background = new SolidColorBrush(semiTransparentColor), CornerRadius = new CornerRadius(4), Padding = new Thickness(6, 1, 6, 1), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 0, 0) };
            badge.Child = new Wpf.Ui.Controls.TextBlock { Text = sourceText, FontSize = 9, FontWeight = FontWeights.SemiBold, Foreground = Brushes.White };
            Grid.SetColumn(badge, 1); headerGrid.Children.Add(badge);

            var desc = new Wpf.Ui.Controls.TextBlock { Text = app.Description, FontSize = 11, Opacity = 0.6, TextWrapping = TextWrapping.Wrap, MaxHeight = 36, LineHeight = 14, TextTrimming = TextTrimming.CharacterEllipsis };
            infoStack.Children.Add(headerGrid); infoStack.Children.Add(desc);
            Grid.SetColumn(infoStack, 1); grid.Children.Add(infoStack);

            anchor.Content = grid;

            if (!string.IsNullOrEmpty(app.IconPath) && File.Exists(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, app.IconPath.TrimStart('/', '\\'))))
                icon.Source = new Helpers.StringToImageSourceConverter().Convert(app.IconPath, null, null, null) as ImageSource;
            else
                _ = LoadIconInBackgroundAsync(app, icon);

            return anchor;
        }

        private async Task LoadIconInBackgroundAsync(AppInfo app, Image iconControl)
        {
            try
            {
                var meta = await Helpers.MetadataService.GetMetadataAsync(app.DownloadUrl);
                if (!string.IsNullOrEmpty(meta.IconUrl))
                {
                    string savedPath = await Helpers.MetadataService.DownloadIconAsync(meta.IconUrl, app.Name);
                    if (!string.IsNullOrEmpty(savedPath))
                    {
                        app.IconPath = savedPath;
                        SaveCurrentAppOrder();
                        Application.Current.Dispatcher.Invoke(() =>
                        {
                            iconControl.Source = new Helpers.StringToImageSourceConverter().Convert(savedPath, null, null, null) as ImageSource;
                            var fadeIn = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(500));
                            iconControl.BeginAnimation(UIElement.OpacityProperty, fadeIn);
                        });
                    }
                }
            }
            catch { }
        }

        private void Button_Drop(object sender, DragEventArgs e)
        {
            var targetButton = sender as Wpf.Ui.Controls.Button;
            var sourceTag = e.Data.GetData(typeof(string)) as string;

            if (targetButton != null && sourceTag != null && targetButton.Tag as string != sourceTag)
            {
                if (targetButton.Tag as string == AddAppTag) return;
                int oldIndex = -1, newIndex = -1;
                Wpf.Ui.Controls.Button sourceBtn = null;

                for (int i = 0; i < AppsWrapPanel.Children.Count; i++)
                {
                    if (AppsWrapPanel.Children[i] is Wpf.Ui.Controls.Button btn)
                    {
                        if (btn.Tag as string == sourceTag) { oldIndex = i; sourceBtn = btn; }
                        if (btn.Tag as string == targetButton.Tag as string) newIndex = i;
                    }
                }

                if (oldIndex != -1 && newIndex != -1)
                {
                    AppsWrapPanel.Children.RemoveAt(oldIndex);
                    AppsWrapPanel.Children.Insert(newIndex, sourceBtn);
                    ((Storyboard)FindResource("ShakeAnimation")).Begin(sourceBtn, true);
                }
            }
        }

        private void SaveCurrentAppOrder()
        {
            var newOrderList = new List<AppInfo>();
            foreach (var child in AppsWrapPanel.Children)
            {
                if (child is Wpf.Ui.Controls.Button btn && btn.Tag as string != AddAppTag)
                {
                    var appData = _loadedApps.FirstOrDefault(a => a.Name == btn.Tag as string);
                    if (appData != null) newOrderList.Add(appData);
                }
            }
            _loadedApps = newOrderList;
            File.WriteAllText(JsonPath, JsonSerializer.Serialize(_loadedApps, new JsonSerializerOptions { WriteIndented = true }));
        }
        private void BackgroundDimmer_MouseDown(object sender, MouseButtonEventArgs e)
        {
            // Вызываем ваш стандартный метод закрытия оверлея (например, CloseOverlay_Click)
            CloseOverlay_Click(sender, e);
            BackgroundDimmer.IsHitTestVisible = false;
        }
        private async void TileButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Wpf.Ui.Controls.Button button && button.Tag is string tag)
            {
                var selectedApp = _loadedApps?.FirstOrDefault(a => a.Name == tag);
                if (selectedApp == null) return;

                // Обновляем бейдж источника в оверлее перед его открытием
                UpdateOverlaySource(selectedApp);

                try
                {
                    string pathForBg = selectedApp.IconPath;
                    var imgSource = new Helpers.StringToImageSourceConverter().Convert(pathForBg, null, null, null) as ImageSource;
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

                    // Выезжающая панель
                    AppOverlayPanel.Visibility = Visibility.Visible;
                    var slideIn = new ThicknessAnimation
                    {
                        To = new Thickness(0, 0, 0, 0),
                        Duration = TimeSpan.FromSeconds(0.4),
                        EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
                    };
                    AppOverlayPanel.BeginAnimation(MarginProperty, slideIn);
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
            var slideOut = new ThicknessAnimation
            {
                To = new Thickness(0, 0, -500, 0),
                Duration = TimeSpan.FromSeconds(0.3),
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn }
            };
            slideOut.Completed += (s, ev) => AppOverlayPanel.Visibility = Visibility.Collapsed;
            AppOverlayPanel.BeginAnimation(MarginProperty, slideOut);

            ((Storyboard)this.Resources["FadeOutBg"]).Begin();
            BackgroundDimmer.IsHitTestVisible = false;
        }

        private void InstallButton_Click(object sender, RoutedEventArgs e)
        {

        }

        private void AppOverlayPanel_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            // Ищем внутреннюю карточку Border
            var border = AppOverlayPanel.Children.OfType<Border>().FirstOrDefault();
            if (border != null)
            {
                // Получаем позицию клика относительно этой карточки
                Point point = e.GetPosition(border);

                // Если клик был за пределами границ карточки (по отступам Margin панели)
                if (point.X < 0 || point.Y < 0 || point.X > border.ActualWidth || point.Y > border.ActualHeight)
                {
                    CloseOverlay_Click(sender, e);
                }
            }
        }
    }



    public class WinGetSearchResultViewModel : INotifyPropertyChanged
    {
        public WGetNET.WinGetPackage Package { get; set; }
        public bool IsInstalled { get; set; }
        public bool HasUpdate { get; set; }

        private bool _isProcessing;
        public bool IsProcessing
        {
            get => _isProcessing;
            set { _isProcessing = value; OnPropertyChanged(); OnPropertyChanged(nameof(ActionButtonsVis)); OnPropertyChanged(nameof(ProgressVis)); }
        }

        public string Name => Package.Name;
        public string Id => Package.Id;
        public string Version => Package.VersionString;

        public Visibility ActionButtonsVis => IsProcessing ? Visibility.Collapsed : Visibility.Visible;
        public Visibility ProgressVis => IsProcessing ? Visibility.Visible : Visibility.Collapsed;

        public Visibility InstallBtnVis => (IsInstalled || IsProcessing) ? Visibility.Collapsed : Visibility.Visible;
        public Visibility UninstallBtnVis => (!IsInstalled || IsProcessing) ? Visibility.Collapsed : Visibility.Visible;
        public Visibility UpdateBtnVis => (HasUpdate && !IsProcessing) ? Visibility.Visible : Visibility.Collapsed;

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}