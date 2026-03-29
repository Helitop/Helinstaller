using Helinstaller.ViewModels.Pages;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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
        private Point _startPoint;
        private bool _isDragging = false;
        private Wpf.Ui.Controls.Button _draggedButton;

        private DragAdorner _dragAdorner;
        private AdornerLayer _adornerLayer;
        private Point _dragOffset;

        // НОВЫЕ РАЗМЕРЫ: Под 4 штуки в ряд (горизонтальные карточки)
        private const double TileW = 220;
        private const double TileH = 110;

        public DashboardViewModel ViewModel { get; }
        private readonly INavigationService _navigationService;

        public DashboardPage(DashboardViewModel viewModel, INavigationService navigationService)
        {
            ViewModel = viewModel;
            _navigationService = navigationService;
            DataContext = this;
            InitializeComponent();
        }

        private void Page_Loaded(object sender, RoutedEventArgs e)
        {
            DashboardBackground.Opacity = 0;
            BackgroundDimmer.Opacity = 0;
            DashboardBackground.Source = null;
            LoadAppsAndGenerateButtons();
        }

        private void LoadAppsAndGenerateButtons()
        {
            try
            {
                if (_isEditMode) return;

                AppsWrapPanel.Children.Clear();
                AppsWrapPanel.Children.Add(CreateAddAppTile());

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

                foreach (var app in _loadedApps)
                {
                    AppsWrapPanel.Children.Add(CreateAppTile(app));
                }
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show($"Ошибка загрузки приложений: {ex.Message}");
            }
        }

        private Wpf.Ui.Controls.Button CreateAddAppTile()
        {
            var button = new Wpf.Ui.Controls.Button
            {
                Width = TileW,
                Height = TileH,
                Margin = new Thickness(8),
                Appearance = ControlAppearance.Transparent,
                BorderThickness = new Thickness(1),
                BorderBrush = new SolidColorBrush(Color.FromArgb(20, 255, 255, 255)),
                CornerRadius = new CornerRadius(12),
                HorizontalContentAlignment = HorizontalAlignment.Center,
                VerticalContentAlignment = VerticalAlignment.Center,
                Tag = AddAppTag
            };

            var stack = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            var icon = new SymbolIcon { Symbol = SymbolRegular.AddSquare24, FontSize = 28, Foreground = Brushes.White, Opacity = 0.6 };
            var text = new Wpf.Ui.Controls.TextBlock { Text = "Добавить", FontSize = 14, FontWeight = FontWeights.SemiBold, Margin = new Thickness(12, 0, 0, 0), Foreground = Brushes.White, Opacity = 0.6, VerticalAlignment = VerticalAlignment.Center };

            stack.Children.Add(icon);
            stack.Children.Add(text);
            button.Content = stack;
            button.Click += TileButton_Click;

            button.MouseEnter += (s, e) => { button.Background = new SolidColorBrush(Color.FromArgb(15, 255, 255, 255)); icon.Opacity = 1; text.Opacity = 1; };
            button.MouseLeave += (s, e) => { button.Background = Brushes.Transparent; icon.Opacity = 0.6; text.Opacity = 0.6; };

            return button;
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

            anchor.PreviewMouseLeftButtonDown += Button_PreviewMouseLeftButtonDown;
            anchor.PreviewMouseMove += Button_PreviewMouseMove;
            anchor.PreviewMouseLeftButtonUp += Button_PreviewMouseLeftButtonUp;
            anchor.Drop += Button_Drop;

            // Напрямую вешаем клик на плитку, так как всплывающего окна больше нет
            anchor.Click += TileButton_Click;
            anchor.ContextMenu = CreateAppContextMenu(app);

            // Сетка карточки: Иконка слева, Текст справа
            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            // Левая часть: Иконка
            var iconBorder = new Border
            {
                Width = 48,
                Height = 48,
                CornerRadius = new CornerRadius(8),
                ClipToBounds = true,
                Margin = new Thickness(0, 0, 14, 0),
                VerticalAlignment = VerticalAlignment.Center
            };
            var icon = new Image { Stretch = Stretch.UniformToFill };
            RenderOptions.SetBitmapScalingMode(icon, BitmapScalingMode.HighQuality);
            iconBorder.Child = icon;

            Grid.SetColumn(iconBorder, 0);
            grid.Children.Add(iconBorder);

            // Правая часть: Инфо
            var infoStack = new StackPanel { VerticalAlignment = VerticalAlignment.Center };

            var headerGrid = new Grid { Margin = new Thickness(0, 0, 0, 6) };
            headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var title = new Wpf.Ui.Controls.TextBlock
            {
                Text = app.Title,
                FontSize = 14,
                FontWeight = FontWeights.Bold,
                TextTrimming = TextTrimming.CharacterEllipsis,
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(title, 0);
            headerGrid.Children.Add(title);

            // Бейдж источника
            string sourceText = "Local";
            if (!string.IsNullOrEmpty(app.DownloadUrl))
            {
                if (app.DownloadUrl.StartsWith("github:", StringComparison.OrdinalIgnoreCase)) sourceText = "GitHub";
                else if (app.DownloadUrl.StartsWith("winget:", StringComparison.OrdinalIgnoreCase)) sourceText = "WinGet";
                else if (app.DownloadUrl.StartsWith("http")) sourceText = "Web";
            }
            // Достаем цвет акцента (как Color)
            Color accentColor = SystemColors.AccentColor;

            // Делаем его полупрозрачным (например, прозрачность 100 из 255)
            Color semiTransparentColor = Color.FromArgb(100, accentColor.R, accentColor.G, accentColor.B);

            var badge = new Border
            {
                Background = new SolidColorBrush(semiTransparentColor),
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(6, 1, 6, 1),
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(8, 0, 0, 0)
            };
            badge.Child = new Wpf.Ui.Controls.TextBlock { Text = sourceText, FontSize = 9, FontWeight = FontWeights.SemiBold, Foreground = Brushes.White };

            Grid.SetColumn(badge, 1);
            headerGrid.Children.Add(badge);

            // Описание
            var desc = new Wpf.Ui.Controls.TextBlock
            {
                Text = app.Description,
                FontSize = 11,
                Opacity = 0.6,
                TextWrapping = TextWrapping.Wrap,
                MaxHeight = 36, // Хватает на 2.5 строки
                LineHeight = 14,
                TextTrimming = TextTrimming.CharacterEllipsis
            };

            infoStack.Children.Add(headerGrid);
            infoStack.Children.Add(desc);
            Grid.SetColumn(infoStack, 1);
            grid.Children.Add(infoStack);

            anchor.Content = grid;

            // Загрузка иконки
            if (!string.IsNullOrEmpty(app.IconPath) && File.Exists(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, app.IconPath.TrimStart('/', '\\'))))
            {
                icon.Source = new Helpers.StringToImageSourceConverter().Convert(app.IconPath, null, null, null) as ImageSource;
            }
            else
            {
                _ = LoadIconInBackgroundAsync(app, icon);
            }

            return anchor;
        }

        // Обновленный метод загрузки иконки без Blur'а
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
                            var newImgSource = new Helpers.StringToImageSourceConverter().Convert(savedPath, null, null, null) as ImageSource;
                            iconControl.Source = newImgSource;

                            var fadeIn = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(500));
                            iconControl.BeginAnimation(UIElement.OpacityProperty, fadeIn);
                        });
                    }
                }
            }
            catch { }
        }

        private void EditApp(AppInfo app)
        {
            var editorPage = new Editor(app, _navigationService);
            DependencyObject parent = VisualTreeHelper.GetParent(this);
            while (parent != null && !(parent is Frame)) parent = VisualTreeHelper.GetParent(parent);

            if (parent is Frame parentFrame) parentFrame.Navigate(editorPage);
            else _navigationService.Navigate(typeof(Editor));
        }

        private ContextMenu CreateAppContextMenu(AppInfo app)
        {
            var contextMenu = new ContextMenu();
            var editItem = new MenuItem { Header = "Редактировать", Icon = new SymbolIcon { Symbol = SymbolRegular.Edit24 } };
            editItem.Click += (s, e) => EditApp(app);

            var moveItem = new MenuItem { Header = "Изменить порядок", Icon = new SymbolIcon { Symbol = SymbolRegular.ArrowSort24 } };
            moveItem.Click += (s, e) => EnterEditMode();

            var deleteItem = new MenuItem { Header = "Удалить", Icon = new SymbolIcon { Symbol = SymbolRegular.Delete24 }, Foreground = Brushes.IndianRed };
            deleteItem.Click += (s, e) => DeleteApp(app.Name);

            contextMenu.Items.Add(editItem);
            contextMenu.Items.Add(moveItem);
            contextMenu.Items.Add(new Separator());
            contextMenu.Items.Add(deleteItem);

            return contextMenu;
        }

        // --- Drag & Drop логика (БЕЗ ИЗМЕНЕНИЙ, работает идеально) ---
        private void Button_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            _draggedButton = sender as Wpf.Ui.Controls.Button;
            _startPoint = e.GetPosition(this);
            _dragOffset = e.GetPosition(this);
        }

        private void Button_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            _isDragging = false;
            CleanupDrag();
        }

        private void EnterEditMode()
        {
            if (_isEditMode) return;
            _isEditMode = true;

            ((Storyboard)FindResource("SlideDownAnimation")).Begin();
            var storyboard = (Storyboard)FindResource("ShakeAnimation");
            foreach (var child in AppsWrapPanel.Children)
                if (child is Wpf.Ui.Controls.Button btn && btn.Tag as string != AddAppTag)
                    storyboard.Begin(btn, true);
        }

        private void ExitEditMode()
        {
            if (!_isEditMode) return;
            _isEditMode = false;

            ((Storyboard)FindResource("SlideUpAnimation")).Begin();
            var storyboard = (Storyboard)FindResource("ShakeAnimation");
            foreach (var child in AppsWrapPanel.Children)
                if (child is Wpf.Ui.Controls.Button btn)
                {
                    storyboard.Stop(btn);
                    if (btn.RenderTransform is RotateTransform rt) rt.Angle = 0;
                }

            SaveCurrentAppOrder();
        }

        private void Grid_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) => ExitEditMode();

        private void Button_PreviewMouseMove(object sender, MouseEventArgs e)
        {
            if (!_isEditMode) return;
            if (e.LeftButton == MouseButtonState.Pressed && !_isDragging)
            {
                Point position = e.GetPosition(this);
                if (Math.Abs(position.X - _startPoint.X) > SystemParameters.MinimumHorizontalDragDistance ||
                    Math.Abs(position.Y - _startPoint.Y) > SystemParameters.MinimumVerticalDragDistance)
                {
                    StartDrag(sender as Wpf.Ui.Controls.Button);
                }
            }
        }

        private void StartDrag(Wpf.Ui.Controls.Button button)
        {
            if (button == null || button.Tag as string == AddAppTag) return;
            _isDragging = true;
            _draggedButton = button;

            _adornerLayer = AdornerLayer.GetAdornerLayer(this);
            _dragAdorner = new DragAdorner(_draggedButton);
            _adornerLayer.Add(_dragAdorner);

            Point currentPos = Mouse.GetPosition(this);
            _dragAdorner.UpdatePosition(new Point(currentPos.X - _dragOffset.X, currentPos.Y - _dragOffset.Y));

            _draggedButton.Opacity = 0.2;
            DragDrop.DoDragDrop(button, button.Tag, DragDropEffects.Move);
            CleanupDrag();
        }

        private void Grid_PreviewDragOver(object sender, DragEventArgs e)
        {
            if (_isDragging && _dragAdorner != null)
            {
                Point currentPos = e.GetPosition(this);
                _dragAdorner.UpdatePosition(new Point(currentPos.X - _dragOffset.X, currentPos.Y - _dragOffset.Y));
            }
        }

        private void CleanupDrag()
        {
            if (_dragAdorner != null) { _adornerLayer.Remove(_dragAdorner); _dragAdorner = null; }
            if (_draggedButton != null) { _draggedButton.Opacity = 1.0; _draggedButton = null; }
            _isDragging = false;
        }

        private void Button_Drop(object sender, DragEventArgs e)
        {
            if (!_isEditMode) return;

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

        private void DeleteApp(string appTag)
        {
            var res = CustomMessageBox.Show("Удалить приложение?", "", System.Windows.MessageBoxButton.YesNo);
            if (res == CustomMessageBox.MessageBoxResult.Yes)
            {
                var btnToRemove = AppsWrapPanel.Children.OfType<Wpf.Ui.Controls.Button>().FirstOrDefault(b => b.Tag as string == appTag);
                if (btnToRemove != null)
                {
                    ((Storyboard)FindResource("ShakeAnimation")).Stop(btnToRemove);
                    AppsWrapPanel.Children.Remove(btnToRemove);
                }

                var appToRemove = _loadedApps.FirstOrDefault(a => a.Name == appTag);
                if (appToRemove != null)
                {
                    _loadedApps.Remove(appToRemove);
                    SaveCurrentAppOrder();
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

        private void ConfirmChanges_Click(object sender, RoutedEventArgs e) => ExitEditMode();

        private async void TileButton_Click(object sender, RoutedEventArgs e)
        {
            if (_isEditMode || _isDragging) return;

            if (sender is Wpf.Ui.Controls.Button button && button.Tag is string tag)
            {
                if (tag == AddAppTag)
                {
                    _navigationService.Navigate(typeof(Editor));
                    return;
                }

                var selectedApp = _loadedApps?.FirstOrDefault(a => a.Name == tag);
                if (selectedApp == null) return;

                // Красивый эффект блюр-фона
                try
                {
                    string pathForBg = !string.IsNullOrEmpty(selectedApp.PreviewPath) ? selectedApp.PreviewPath : selectedApp.IconPath;
                    var imgSource = new Helpers.StringToImageSourceConverter().Convert(pathForBg, null, null, null) as ImageSource;
                    if (imgSource != null)
                    {
                        DashboardBackground.Source = imgSource;
                        ((Storyboard)this.Resources["FadeInBg"]).Begin();
                    }
                }
                catch { }

                ViewModel.OnNavigateToApp(tag);

                LoadingOverlay.Visibility = Visibility.Visible;
                ((Storyboard)this.Resources["FadeInLoading"]).Begin();

                try
                {
                    _navigationService.Navigate(typeof(AppPage));
                    var page = App.Services.GetService(typeof(AppPage)) as AppPage;
                    if (page != null)
                    {
                        await page.ViewModel.InitializeAsync(selectedApp.Title, selectedApp.Description ?? "", selectedApp.IconPath ?? "", selectedApp.PreviewPath ?? "", selectedApp.DownloadUrl);
                        await page.ViewModel.CheckCommand.ExecuteAsync(null);
                    }
                }
                finally
                {
                    ((Storyboard)this.Resources["FadeInLoading"]).Stop();
                    ((Storyboard)this.Resources["FadeOutBg"]).Begin();
                    LoadingOverlay.Visibility = Visibility.Collapsed;
                }
            }
        }

        // Класс DragAdorner остался без изменений (он идеален)
        public class DragAdorner : Adorner
        {
            private readonly Rectangle _child;
            private double _offsetLeft;
            private double _offsetTop;

            public DragAdorner(UIElement adornedElement) : base(adornedElement)
            {
                var brush = new VisualBrush(adornedElement) { Opacity = 0.8 };
                _child = new Rectangle
                {
                    Width = adornedElement.RenderSize.Width,
                    Height = adornedElement.RenderSize.Height,
                    Fill = brush,
                    Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 10, ShadowDepth = 5, Opacity = 0.5 }
                };
            }

            public void UpdatePosition(Point currentPosition) { _offsetLeft = currentPosition.X; _offsetTop = currentPosition.Y; if (Parent is AdornerLayer layer) layer.Update(AdornedElement); }
            protected override Size MeasureOverride(Size constraint) { _child.Measure(constraint); return _child.DesiredSize; }
            protected override Size ArrangeOverride(Size finalSize) { _child.Arrange(new Rect(finalSize)); return finalSize; }
            protected override Visual GetVisualChild(int index) => _child;
            protected override int VisualChildrenCount => 1;
            public override GeneralTransform GetDesiredTransform(GeneralTransform transform)
            {
                var result = new GeneralTransformGroup();
                result.Children.Add(base.GetDesiredTransform(transform));
                result.Children.Add(new TranslateTransform(_offsetLeft, _offsetTop));
                return result;
            }
        }
    }
}