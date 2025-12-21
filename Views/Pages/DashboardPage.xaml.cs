using Helinstaller.ViewModels.Pages;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents; // Нужно для Adorner
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading; // DispatcherTimer больше не нужен для LongPress, но может пригодиться для UI
using Wpf.Ui;
using Wpf.Ui.Abstractions.Controls;
using Wpf.Ui.Controls;

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
        // private DispatcherTimer _longPressTimer; // УДАЛЕНО: Больше не нужно
        private Wpf.Ui.Controls.Button _draggedButton;

        // Для визуального Adorner'а
        private DragAdorner _dragAdorner;
        private AdornerLayer _adornerLayer;
        private Point _dragOffset;

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
                Width = 140,
                Height = 140,
                Margin = new Thickness(5),
                Appearance = Wpf.Ui.Controls.ControlAppearance.Transparent,
                Tag = AddAppTag
            };
            button.Click += TileButton_Click;

            var grid = new Grid();
            grid.RowDefinitions.Add(new RowDefinition());
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var icon = new Wpf.Ui.Controls.SymbolIcon { Symbol = SymbolRegular.Add48, FontSize = 50 };
            var text = new Wpf.Ui.Controls.TextBlock { Text = "Добавить", FontSize = 16, FontWeight = FontWeights.SemiBold, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 5, 0, 5) };
            Grid.SetRow(icon, 0); grid.Children.Add(icon);
            Grid.SetRow(text, 1); grid.Children.Add(text);
            button.Content = grid;
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
        private Wpf.Ui.Controls.Button CreateAppTile(AppInfo app)
        {

            var button = new Wpf.Ui.Controls.Button
            {
                Width = 140,
                Height = 140,
                Margin = new Thickness(5),
                Appearance = Wpf.Ui.Controls.ControlAppearance.Secondary,
                Tag = app.Name,
                AllowDrop = true
            };
            // --- ДОБАВЛЕНО: Контекстное меню для ПКМ ---
            var contextMenu = new ContextMenu();

            var editItem = new Wpf.Ui.Controls.MenuItem { Header = "Изменить порядок", Icon = new Wpf.Ui.Controls.SymbolIcon { Symbol = SymbolRegular.Edit16 } };
            editItem.Click += (s, e) => EnterEditMode();
            contextMenu.Items.Add(editItem);

            // Можно добавить пункт "Удалить" сразу сюда для удобства
            var deleteItem = new Wpf.Ui.Controls.MenuItem { Header = "Удалить", Icon = new Wpf.Ui.Controls.SymbolIcon { Symbol = SymbolRegular.Delete16 } };
            deleteItem.Click += (s, e) => DeleteApp(app.Name);
            contextMenu.Items.Add(deleteItem);
            button.ContextMenu = contextMenu;
            button.RenderTransformOrigin = new Point(0.5, 0.5);
            button.RenderTransform = new RotateTransform(0);
            button.PreviewMouseLeftButtonDown += Button_PreviewMouseLeftButtonDown;
            button.PreviewMouseLeftButtonUp += Button_PreviewMouseLeftButtonUp;
            button.PreviewMouseMove += Button_PreviewMouseMove;
            button.Drop += Button_Drop;
            button.DragLeave += Button_DragLeave;
            button.Click += TileButton_Click;
            button.MouseEnter += TileButton_MouseEnter;
            button.MouseLeave += TileButton_MouseLeave;
            var grid = new Grid();
            grid.RowDefinitions.Add(new RowDefinition());
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            string fullPath = AppDomain.CurrentDomain.BaseDirectory + app.IconPath;
            var Bitmap = new BitmapImage();
            // 2. Проверяем существование файла. Это важно для предотвращения ошибок.
            if (File.Exists(fullPath))
            {
                // 3. Создаем URI. Используем UriKind.Absolute, так как fullPath - это полный путь.
                Uri fileUri = new Uri(fullPath, UriKind.Absolute);

                // 4. Загружаем изображение
                Bitmap.BeginInit();
                Bitmap.UriSource = fileUri;
                Bitmap.CacheOption = BitmapCacheOption.OnLoad; // Рекомендовано для файлов, чтобы избежать блокировки
                Bitmap.EndInit();
            }
            if (!string.IsNullOrEmpty(app.IconPath))
            {
                var img = new Wpf.Ui.Controls.Image
                {
                    Source = Bitmap,
                    MaxWidth = 80,
                    MaxHeight = 80
                };
                Grid.SetRow(img, 0); grid.Children.Add(img);
            }
            else
            {
                var icon = new Wpf.Ui.Controls.SymbolIcon { Symbol = SymbolRegular.Apps48, FontSize = 80 };
                Grid.SetRow(icon, 0); grid.Children.Add(icon);
            }

            var text = new Wpf.Ui.Controls.TextBlock { Text = app.Title, FontSize = 16, FontWeight = FontWeights.SemiBold, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 5, 0, 5) };
            Grid.SetRow(text, 1); grid.Children.Add(text);
            button.Content = grid;
            return button;
        }

        // --- Логика Edit Mode ---

        private void Button_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            _draggedButton = sender as Wpf.Ui.Controls.Button;
            _startPoint = e.GetPosition(this);
            _dragOffset = e.GetPosition(this);

            // УДАЛЕНО: Старт таймера для Long Press
            /*
            if (!_isEditMode && _draggedButton.Tag as string != AddAppTag)
            {
                _longPressTimer.Start();
            }
            */
        }

        private void Button_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            // УДАЛЕНО: Остановка таймера
            // _longPressTimer.Stop();

            _isDragging = false;
            CleanupDrag();
        }

        // УДАЛЕНО: Метод LongPressTimer_Tick больше не нужен

        private void EnterEditMode()
        {
            if (_isEditMode) return;
            _isEditMode = true;

            // Запуск анимации появления (Кнопка едет вниз, Грид едет вниз)
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

            // Запуск анимации исчезновения (Кнопка едет вверх, Грид едет вверх)
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

        // --- Drag and Drop (Остался без изменений) ---

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

        private void Button_DragLeave(object sender, DragEventArgs e)
        {
        }

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
            var res = CustomMessageBox.Show("Вы уверены что хотите удалить приложение?","", System.Windows.MessageBoxButton.YesNo);
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

        // --- Вспомогательные методы ---

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

                // --- ПЛАВНОЕ ПОЯВЛЕНИЕ ---
                LoadingOverlay.Visibility = Visibility.Visible;
                var fadeIn = (Storyboard)this.Resources["FadeInLoading"];
                var fadeOut = (Storyboard)this.Resources["FadeOutLoading"];
                fadeOut.Begin();
                fadeIn.Begin();


                try
                {
                    ViewModel.OnNavigateToApp(tag);

                    var vm = new AppPageViewmodel
                    {
                        Title = selectedApp.Title,
                        Description = selectedApp.Description,
                        IconPath = AppDomain.CurrentDomain.BaseDirectory + selectedApp.IconPath,
                        PreviewPath = AppDomain.CurrentDomain.BaseDirectory + selectedApp.PreviewPath,
                        DownloadUrl = selectedApp.DownloadUrl,
                        IsInstalling = ViewModel.IsInstalling,
                        ProgressValue = ViewModel.ProgressValue,
                        IsChecking = ViewModel.IsChecking,
                        IsInstalled = ViewModel.IsInstalled,
                    };

                    if (vm.CheckCommand is IAsyncRelayCommand asyncCommand)
                    {
                        // Пока выполняется этот await, пользователь видит плавную загрузку
                        await asyncCommand.ExecuteAsync(null);
                    }
                    fadeIn.Stop();
                    fadeOut.Stop();
                    _navigationService.Navigate(typeof(AppPage), vm);
                    LoadingOverlay.Visibility = Visibility.Collapsed;
                    LoadingOverlay.Opacity = 0;
                    AppsGrid.Opacity = 1;

                }
                catch (Exception ex)
                {
                    fadeIn.Stop();
                    fadeOut.Stop();
                    // Возврат в исходное состояние при ошибке
                    LoadingOverlay.Visibility = Visibility.Collapsed;
                    LoadingOverlay.Opacity = 0;
                    AppsGrid.Opacity = 1;

                    CustomMessageBox.Show($"Ошибка: {ex.Message}");
                }
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