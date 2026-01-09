using Helinstaller.ViewModels.Windows;
using Microsoft.Win32;
using NAudio.Dsp;
using NAudio.Wave;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using Wpf.Ui;
using Wpf.Ui.Abstractions;
using Wpf.Ui.Appearance;
using Wpf.Ui.Controls;
using Path = System.IO.Path;

namespace Helinstaller.Views.Windows
{
    public partial class MainWindow : INavigationWindow
    {
        // Аудио компоненты
        private IWavePlayer? _waveOut;
        private VisualizationProvider? _visProvider;
        private MediaFoundationReader? _mediaReader;

        // Плейлист и навигация
        private List<string> _playlist = new List<string>();
        private int _currentTrackIndex = 0;

        // Контроль потоков
        private readonly SemaphoreSlim _playerLock = new SemaphoreSlim(1, 1);
        private bool _isClosed = false;
        private bool _isLoading = false;

        // Графика визуализатора
        private System.Windows.Shapes.Rectangle[] _barShapes;
        private float[] _currentValues;
        private float[] _smoothedValues;

        // Поля для анимации UI
        private bool _socialExpanded = false;
        private bool _isRenderingSubscribed = false;

        public MainWindowViewModel ViewModel { get; }

        public MainWindow(
            MainWindowViewModel viewModel,
            INavigationViewPageProvider navigationViewPageProvider,
            INavigationService navigationService
        )
        {
            ViewModel = viewModel;
            DataContext = this;
            SystemThemeWatcher.Watch(this);

            InitializeComponent();
            SetPageService(navigationViewPageProvider);
            navigationService.SetNavigationControl(RootNavigation);
        }

        // Класс для чтения ответа от GitHub API
        public class GitHubFile
        {
            public string name { get; set; } = "";
            public string download_url { get; set; } = "";
        }

        // === Инициализация Плеера ===
        private async void InitializePlayer()
        {
            // --- НАСТРОЙКИ ---
            string user = "Helitop";
            string repo = "Heli-Music";
            string path = "";
            string apiUrl = $"https://api.github.com/repos/{user}/{repo}/contents/{path}";

            try
            {
                using (HttpClient client = new HttpClient())
                {
                    client.DefaultRequestHeaders.UserAgent.ParseAdd("HelinstallerApp");
                    string json = await client.GetStringAsync(apiUrl);
                    var files = System.Text.Json.JsonSerializer.Deserialize<List<GitHubFile>>(json);

                    if (files != null)
                    {
                        foreach (var file in files)
                        {
                            if (!string.IsNullOrEmpty(file.download_url) &&
                               (file.name.EndsWith(".mp3") || file.name.EndsWith(".wav") || file.name.EndsWith(".ogg")))
                            {
                                _playlist.Add(file.download_url);
                            }
                        }
                    }
                }
                playerBadge.Visibility = Visibility.Visible;
            }
            catch (Exception ex)
            {
                SongTitle.Text = "Ошибка получения списка";
                System.Diagnostics.Debug.WriteLine(ex.Message);
            }

            if (!_playlist.Any())
            {
                SongTitle.Text = "Плейлист пуст!";
                playerBadge.Visibility = Visibility.Collapsed;
                return;
            }

            // Перемешивание
            var rng = new Random();
            _playlist = _playlist.OrderBy(a => rng.Next()).ToList();

            // Инициализация WaveOut (устройство вывода)
            _waveOut = new WaveOutEvent { Volume = 0.05f };
            _waveOut.PlaybackStopped += OnPlaybackStopped;

            InitializeVisualizerUI();

            // Загружаем первый трек асинхронно
            await LoadTrackAsync(_currentTrackIndex);
        }

        // === Метод загрузки трека (АСИНХРОННЫЙ) ===
        private async Task LoadTrackAsync(int index)
        {
            if (_isClosed) return;

            // Блокируем одновременные вызовы (чтобы не наслаивались загрузки)
            await _playerLock.WaitAsync();

            _isLoading = true;
            songProgress.Value = 0;
            songProgress.IsIndeterminate = true;
            UpdateControlsState(false); // Отключаем кнопки

            try
            {
                // UI: Показываем статус
                SongTitle.Text = "...";
                PlayPauseButton.Content = new ProgressRing() {IsIndeterminate = true, IsHitTestVisible = false, Width = 20, Height = 20, Foreground = songProgress.Foreground };

                string trackUrl = "";
                if (index >= 0 && index < _playlist.Count)
                {
                    trackUrl = _playlist[index];
                }
                else
                {
                    SongTitle.Text = "Ошибка индекса";
                    return;
                }

                // --- ФОНОВАЯ ЗАДАЧА (HEAVY LIFTING) ---
                // Создаем ридер и провайдер в отдельном потоке, чтобы UI не вис
                var result = await Task.Run(() =>
                {
                    try
                    {
                        // 1. Останавливаем воспроизведение (безопасно)
                        _waveOut?.Stop();

                        // 2. Очищаем старые ресурсы
                        _visProvider = null;
                        _mediaReader?.Dispose();
                        _mediaReader = null;

                        // 3. Создаем новые (ЭТО САМАЯ ДОЛГАЯ ОПЕРАЦИЯ)
                        var reader = new MediaFoundationReader(trackUrl);
                        var sampleProvider = reader.ToSampleProvider();
                        var visProvider = new VisualizationProvider(sampleProvider);

                        return (reader, visProvider, null as Exception);
                    }
                    catch (Exception ex)
                    {
                        return (null, null, ex);
                    }
                });

                // --- ВОЗВРАТ В UI ПОТОК ---
                if (result.Item3 != null)
                {
                    throw result.Item3;
                }

                _mediaReader = result.Item1;
                _visProvider = result.Item2;

                if (_waveOut != null && _visProvider != null)
                {
                    _waveOut.Init(_visProvider);

                    // === ДОБАВИТЬ ЭТО ===
                    // Устанавливаем длину прогресс-бара в секундах
                    songProgress.Minimum = 0;
                    songProgress.Maximum = _mediaReader.TotalTime.TotalSeconds;
                    songProgress.Value = 0;
                    // ===================

                    UpdateVisualizerColor(trackUrl);

                    // Обновляем текст
                    string cleanName = System.Net.WebUtility.UrlDecode(System.IO.Path.GetFileNameWithoutExtension(trackUrl));
                    SongTitle.Text = cleanName;
                    songProgress.IsIndeterminate = false;
                    // Запускаем
                    _waveOut.Play();
                    PlayPauseButton.Content = PlayIcon;
                    PlayIcon.Symbol = SymbolRegular.Pause48;
                    SubscribeToRendering();
                }
            }
            catch (Exception ex)
            {
                SongTitle.Text = "Ошибка: " + ex.Message;
                PlayPauseButton.Content = PlayIcon;
                PlayIcon.Symbol = SymbolRegular.ErrorCircle24;
                _visProvider = null; // Маркер ошибки для кнопок
            }
            finally
            {
                _isLoading = false;
                UpdateControlsState(true); // Включаем кнопки
                _playerLock.Release();
            }
        }

        private void UpdateControlsState(bool isEnabled)
        {
            // Можно блокировать кнопки, чтобы юзер не спамил
            // Если кнопки NextButton/PrevButton имеют имена в XAML, раскомментируйте:
            // NextButton.IsEnabled = isEnabled;
            // PrevButton.IsEnabled = isEnabled;
        }

        // === Обработчики событий плеера ===
        private void OnPlaybackStopped(object? sender, StoppedEventArgs e)
        {
            if (_isClosed) return;

            // Событие происходит из аудио-потока, поэтому нужно использовать Dispatcher
            Application.Current.Dispatcher.Invoke(async () =>
            {
                // Проверяем, что это естественное окончание трека, а не мы его стопнули для загрузки нового
                // MediaFoundationReader выставляет Position = Length в конце
                if (!_isLoading && _mediaReader != null)
                {
                    bool atEnd = false;
                    try
                    {
                        if (_mediaReader.Position >= _mediaReader.Length - 1000) atEnd = true;
                    }
                    catch { }

                    if (atEnd || e.Exception == null)
                    {
                        await NextButton_Click_Async();
                    }
                }
            });
        }

        private void PlayPauseButton_Click(object sender, RoutedEventArgs e)
        {
            if (_isLoading || _waveOut == null || _visProvider == null) return;

            if (_waveOut.PlaybackState == PlaybackState.Playing)
            {
                _waveOut.Pause();
                PlayPauseButton.Content = PlayIcon;
                PlayIcon.Symbol = SymbolRegular.Play48;
                UnsubscribeFromRendering();
            }
            else
            {
                _waveOut.Play();
                PlayPauseButton.Content = PlayIcon;
                PlayIcon.Symbol = SymbolRegular.Pause48;
                SubscribeToRendering();
            }
        }

        // Делаем обертку для Next клика
        private async void NextButton_Click(object? sender, RoutedEventArgs? e)
        {
            await NextButton_Click_Async();
        }

        private async Task NextButton_Click_Async()
        {
            try
            {
                if (_isLoading) return;
                _currentTrackIndex = (_currentTrackIndex + 1) % _playlist.Count;
                await LoadTrackAsync(_currentTrackIndex);
            }
            catch (Exception ex)
            { SongTitle.Text = ex.Message; }
        }

        private async void PrevButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (_isLoading) return;
                _currentTrackIndex = (_currentTrackIndex - 1 + _playlist.Count) % _playlist.Count;
                await LoadTrackAsync(_currentTrackIndex);
            }
            catch (Exception ex)
            { SongTitle.Text = ex.Message; }
        }

        // --- ЛОГИКА ЦВЕТОВ ---
        public enum VisualizerStyle
        {
            Default, Cyberpunk, BlueYellow, Edgerunners
        }

        private void UpdateVisualizerColor(string filename)
        {
            VisualizerStyle style = VisualizerStyle.Default;
            string lowerName = System.IO.Path.GetFileName(filename).ToLower();
            // Определение стиля
            if (lowerName.Contains("johnny") || lowerName.Contains("ballad") || lowerName.Contains("anthem"))
                style = VisualizerStyle.BlueYellow;
            else if (lowerName.Contains("edgerunners") || lowerName.Contains("rat") || lowerName.Contains("house"))
                style = VisualizerStyle.Edgerunners;
            else if (lowerName.Contains("cyber") || lowerName.Contains("sneak") || lowerName.Contains("rebel") || lowerName.Contains("synth") || lowerName.Contains("phantom") || lowerName.Contains("samurai"))
                style = VisualizerStyle.Cyberpunk;

            FontFamily font = this.FontFamily;
            LinearGradientBrush gradient = new LinearGradientBrush { StartPoint = new Point(0, 1), EndPoint = new Point(0, 0) };

            switch (style)
            {
                case VisualizerStyle.Cyberpunk:
                    gradient.GradientStops.Add(new GradientStop(Color.FromRgb(180, 40, 100), 0.3));
                    gradient.GradientStops.Add(new GradientStop(Color.FromRgb(100, 255, 240), 0.8));
                    gradient.GradientStops.Add(new GradientStop(Color.FromRgb(150, 255, 255), 1.0));
                    font = new FontFamily(new Uri("pack://application:,,,/"), "./Assets/voyage-fantastique-4.ttf#Voyage Fantastique Condensed");
                    break;
                case VisualizerStyle.BlueYellow:
                    gradient.GradientStops.Add(new GradientStop(Color.FromRgb(40, 90, 200), 0.0));
                    gradient.GradientStops.Add(new GradientStop(Color.FromRgb(255, 220, 80), 0.7));
                    gradient.GradientStops.Add(new GradientStop(Color.FromRgb(255, 255, 120), 1.0));
                    font = new FontFamily(new Uri("pack://application:,,,/"), "./Assets/voyage-fantastique-4.ttf#Voyage Fantastique Condensed");
                    break;
                case VisualizerStyle.Edgerunners:
                    gradient.GradientStops.Add(new GradientStop(Color.FromRgb(142, 241, 27), 0.0));
                    gradient.GradientStops.Add(new GradientStop(Color.FromRgb(207, 252, 3), 0.7));
                    gradient.GradientStops.Add(new GradientStop(Color.FromRgb(234, 234, 19), 1.0));
                    font = new FontFamily(new Uri("pack://application:,,,/Assets"), "./Assets/voyage-fantastique-4.ttf#Voyage Fantastique Condensed");
                    break;
                case VisualizerStyle.Default:
                default:
                    // === ВОТ ЗДЕСЬ БЕРЕМ СИСТЕМНЫЙ ЦВЕТ ===
                    // Создаем градиент из одного цвета (системного), чтобы он был совместим с остальной логикой
                    Color sysColor = SystemColors.AccentColor;
                    gradient.GradientStops.Add(new GradientStop(sysColor, 0.0));
                    gradient.GradientStops.Add(new GradientStop(sysColor, 1.0));
                    font = this.FontFamily;
                    break;
            }
            // 1. Полоски эквалайзера
            if (_barShapes != null)
            {
                foreach (var rect in _barShapes)
                {
                    if (style == VisualizerStyle.Default)
                        // Для дефолта лучше использовать ResourceReference (чтобы менялось при смене темы винды на лету)
                        rect.SetResourceReference(Shape.FillProperty, SystemColors.AccentColorBrushKey);
                    else
                        rect.Fill = gradient;
                }
            }

            // 2. Прогресс бар (Крутим)
            // Тут используем gradient (в котором теперь лежит системный цвет для Default)
            var progressGradient = gradient.Clone();
            progressGradient.RelativeTransform = new RotateTransform { Angle = 90, CenterX = 0.5, CenterY = 0.5 };
            songProgress.Foreground = progressGradient;
            SongTitle.FontFamily = font;

            // 3. Бордер и фон
            if (style == VisualizerStyle.Default)
            {
                // === ДЕФОЛТНОЕ ПОВЕДЕНИЕ ===
                // Если хотите рамку системного цвета:
                // HubCard.BorderBrush = gradient; 
                
                // Если хотите убрать рамку в дефолте (как было в старом коде):
                HubCard.BorderBrush = Brushes.Transparent;

                // Убираем фон и маску шума
                HubBorder.Background = Brushes.Transparent;
                HubBorder.OpacityMask = null;

                SongTitle.Foreground = this.Foreground;
                SongTitle.FontSize = this.FontSize;
            }
            else
            {
                SongTitle.Foreground = progressGradient;
                
                SongTitle.FontSize = 24;
                // === ЦВЕТНЫЕ СТИЛИ С АНИМАЦИЕЙ ===
                HubBorder.Background = gradient;

                // Анимация шума
                var uri = new Uri("pack://application:,,,/Assets/noise.png");
                var bitmap = new BitmapImage(uri);
                var bgBrush = new ImageBrush(bitmap);

                bgBrush.TileMode = TileMode.Tile;
                bgBrush.Viewport = new Rect(0, 0, 1, 2);
                bgBrush.ViewportUnits = BrushMappingMode.RelativeToBoundingBox;

                var translate = new TranslateTransform();
                bgBrush.RelativeTransform = translate;

                var animation = new DoubleAnimation
                {
                    From = 0,
                    To = 1,
                    Duration = TimeSpan.FromSeconds(10),
                    RepeatBehavior = RepeatBehavior.Forever
                };
                translate.BeginAnimation(TranslateTransform.YProperty, animation);

                HubBorder.OpacityMask = bgBrush;
            }
        }

        private void InitializeVisualizerUI()
        {
            VisualizerCanvas.Children.Clear();
            int bands = 96;
            _barShapes = new System.Windows.Shapes.Rectangle[bands];
            _currentValues = new float[bands];
            _smoothedValues = new float[bands];
            for (int i = 0; i < bands; i++)
            {
                var rect = new System.Windows.Shapes.Rectangle
                {
                    RadiusX = 3,
                    RadiusY = 3,
                    Width = 6,
                    Height = 1,
                    Fill = Brushes.Transparent
                };
                VisualizerCanvas.Children.Add(rect);
                _barShapes[i] = rect;
            }
        }

        // === Логика Визуализатора (Отрисовка) ===
        private void OnRendering(object? sender, EventArgs e)
        {
            if (_mediaReader != null && !_isLoading && _waveOut?.PlaybackState == PlaybackState.Playing)
            {
                // Обновляем значение полоски (текущее время в секундах)
                songProgress.Value = _mediaReader.CurrentTime.TotalSeconds;
            }
            // ===================

            if (_visProvider == null || _visProvider.FftData == null || _barShapes == null) return;

            float[] fft = _visProvider.FftData;
            double w = VisualizerCanvas.ActualWidth;
            double h = VisualizerCanvas.ActualHeight;
            if (w <= 0 || h <= 0) return;

            int bands = _barShapes.Length;
            int half = bands / 2;
            double barWidth = w / bands;
            double gap = 2;
            double actualWidth = Math.Max(3, barWidth - gap);

            float attack = 0.9f;
            float release = 0.2f;
            float ema = 0.3f;
            double maxMultiplier = 0.50;

            for (int i = 0; i < half; i++)
            {
                double t = 1 - (double)i / half;
                int fftIndex = (int)(Math.Pow(t, 2.0) * (fft.Length - 1));
                float raw = fftIndex < fft.Length ? fft[fftIndex] : 0f;

                raw = MathF.Log10(1 + raw * 70) * 0.5f;
                raw = Math.Clamp(raw, 0, 3.0f);

                UpdateSingleBar(i, raw, h, barWidth, gap, actualWidth, attack, release, ema, maxMultiplier);
                UpdateSingleBar(bands - 1 - i, raw, h, barWidth, gap, actualWidth, attack, release, ema, maxMultiplier);
            }
        }

        private void UpdateSingleBar(int index, float raw, double h, double barWidth, double gap, double actualWidth,
                                     float attack, float release, float ema, double maxMultiplier)
        {
            float current = _currentValues[index];
            if (raw > current) current += (raw - current) * attack;
            else current += (raw - current) * release;
            _currentValues[index] = current;

            _smoothedValues[index] += (current - _smoothedValues[index]) * ema;
            double barHeight = _smoothedValues[index] * h * maxMultiplier;

            var rect = _barShapes[index];
            rect.Width = actualWidth;
            rect.Height = Math.Max(3, barHeight);
            Canvas.SetLeft(rect, index * barWidth + gap / 2);
            Canvas.SetBottom(rect, 0);
        }

        private void SubscribeToRendering()
        {
            if (!_isRenderingSubscribed)
            {
                CompositionTarget.Rendering += OnRendering;
                _isRenderingSubscribed = true;
            }
        }

        private void UnsubscribeFromRendering()
        {
            if (_isRenderingSubscribed)
            {
                CompositionTarget.Rendering -= OnRendering;
                _isRenderingSubscribed = false;
            }
        }

        // === UI События ===
        private void SocialToggleButton_Click(object sender, RoutedEventArgs e)
        {
            SocialToggleButton.Appearance = ControlAppearance.Secondary;
            double targetWidth = _socialExpanded ? 0 : 600;
            double targetOpacity = _socialExpanded ? 0 : 1;
            var widthAnim = new DoubleAnimation
            {
                To = targetWidth,
                Duration = TimeSpan.FromMilliseconds(400),
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
            };
            var opacityAnim = new DoubleAnimation
            {
                To = targetOpacity,
                Duration = TimeSpan.FromMilliseconds(350)
            };
            SocialPanelContainer.BeginAnimation(FrameworkElement.WidthProperty, widthAnim);
            SocialPanel.BeginAnimation(UIElement.OpacityProperty, opacityAnim);
            _socialExpanded = !_socialExpanded;
        }

        private void SocialLink_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Wpf.Ui.Controls.Button btn)
            {
                string? url = btn.CommandParameter as string ?? btn.Tag as string;
                if (!string.IsNullOrWhiteSpace(url))
                {
                    try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); } catch { }
                }
            }
        }

        private async void FluentWindow_Initialized(object sender, EventArgs e)
        {
            var connectionTask = ConnectionCheck();

            string userName = Environment.UserName;
            string greeting;
            int hour = DateTime.Now.Hour;
            if (hour >= 5 && hour < 12) greeting = "Доброе утро";
            else if (hour >= 12 && hour < 17) greeting = "Добрый день";
            else if (hour >= 17 && hour < 23) greeting = "Добрый вечер";
            else greeting = "Доброй ночи";

            Message.Text = $"{greeting}, {userName}!";
            User.Text = userName;
            Picture.Source = GetUserAvatar();

            await connectionTask;
            InitializePlayer();
            if (!ThemeChanger.IsSystemInDarkMode())
            {
                var res = CustomMessageBox.Show("Рекомендуется использовать тёмную тему.", "", System.Windows.MessageBoxButton.YesNo);
                if (res == CustomMessageBox.MessageBoxResult.Yes) ThemeChanger.ToggleWindowsTheme();
            }
            var updateItem = this.RootNavigation.FooterMenuItems
            .OfType<NavigationViewItem>()
            .FirstOrDefault(x => x.Content?.ToString() == "Обновления");
            updateItem.Click += UpdateItem_Click;
        }
        private Version GetAssemblyVersion()
        {
            return System.Reflection.Assembly
                .GetExecutingAssembly()
                .GetName()
                .Version
                ?? new Version(0, 0, 0, 0);
        }

        private async void UpdateItem_Click(object sender, RoutedEventArgs e)
        {
            if (sender is NavigationViewItem item)
            {
                await CheckUpdatesForItem(item);
            }
        }

        private async Task CheckUpdatesForItem(NavigationViewItem updateItem)
        {
            try
            {
                updateItem.IsEnabled = false;
                updateItem.Content = "Проверка обновлений...";

                Version currentVersion = GetAssemblyVersion();


                // 1️⃣ Получаем последний релиз с GitHub
                using var http = new HttpClient();
                http.DefaultRequestHeaders.UserAgent.ParseAdd("Helinstaller");
                string releasesJson = await http.GetStringAsync(
                    "https://api.github.com/repos/Helitop/Helinstaller/releases/latest");

                using var doc = JsonDocument.Parse(releasesJson);
                var tagName = doc.RootElement.GetProperty("tag_name").GetString();
                if (string.IsNullOrWhiteSpace(tagName)) return;

                var match = Regex.Match(tagName, @"\d+(\.\d+)+");
                if (!match.Success) return;

                Version latestVersion = new Version(match.Value);

                if (latestVersion <= currentVersion)
                {
                    updateItem.Content = "Обновлений нет";
                    return;
                }

                updateItem.Content = $"Найдено {latestVersion}";

                // 2️⃣ Находим ZIP в релизе
                var assets = doc.RootElement.GetProperty("assets");
                string zipUrl = null;

                foreach (var asset in assets.EnumerateArray())
                {
                    var name = asset.GetProperty("name").GetString();
                    if (name != null && name.EndsWith("Helinstaller.zip"))
                    {
                        zipUrl = asset.GetProperty("browser_download_url").GetString();
                        break;
                    }
                }

                if (zipUrl == null)
                {
                    updateItem.Content = "ZIP не найден";
                    return;
                }

                // 3️⃣ Скачиваем ZIP
                string tempFile = Path.Combine(Path.GetTempPath(), "Helinstaller_update.zip");
                using (var fs = new FileStream(tempFile, FileMode.Create, FileAccess.Write, FileShare.None))
                {
                    var stream = await http.GetStreamAsync(zipUrl);
                    await stream.CopyToAsync(fs);
                }

                string tempExtract = Path.Combine(Path.GetTempPath(), "Helinstaller_update");
                if (Directory.Exists(tempExtract))
                    Directory.Delete(tempExtract, true);
                ZipFile.ExtractToDirectory(tempFile, tempExtract);

                // Путь к новой версии
                string newAppFolder = Path.Combine(tempExtract, "Helinstaller Packed");

                // 4️⃣ Запускаем PowerShell для замены текущей версии и перезапуска
                string currentExe = Process.GetCurrentProcess().MainModule!.FileName;
                string currentDir = Path.GetDirectoryName(currentExe);

                // Сценарий PowerShell
                string psScript = $@"
            Start-Sleep -Milliseconds 500;
            Remove-Item -Recurse -Force '{currentDir}\*';
            Copy-Item -Recurse -Force '{newAppFolder}\*' '{currentDir}';
            Start-Process '{currentExe}';
        ";

                string psFile = Path.Combine(Path.GetTempPath(), "update.ps1");
                await File.WriteAllTextAsync(psFile, psScript);

                // Запуск PowerShell
                ProcessStartInfo psi = new ProcessStartInfo("powershell", $"-NoProfile -ExecutionPolicy Bypass -File \"{psFile}\"")
                {
                    UseShellExecute = true,
                    WindowStyle = ProcessWindowStyle.Hidden
                };
                Process.Start(psi);

                // Заканчиваем текущую программу
                Application.Current.Shutdown();
            }
            catch (Exception ex)
            {
                updateItem.Content = $"Ошибка: {ex.Message}";
                updateItem.IsEnabled = true;
            }
        }


        public static async Task<(bool IsUpdateAvailable, Version? LatestVersion)>
        CheckUpdates(string owner, string repo, Version currentVersion)
        {
            using var http = new HttpClient();
            http.DefaultRequestHeaders.UserAgent.ParseAdd("Helinstaller");

            var url = $"https://api.github.com/repos/{owner}/{repo}/tags";
            var json = await http.GetStringAsync(url);

            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.GetArrayLength() == 0)
                return (false, null);

            var tagName = doc.RootElement[0].GetProperty("name").GetString();

            if (string.IsNullOrWhiteSpace(tagName))
                return (false, null);

            // выдёргиваем версию из тега (v1.2.3 → 1.2.3)
            var match = Regex.Match(tagName, @"\d+(\.\d+)+");
            if (!match.Success)
                return (false, null);

            var latestVersion = new Version(match.Value);

            return (latestVersion > currentVersion, latestVersion);
        }

        // === Вспомогательные классы и методы ===
        public static class ThemeChanger
        {
            private const string PersonalizeKey = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";
            private const string SystemUsesLightThemeKey = "SystemUsesLightTheme";
            private const string AppsUseLightThemeKey = "AppsUseLightTheme";

            [DllImport("user32.dll", SetLastError = true)]
            private static extern int SendMessageTimeout(IntPtr hWnd, uint Msg, IntPtr wParam, string lParam,
                uint fuFlags, uint uTimeout, out IntPtr lpdwResult);
            private const uint WM_SETTINGCHANGE = 0x001A;
            private const uint SMTO_ABORTIFHUNG = 0x0002;

            public static bool IsSystemInDarkMode()
            {
                try
                {
                    using (RegistryKey key = Registry.CurrentUser.OpenSubKey(PersonalizeKey))
                    {
                        object value = key?.GetValue(SystemUsesLightThemeKey);
                        return value != null && value is int intValue && intValue == 0;
                    }
                }
                catch { return false; }
            }

            public static void ToggleWindowsTheme()
            {
                bool isDark = IsSystemInDarkMode();
                int newSystemValue = isDark ? 1 : 0;
                int newAppsValue = newSystemValue;

                try
                {
                    using (RegistryKey key = Registry.CurrentUser.CreateSubKey(PersonalizeKey, true))
                    {
                        key.SetValue(SystemUsesLightThemeKey, newSystemValue, RegistryValueKind.DWord);
                        key.SetValue(AppsUseLightThemeKey, newAppsValue, RegistryValueKind.DWord);
                    }
                    SendMessageTimeout(new IntPtr(0xFFFF), WM_SETTINGCHANGE, IntPtr.Zero, "ImmersiveColorSet", SMTO_ABORTIFHUNG, 100, out _);

                    isDark = IsSystemInDarkMode();
                    if (!isDark) ApplicationThemeManager.Apply(ApplicationTheme.Light);
                    if (isDark) ApplicationThemeManager.Apply(ApplicationTheme.Dark);
                }
                catch (Exception ex)
                {
                    CustomMessageBox.Show($"Ошибка при переключении темы: {ex.Message}", "", System.Windows.MessageBoxButton.OK);
                }
            }
        }

        private async Task ConnectionCheck()
        {
            // Список целей для проверки: (URL, Отображаемое имя)
            var targets = new[]
            {
        ("https://google.com", "Интернет", 1),
        ("https://github.com", "GitHub", 2),
        ("https://massgrave.dev", "API Massgrave", 3)
    };

            bool allConnected = true;

            // Используем один HttpClient для всех запросов, чтобы не создавать лишние сокеты
            using (var client = new HttpClient())
            {
                client.Timeout = TimeSpan.FromSeconds(15); // Таймаут на каждый запрос

                foreach (var (url, name, i) in targets)
                {
                    desc.Text = $"Подключение: {name}";
                    bool targetSuccess = false;
                    int attempts = 0;

                    // Пытаемся подключиться к конкретному сайту до 3 раз
                    while (attempts < 3)
                    {
                        try
                        {
                            loadingBar.Value = i;
                            // Используем SendAsync с HEAD, чтобы скачивать только заголовки (быстрее), 
                            // или GetAsync, если сервер не поддерживает HEAD.
                            var response = await client.GetAsync(url);
                            response.EnsureSuccessStatusCode();

                            targetSuccess = true;
                            break; // Успех, выходим из цикла попыток
                        }
                        catch (Exception ex)
                        {
                            attempts++;
                            // Если это последняя попытка и она неудачная - выводим ошибку
                            if (attempts >= 3)
                            {
                                info.Text = ex.Message; // Показываем ошибку
                            }
                            await Task.Delay(500); // Небольшая пауза перед повторной попыткой
                        }
                    }

                    if (!targetSuccess)
                    {
                        // Если хоть один сервис не ответил
                        allConnected = false;
                        desc.Text = $"Ошибка: {name}";
                        stat.Symbol = SymbolRegular.CloudError48;
                        stat.Visibility = Visibility.Visible;
                        ring.Visibility = Visibility.Collapsed;
                        var updateItem = this.RootNavigation.FooterMenuItems
                        .OfType<NavigationViewItem>()
                        .FirstOrDefault(x => (string?)x.Tag == "updates");

                        if (updateItem != null)
                            await CheckUpdatesForItem(updateItem); // ✅ Task, await можно использовать

                        // Прерываем проверку, так как цепочка нарушена
                        // (например, если нет интернета, нет смысла проверять GitHub)
                        break;
                    }
                }
            }

            if (allConnected)
            {
                desc.Text = "Подключено!";
                ring.Visibility = Visibility.Collapsed;
                stat.Visibility = Visibility.Visible;
                stat.Symbol = SymbolRegular.CloudCheckmark48;

                // Задержка, чтобы пользователь успел увидеть галочку "ОК" перед входом
                await Task.Delay(500);

                // Анимация входа
                MainGrid.Visibility = Visibility.Visible;
                DoubleAnimation fadeIn = new DoubleAnimation(0.0, 1.0, TimeSpan.FromSeconds(1));
                DoubleAnimation fadeOut = new DoubleAnimation(1.0, 0.0, TimeSpan.FromSeconds(1));

                info.Visibility = Visibility.Collapsed;
                fadeOut.Completed += (s, _) => LoadingPanel.Visibility = Visibility.Collapsed;

                MainGrid.BeginAnimation(Grid.OpacityProperty, fadeIn);
                LoadingPanel.BeginAnimation(Grid.OpacityProperty, fadeOut);
            }
            else
            {
                // Логика на случай провала:
                // Либо оставляем висеть ошибку, либо пускаем в приложение с ограничениями.
                // Если нужно пустить в приложение даже при ошибке, раскомментируй код ниже:
                

                await Task.Delay(2000); // Даем прочитать ошибку
                MainGrid.Visibility = Visibility.Visible;
                DoubleAnimation fadeIn = new DoubleAnimation(0.0, 1.0, TimeSpan.FromSeconds(1));
                DoubleAnimation fadeOut = new DoubleAnimation(1.0, 0.0, TimeSpan.FromSeconds(1));
                fadeOut.Completed += (s, _) => LoadingPanel.Visibility = Visibility.Collapsed;
                MainGrid.BeginAnimation(Grid.OpacityProperty, fadeIn);
                LoadingPanel.BeginAnimation(Grid.OpacityProperty, fadeOut);
                info.BeginAnimation(Grid.OpacityProperty, fadeOut);

            }
        }

        public static BitmapImage? GetUserAvatar()
        {
            string dir = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Microsoft", "Windows", "AccountPicture");
            if (!Directory.Exists(dir)) return null;
            var files = Directory.GetFiles(dir, "user*.png").Concat(Directory.GetFiles(dir, "user*.jpg"))
                .OrderByDescending(f => new FileInfo(f).Length).ToList();
            if (!files.Any()) return null;
            try
            {
                var bmp = new BitmapImage();
                bmp.BeginInit();
                bmp.CacheOption = BitmapCacheOption.OnLoad;
                bmp.UriSource = new Uri(files.First());
                bmp.EndInit();
                bmp.Freeze();
                return bmp;
            }
            catch { return null; }
        }

        #region INavigationWindow methods
        public INavigationView GetNavigation() => RootNavigation;
        public bool Navigate(Type pageType) => RootNavigation.Navigate(pageType);
        public void SetPageService(INavigationViewPageProvider service) => RootNavigation.SetPageProviderService(service);
        public void ShowWindow() => Show();
        public void CloseWindow() => Close();
        #endregion

        protected override void OnClosed(EventArgs e)
        {
            _isClosed = true;
            UnsubscribeFromRendering();
            _waveOut?.Stop();
            _waveOut?.Dispose();
            _mediaReader?.Dispose();
            base.OnClosed(e);
            Application.Current.Shutdown();
        }

        public void SetServiceProvider(IServiceProvider serviceProvider) => throw new NotImplementedException();
        private void info_TextChanged(object sender, TextChangedEventArgs e) { info.Visibility = Visibility.Visible; }
    }

    /// <summary>
    /// Провайдер FFT
    /// </summary>
    public class VisualizationProvider : ISampleProvider
    {
        private readonly ISampleProvider _source;
        private readonly int _fftLength = 1024;
        private readonly int _m;
        private readonly NAudio.Dsp.Complex[] _complexData;
        private readonly float[] _audioBuffer;
        private readonly float[] _fftData;
        private int _bufferPos;
        private readonly int _channels;
        public float[] FftData => _fftData;
        public WaveFormat WaveFormat => _source.WaveFormat;
        public VisualizationProvider(ISampleProvider source)
        {
            _source = source;
            _channels = source.WaveFormat.Channels;
            _m = (int)Math.Log(_fftLength, 2.0);
            _complexData = new NAudio.Dsp.Complex[_fftLength];
            _audioBuffer = new float[_fftLength];
            _fftData = new float[_fftLength / 2];
        }
        public int Read(float[] buffer, int offset, int count)
        {
            int samplesRead = _source.Read(buffer, offset, count);
            for (int i = 0; i < samplesRead; i += _channels)
            {
                if (_bufferPos >= _fftLength)
                {
                    ProcessFft();
                    _bufferPos = 0;
                }
                float sample = buffer[offset + i];
                if (_channels == 2 && (i + 1) < samplesRead)
                {
                    float right = buffer[offset + i + 1];
                    sample = (sample + right) * 0.5f;
                }
                _audioBuffer[_bufferPos++] = sample;
            }
            return samplesRead;
        }
        private void ProcessFft()
        {
            for (int i = 0; i < _fftLength; i++)
            {
                double window = 0.5 * (1.0 - Math.Cos(2.0 * Math.PI * i / (_fftLength - 1)));
                _complexData[i].X = (float)(_audioBuffer[i] * window);
                _complexData[i].Y = 0.0f;
            }
            FastFourierTransform.FFT(true, _m, _complexData);
            for (int i = 0; i < _fftData.Length; i++)
            {
                float real = _complexData[i].X;
                float imag = _complexData[i].Y;
                double magnitude = Math.Sqrt(real * real + imag * imag);
                float val = (float)(magnitude * 200.0);
                if (val < _fftData[i]) _fftData[i] = _fftData[i] * 0.8f;
                else _fftData[i] = val;
            }
        }
    }
}