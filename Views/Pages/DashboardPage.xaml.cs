using Helinstaller.ViewModels.Pages;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents; // Нужно для Adorner
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

        // Состояние Drag & Drop
        private bool _isEditMode = false;
        private Point _startPoint;
        private bool _isDragging = false;
        private Wpf.Ui.Controls.Button _draggedButton;

        // Для визуального Adorner'а
        private DragAdorner _dragAdorner;
        private AdornerLayer _adornerLayer;
        private Point _dragOffset;

        // Переменные для отслеживания ховера
        private string _currentHoveringApp = "";
        private Point _currentAnchorLocation; 

        // КОНСТАНТЫ РАЗМЕРОВ ПЛИТКИ
        private const double TileW = 120;
        private const double TileH = 160;

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
            // Сбрасываем фон при каждом заходе на страницу
            DashboardBackground.Opacity = 0;
            BackgroundDimmer.Opacity = 0;
            DashboardBackground.Source = null;

            LoadAppsAndGenerateButtons();
        }

        // --- Загрузка и создание UI ---
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
                Width = TileW,  // 120
                Height = TileH, // 160
                Margin = new Thickness(8),
                Appearance = Wpf.Ui.Controls.ControlAppearance.Transparent,
                Background = new SolidColorBrush(Color.FromArgb(20, 255, 255, 255)), // Полупрозрачный фон
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(12),
                Tag = AddAppTag
            };
            var grid = new Grid();
            grid.RowDefinitions.Add(new RowDefinition());
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            var icon = new Wpf.Ui.Controls.SymbolIcon
            {
                Symbol = SymbolRegular.Add48,
                FontSize = 32,
            };
            var text = new Wpf.Ui.Controls.TextBlock
            {
                Text = "Добавить",
                FontSize = 13,
                FontWeight = FontWeights.SemiBold,
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 5, 0, 10),
            };

            Grid.SetRow(icon, 0); grid.Children.Add(icon);
            Grid.SetRow(text, 1); grid.Children.Add(text);

            button.Content = grid;
            button.Click += TileButton_Click;

            // Анимация для "Добавить"
            button.MouseEnter += (s, e) => {
                button.Background = new SolidColorBrush(Color.FromArgb(40, 255, 255, 255));
            };
            button.MouseLeave += (s, e) => {
                button.Background = new SolidColorBrush(Color.FromArgb(20, 255, 255, 255));
            };

            return button;
        }

        private void TileButton_MouseEnter(object sender, MouseEventArgs e)
        {
            // Исключаем плитку "Добавить"
            if (sender is Wpf.Ui.Controls.Button btn && btn.Tag as string == AddAppTag) return;

            // Запуск анимации появления editTip
            if (FindResource("ShowEditTip") is Storyboard showStoryboard)
            {
                showStoryboard.Begin(this);
            }
        }

        private void TileButton_MouseLeave(object sender, MouseEventArgs e)
        {
            // Исключаем плитку "Добавить"
            if (sender is Wpf.Ui.Controls.Button btn && btn.Tag as string == AddAppTag) return;

            // Запуск анимации исчезновения editTip
            if (FindResource("HideEditTip") is Storyboard hideStoryboard)
            {
                hideStoryboard.Begin(this);
            }
        }

        private FrameworkElement CreateAppTile(AppInfo app)
        {
            var anchor = new Wpf.Ui.Controls.Button
            {
                Width = TileW,
                Height = TileH,
                Margin = new Thickness(8),
                Appearance = Wpf.Ui.Controls.ControlAppearance.Secondary,
                CornerRadius = new CornerRadius(12),
                Padding = new Thickness(0),
                BorderThickness = new Thickness(1),
                HorizontalContentAlignment = HorizontalAlignment.Center,
                VerticalContentAlignment = VerticalAlignment.Center,
                Tag = app.Name
            };

            // === ДОБАВЛЯЕМ КОНТЕКСТНОЕ МЕНЮ (ПКМ) ===
            anchor.ContextMenu = CreateAppContextMenu(app);
            // =========================================

            var mainGrid = new Grid
            {
                Width = TileW - 2,
                Height = TileH - 2,
                Clip = new RectangleGeometry(new Rect(0, 0, TileW - 2, TileH - 2), 11, 11)
            };

            mainGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1.8, GridUnitType.Star) });
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

            // Верх (Иконка + Блюр)
            var topContainer = new Grid();
            Grid.SetRow(topContainer, 0);

            // Решаем, какую картинку блюрить. Если есть превью - берем его. Если нет - берем иконку.
            string pathForBlur = !string.IsNullOrEmpty(app.PreviewPath) ? app.PreviewPath : app.IconPath;

            var blurredRect = new System.Windows.Shapes.Rectangle
            {
                Width = 200,
                Height = 200,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Fill = new ImageBrush
                {
                    // Используем pathForBlur
                    ImageSource = new Helpers.StringToImageSourceConverter().Convert(pathForBlur, null, null, null) as ImageSource,
                    Stretch = Stretch.UniformToFill,
                    AlignmentX = AlignmentX.Center,
                    AlignmentY = AlignmentY.Center
                },
                Effect = new System.Windows.Media.Effects.BlurEffect { Radius = 20, KernelType = System.Windows.Media.Effects.KernelType.Gaussian }
            };

            var topOverlay = new Border { Background = new SolidColorBrush(Color.FromArgb(60, 0, 0, 0)) };

            var icon = new Image
            {
                Width = 44,
                Height = 44,
                Source = new Helpers.StringToImageSourceConverter().Convert(app.IconPath, null, null, null) as ImageSource,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
            RenderOptions.SetBitmapScalingMode(icon, BitmapScalingMode.HighQuality);
            icon.Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 10, ShadowDepth = 2, Opacity = 0.5 };

            topContainer.Children.Add(blurredRect);
            topContainer.Children.Add(topOverlay);
            topContainer.Children.Add(icon);

            // Низ (Инфо)
            var bottomContainer = new Grid { Background = new SolidColorBrush(Color.FromRgb(32, 32, 32)) };
            Grid.SetRow(bottomContainer, 1);

            var infoGrid = new Grid { Margin = new Thickness(8, 6, 8, 6) };
            infoGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            infoGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

            var title = new Wpf.Ui.Controls.TextBlock
            {
                Text = app.Title,
                FontSize = 12,
                FontWeight = FontWeights.SemiBold,
                Foreground = Brushes.White,
                TextWrapping = TextWrapping.Wrap,
                TextTrimming = TextTrimming.CharacterEllipsis,
                MaxHeight = 34,
                VerticalAlignment = VerticalAlignment.Top
            };

            string sourceText = "Local";
            if (!string.IsNullOrEmpty(app.DownloadUrl))
            {
                if (app.DownloadUrl.StartsWith("github:", StringComparison.OrdinalIgnoreCase)) sourceText = "GitHub";
                else if (app.DownloadUrl.StartsWith("winget:", StringComparison.OrdinalIgnoreCase)) sourceText = "Winget";
                else if (app.DownloadUrl.StartsWith("http")) sourceText = "Web";
            }

            var badgeBorder = new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(55, 55, 55)),
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(5, 1, 5, 1),
                VerticalAlignment = VerticalAlignment.Bottom,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(0, 4, 0, 0)
            };

            badgeBorder.Child = new Wpf.Ui.Controls.TextBlock { Text = sourceText, FontSize = 9, Foreground = Brushes.LightGray };

            Grid.SetRow(title, 0);
            Grid.SetRow(badgeBorder, 1);
            infoGrid.Children.Add(title);
            infoGrid.Children.Add(badgeBorder);
            bottomContainer.Children.Add(infoGrid);

            mainGrid.Children.Add(topContainer);
            mainGrid.Children.Add(bottomContainer);
            anchor.Content = mainGrid;

            anchor.MouseEnter += (s, e) => { if (_currentHoveringApp != app.Name) ShowHoverTile(anchor, app); };
            anchor.MouseLeave += async (s, e) =>
            {
                await Task.Delay(50);
                if (!HoverTile.IsMouseOver && _currentHoveringApp == app.Name) HoverTile_MouseLeave(null, null);
            };
            // ЭТО ДОБАВИТЬ В КОНЕЦ МЕТОДА CreateAppTile (перед return anchor;)
            if (string.IsNullOrEmpty(app.IconPath) || !File.Exists(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, app.IconPath.TrimStart('/', '\\'))))
            {
                // Запускаем фоновую загрузку, не блокируя UI
                _ = LoadIconInBackgroundAsync(app, icon, blurredRect);
            }
            return anchor;
        }
        private async Task LoadIconInBackgroundAsync(AppInfo app, Image iconControl, System.Windows.Shapes.Rectangle blurControl)
        {
            try
            {
                // 1. Получаем URL иконки, если его нет
                var meta = await Helpers.MetadataService.GetMetadataAsync(app.DownloadUrl);

                if (!string.IsNullOrEmpty(meta.IconUrl))
                {
                    // 2. Скачиваем саму иконку (сохраняется с прозрачностью)
                    string savedPath = await Helpers.MetadataService.DownloadIconAsync(meta.IconUrl, app.Name);

                    if (!string.IsNullOrEmpty(savedPath))
                    {
                        // 3. Обновляем JSON (чтобы в следующий раз грузилось сразу с диска)
                        app.IconPath = savedPath;
                        SaveCurrentAppOrder(); // Твой метод сохранения JSON

                        // 4. Обновляем UI в главном потоке
                        Application.Current.Dispatcher.Invoke(() =>
                        {
                            var converter = new Helpers.StringToImageSourceConverter();
                            var newImgSource = converter.Convert(savedPath, null, null, null) as ImageSource;

                            // Плавно показываем иконку
                            iconControl.Source = newImgSource;
                            blurControl.Fill = new ImageBrush
                            {
                                ImageSource = newImgSource,
                                Stretch = Stretch.UniformToFill,
                                AlignmentX = AlignmentX.Center,
                                AlignmentY = AlignmentY.Center
                            };

                            // Анимация плавного появления
                            var fadeIn = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(500));
                            iconControl.BeginAnimation(UIElement.OpacityProperty, fadeIn);
                            blurControl.BeginAnimation(UIElement.OpacityProperty, fadeIn);
                        });
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Ошибка фоновой загрузки иконки: {ex.Message}");
            }
        }

        private void EditApp(AppInfo app)
        {
            // 1. Создаем страницу редактора вручную, передавая ей данные приложения
            var editorPage = new Editor(app, _navigationService);

            // 2. Ищем родительский Frame, чтобы заставить его показать нашу созданную страницу
            // Это "хак", чтобы обойти ограничение DI-контейнера навигации
            DependencyObject parent = VisualTreeHelper.GetParent(this);
            while (parent != null && !(parent is Frame))
            {
                parent = VisualTreeHelper.GetParent(parent);
            }

            if (parent is Frame parentFrame)
            {
                // Навигация напрямую к экземпляру страницы
                parentFrame.Navigate(editorPage);
            }
            else
            {
                // Запасной вариант (откроет пустой редактор, если фрейм не найден)
                _navigationService.Navigate(typeof(Editor));
            }
        }

        private void ShowHoverTile(FrameworkElement anchor, AppInfo app)
        {
            if (_currentHoveringApp == app.Name) return;
            _currentHoveringApp = app.Name;

            // 1. ЖЕСТКАЯ ОСТАНОВКА всех предыдущих анимаций
            HoverTile.BeginAnimation(FrameworkElement.WidthProperty, null);
            HoverTile.BeginAnimation(FrameworkElement.HeightProperty, null);
            HoverDescRow.BeginAnimation(RowDefinition.HeightProperty, null);
            HoverInfoStack.BeginAnimation(UIElement.OpacityProperty, null);

            // 2. Мгновенный сброс к базовому размеру
            HoverTile.Width = TileW;
            HoverTile.Height = TileH;
            HoverDescRow.Height = new GridLength(0);
            HoverInfoStack.Opacity = 0;

            // 3. Обновляем контент.
            HoverBgBrush.ImageSource = null;

            var converter = new Helpers.StringToImageSourceConverter();

            // Подсовываем иконку в фон, если превью пустое
            string pathForHoverBlur = !string.IsNullOrEmpty(app.PreviewPath) ? app.PreviewPath : app.IconPath;
            HoverBgBrush.ImageSource = converter.Convert(pathForHoverBlur, null, null, null) as ImageSource;

            HoverTitle.Text = app.Title;
            HoverDesc.Text = app.Description;
            HoverTitle.Text = app.Title;
            HoverDesc.Text = app.Description;
            HoverTile.ContextMenu = CreateAppContextMenu(app);
            HoverTile.BorderThickness = new Thickness(1);
            HoverTile.SetResourceReference(Border.BorderBrushProperty, SystemColors.AccentColorBrushKey);

            // 4. Переносим Popup
            HoverPopup.PlacementTarget = anchor;
            if (!HoverPopup.IsOpen)
            {
                HoverPopup.IsOpen = true;
            }
            else
            {
                // Простой и надежный флип (переключаем туда-сюда на тысячную долю), 
                // чтобы заставить WPF перерисовать координаты попапа
                HoverPopup.HorizontalOffset = HoverPopup.HorizontalOffset == 0 ? 0.001 : 0;
            }

            // 5. Запуск анимаций
            var duration = TimeSpan.FromMilliseconds(200); // Сделал 200мс, так оно будет казаться более отзывчивым
            var easeOut = new CubicEase { EasingMode = EasingMode.EaseOut };

            double targetW = 160;
            double targetH = 220;

            HoverTile.BeginAnimation(FrameworkElement.WidthProperty, new DoubleAnimation(targetW, duration) { EasingFunction = easeOut });
            HoverTile.BeginAnimation(FrameworkElement.HeightProperty, new DoubleAnimation(targetH, duration) { EasingFunction = easeOut });

            HoverDescRow.BeginAnimation(RowDefinition.HeightProperty, new Animations.GridLengthAnimation
            {
                From = new GridLength(0),
                To = new GridLength(90),
                Duration = duration,
                EasingFunction = easeOut
            });
            HoverInfoStack.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(1, duration));
        }
        private void HoverTile_MouseLeave(object sender, MouseEventArgs e)
        {
            // ЗАЩИТА: Если ПКМ-меню сейчас открыто, НЕ сворачиваем плитку!
            if (HoverTile.ContextMenu != null && HoverTile.ContextMenu.IsOpen) return;

            if (string.IsNullOrEmpty(_currentHoveringApp)) return;
            _currentHoveringApp = "";

            var duration = TimeSpan.FromMilliseconds(200);
            var easeIn = new CubicEase { EasingMode = EasingMode.EaseIn };

            var widthAnim = new DoubleAnimation(TileW, duration) { EasingFunction = easeIn };
            var heightAnim = new DoubleAnimation(TileH, duration) { EasingFunction = easeIn };
            var rowAnim = new Animations.GridLengthAnimation
            {
                From = HoverDescRow.Height,
                To = new GridLength(0),
                Duration = duration,
                EasingFunction = easeIn
            };
            var opacityAnim = new DoubleAnimation(0, duration);

            widthAnim.Completed += (s, args) => {
                // Закрываем попап только если мышка не перескочила на другое приложение
                if (string.IsNullOrEmpty(_currentHoveringApp)) HoverPopup.IsOpen = false;
            };

            HoverTile.BeginAnimation(FrameworkElement.WidthProperty, widthAnim);
            HoverTile.BeginAnimation(FrameworkElement.HeightProperty, heightAnim);
            HoverDescRow.BeginAnimation(RowDefinition.HeightProperty, rowAnim);
            HoverInfoStack.BeginAnimation(UIElement.OpacityProperty, opacityAnim);
        }

        private void HideHoverTileInstant()
        {
            _currentHoveringApp = "";
            HoverPopup.IsOpen = false;

            HoverTile.BeginAnimation(FrameworkElement.WidthProperty, null);
            HoverTile.BeginAnimation(FrameworkElement.HeightProperty, null);
            HoverDescRow.BeginAnimation(RowDefinition.HeightProperty, null);
            HoverInfoStack.BeginAnimation(UIElement.OpacityProperty, null);

            HoverTile.Width = TileW;
            HoverTile.Height = TileH;
            HoverDescRow.Height = new GridLength(0);
            HoverInfoStack.Opacity = 0;
        }

        private void HoverTile_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (string.IsNullOrEmpty(_currentHoveringApp)) return;
            string appTag = _currentHoveringApp;

            HideHoverTileInstant();

            ViewModel.OnNavigateToApp(appTag);

            var dummyButton = new Wpf.Ui.Controls.Button { Tag = appTag };
            TileButton_Click(dummyButton, null);
        }

        // --- Остальные методы без изменений ---
        private ContextMenu CreateAppContextMenu(AppInfo app)
        {
            var contextMenu = new ContextMenu();

            var editItem = new MenuItem { Header = "Редактировать", Icon = new SymbolIcon { Symbol = SymbolRegular.Edit24 } };
            editItem.Click += (s, e) => { HideHoverTileInstant(); EditApp(app); };

            var moveItem = new MenuItem { Header = "Изменить порядок", Icon = new SymbolIcon { Symbol = SymbolRegular.ArrowSort24 } };
            moveItem.Click += (s, e) => { HideHoverTileInstant(); EnterEditMode(); };

            var deleteItem = new MenuItem { Header = "Удалить", Icon = new SymbolIcon { Symbol = SymbolRegular.Delete24 }, Foreground = Brushes.IndianRed };
            deleteItem.Click += (s, e) => { HideHoverTileInstant(); DeleteApp(app.Name); };

            contextMenu.Items.Add(editItem);
            contextMenu.Items.Add(moveItem);
            contextMenu.Items.Add(new Separator());
            contextMenu.Items.Add(deleteItem);

            // ВАЖНО: Когда меню закрывается (пользователь кликнул мимо), 
            // проверяем, осталась ли мышь на плитке. Если нет - сворачиваем.
            contextMenu.Closed += (s, e) =>
            {
                if (!HoverTile.IsMouseOver)
                {
                    HoverTile_MouseLeave(null, null);
                }
            };

            return contextMenu;
        }

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

            var slideDown = (Storyboard)FindResource("SlideDownAnimation");
            slideDown.Begin();

            var storyboard = (Storyboard)FindResource("ShakeAnimation");
            foreach (var child in AppsWrapPanel.Children)
            {
                if (child is Wpf.Ui.Controls.Button btn && btn.Tag as string != AddAppTag)
                {
                    storyboard.Begin(btn, true);
                }
            }
        }

        private void ExitEditMode()
        {
            if (!_isEditMode) return;
            _isEditMode = false;

            var slideUp = (Storyboard)FindResource("SlideUpAnimation");
            slideUp.Begin();

            var storyboard = (Storyboard)FindResource("ShakeAnimation");
            foreach (var child in AppsWrapPanel.Children)
            {
                if (child is Wpf.Ui.Controls.Button btn)
                {
                    storyboard.Stop(btn);
                    if (btn.RenderTransform is RotateTransform rt) rt.Angle = 0;
                }
            }
            SaveCurrentAppOrder();
        }

        private void Grid_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            ExitEditMode();
        }

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
            if (_dragAdorner != null)
            {
                _adornerLayer.Remove(_dragAdorner);
                _dragAdorner = null;
            }

            if (_draggedButton != null)
            {
                _draggedButton.Opacity = 1.0;
                _draggedButton = null;
            }
            _isDragging = false;
        }

        private void Button_DragLeave(object sender, DragEventArgs e) { }

        private void Button_Drop(object sender, DragEventArgs e)
        {
            if (!_isEditMode) return;

            var targetButton = sender as Wpf.Ui.Controls.Button;
            var sourceTag = e.Data.GetData(typeof(string)) as string;

            if (targetButton != null && sourceTag != null && targetButton.Tag as string != sourceTag)
            {
                if (targetButton.Tag as string == AddAppTag) return;

                int oldIndex = -1;
                int newIndex = -1;
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

                    var storyboard = (Storyboard)FindResource("ShakeAnimation");
                    storyboard.Begin(sourceBtn, true);
                }
            }
        }

        private void DeleteApp(string appTag)
        {
            var res = CustomMessageBox.Show("Вы уверены что хотите удалить приложение?", "", System.Windows.MessageBoxButton.YesNo);
            if (res == CustomMessageBox.MessageBoxResult.Yes)
            {
                Wpf.Ui.Controls.Button btnToRemove = null;
                foreach (var child in AppsWrapPanel.Children)
                {
                    if (child is Wpf.Ui.Controls.Button btn && btn.Tag as string == appTag)
                    {
                        btnToRemove = btn;
                        break;
                    }
                }

                if (btnToRemove != null)
                {
                    var storyboard = (Storyboard)FindResource("ShakeAnimation");
                    storyboard.Stop(btnToRemove);
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
                if (child is Wpf.Ui.Controls.Button btn)
                {
                    string tag = btn.Tag as string;
                    if (tag == AddAppTag) continue;
                    var appData = _loadedApps.FirstOrDefault(a => a.Name == tag);
                    if (appData != null) newOrderList.Add(appData);
                }
            }
            _loadedApps = newOrderList;
            try
            {
                string json = JsonSerializer.Serialize(_loadedApps, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(JsonPath, json);
            }
            catch (Exception ex) { System.Windows.MessageBox.Show($"Ошибка сохранения порядка: {ex.Message}"); }
        }

        private void ConfirmChanges_Click(object sender, RoutedEventArgs e)
        {
            ExitEditMode();
        }

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

                try
                {
                    var converter = new Helpers.StringToImageSourceConverter();
                    var imgSource = converter.Convert(selectedApp.PreviewPath, typeof(ImageSource), null, System.Globalization.CultureInfo.CurrentCulture) as ImageSource;

                    if (imgSource != null)
                    {
                        DashboardBackground.Source = imgSource;
                        ((Storyboard)this.Resources["FadeInBg"]).Begin();
                    }
                }
                catch { }
                
                LoadingOverlay.Visibility = Visibility.Visible;
                ((Storyboard)this.Resources["FadeInLoading"]).Begin();

                try
                {
                    _navigationService.Navigate(typeof(AppPage));
                    var page = App.Services.GetService(typeof(AppPage)) as AppPage;

                    if (page != null)
                    {
                        await page.ViewModel.InitializeAsync(
                            selectedApp.Title,
                            selectedApp.Description ?? "",
                            selectedApp.IconPath ?? "",
                            selectedApp.PreviewPath ?? "",
                            selectedApp.DownloadUrl
                        );
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
                    Effect = new System.Windows.Media.Effects.DropShadowEffect
                    {
                        BlurRadius = 10,
                        ShadowDepth = 5,
                        Opacity = 0.5,
                        Color = Colors.Black
                    }
                };
            }

            public void UpdatePosition(Point currentPosition)
            {
                _offsetLeft = currentPosition.X;
                _offsetTop = currentPosition.Y;
                if (Parent is AdornerLayer layer)
                {
                    layer.Update(AdornedElement);
                }
            }

            protected override Size MeasureOverride(Size constraint)
            {
                _child.Measure(constraint);
                return _child.DesiredSize;
            }

            protected override Size ArrangeOverride(Size finalSize)
            {
                _child.Arrange(new Rect(finalSize));
                return finalSize;
            }

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