using CommunityToolkit.Mvvm.Messaging;
using Helinstaller.Helpers;
using Helinstaller.Models;
using Helinstaller.ViewModels.Windows;
using Helinstaller.Views.Pages;
using Microsoft.Win32;
using NAudio.Dsp;
using NAudio.Wave;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using Velopack;
using Velopack.Sources;

using Wpf.Ui;
using Wpf.Ui.Abstractions;
using Wpf.Ui.Appearance;
using Wpf.Ui.Controls;
using Path = System.IO.Path;
using TextBlock = Wpf.Ui.Controls.TextBlock;

namespace Helinstaller.Views.Windows
{
    public partial class MainWindow : INavigationWindow
    {
        // Аудио компоненты
        private IWavePlayer? _waveOut;
        private VisualizationProvider? _visProvider;
        private MediaFoundationReader? _mediaReader;
        private bool _globalVisualizerEnabled = true;
        private ScaleTransform _lyricsScale = new ScaleTransform();
        private TranslateTransform _lyricsTranslate = new TranslateTransform();
        private ScaleTransform _backgroundScale = new ScaleTransform();
        // Плейлист и навигация
        private List<string> _playlist = new List<string>();
        private int _currentTrackIndex = 0;
        private float _bassHistory = 0;       // Средняя энергия баса
        private float _beatPulse = 0;         // Текущий уровень пульсации (от 0 до 1)
        // Контроль потоков
        private readonly SemaphoreSlim _playerLock = new SemaphoreSlim(1, 1);
        private bool _isClosed = false;
        private bool _isLoading = false;

        // Графика визуализатора
        private float[] _currentValues;
        private float[] _smoothedValues;

        // Поля для анимации UI
        private bool _socialExpanded = false;
        private bool _isRenderingSubscribed = false;
        private bool _isIslandExpanded = false;
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
            // --- НАСТРОЙКА ТРАНСФОРМАЦИЙ И АНТИМЫЛА ---
            var lyricsGroup = new TransformGroup();
            lyricsGroup.Children.Add(_lyricsScale);
            lyricsGroup.Children.Add(_lyricsTranslate);
            LyricsDisplay.RenderTransform = lyricsGroup;
            LyricsDisplay.RenderTransformOrigin = new Point(0.5, 0.5); // Важно: масштабируем ровно от центра текста

            // Лекарство от мыла при анимации текста в WPF:
            TextOptions.SetTextFormattingMode(LyricsDisplay, TextFormattingMode.Ideal);
            TextOptions.SetTextHintingMode(LyricsDisplay, TextHintingMode.Animated); // Отключаем пиксельную привязку

            var bgGroup = new TransformGroup();
            bgGroup.Children.Add(_backgroundScale);
            HubBorder.RenderTransform = bgGroup;
            HubBorder.RenderTransformOrigin = new Point(0.5, 0.5);
            // ------------------------------------------
            WeakReferenceMessenger.Default.Register<VisualizerStatusChangedMessage>(this, (r, m) =>
            {
                _globalVisualizerEnabled = m.Value;

                if (!_globalVisualizerEnabled)
                {
                    UnsubscribeFromRendering();
                    // Убираем анимацию аватарки в дефолт, если выключено
                    AvatarScale.ScaleX = 1;
                    AvatarScale.ScaleY = 1;
                    AvatarRing.Opacity = 0;
                }
                else
                {
                    if (_waveOut?.PlaybackState == PlaybackState.Playing)
                    {
                        SubscribeToRendering();
                    }
                }
            });

            SetPageService(navigationViewPageProvider);
            navigationService.SetNavigationControl(RootNavigation);
        }
        // Класс для чтения ответа от GitHub API
        public class GitHubFile
        {
            public string name { get; set; } = "";
            public string download_url { get; set; } = "";
        }
        public class LrcLine
        {
            public TimeSpan Time { get; set; }
            public string Text { get; set; } = "";
        }

        // Поля в классе MainWindow
        private List<LrcLine> _currentLyrics = new List<LrcLine>();
        private Dictionary<string, string> _lrcMap = new Dictionary<string, string>(); // URL_песни -> URL_лирики
                                                                                       // === Инициализация Плеера ===
        private async void InitializePlayer()
        {
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
                        // Сначала соберем все аудио и все lrc
                        var audioFiles = files.Where(f => f.name.EndsWith(".mp3") || f.name.EndsWith(".wav") || f.name.EndsWith(".ogg")).ToList();
                        var lrcFiles = files.Where(f => f.name.EndsWith(".lrc")).ToList();

                        foreach (var file in audioFiles)
                        {
                            _playlist.Add(file.download_url);

                            // Ищем лирику: имя файла (без расширения) должно совпадать
                            string baseName = Path.GetFileNameWithoutExtension(file.name);
                            var matchingLrc = lrcFiles.FirstOrDefault(l => Path.GetFileNameWithoutExtension(l.name) == baseName);

                            if (matchingLrc != null)
                            {
                                _lrcMap[file.download_url] = matchingLrc.download_url;
                            }
                        }
                    }
                    playerBadge.Visibility = Visibility.Visible;
                }
            }
            catch (Exception ex)
            {
                SongTitle.Text = "Ошибка сети";
                Debug.WriteLine(ex.Message);
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

            _waveOut = new WaveOutEvent { Volume = 0.05f };
            _waveOut.PlaybackStopped += OnPlaybackStopped;

            await LoadTrackAsync(_currentTrackIndex, Models.AppSettings.IsMusicAutoPlayEnabled);
        }



        // Наведение на невидимую зону в тайтлбаре
        // Общая логика открытия (вызывается и триггером, и самим островком)
        private void ExpandIsland()
        {
            if (_isIslandExpanded) return;
            _isIslandExpanded = true;

            var duration = TimeSpan.FromMilliseconds(400);
            var ease = new CubicEase { EasingMode = EasingMode.EaseOut };

            // Считаем "широкую" цель: либо 320, либо больше, если текст очень длинный
            double textWidth = MeasureStringWidth(LyricsDisplay.Text, LyricsDisplay.FontSize);
            double expandedWidth = Math.Max(320, Math.Min(500, textWidth + 120));

            // Анимация роста
            HubCard.BeginAnimation(WidthProperty, new DoubleAnimation(expandedWidth, duration) { EasingFunction = ease });
            HubCard.BeginAnimation(HeightProperty, new DoubleAnimation(160, duration) { EasingFunction = ease });

            // Показываем плеер
            playerBadge.IsHitTestVisible = true;
            playerBadge.BeginAnimation(OpacityProperty, new DoubleAnimation(1, duration) { BeginTime = TimeSpan.FromMilliseconds(150) });
            // ПЛАВНО УБИРАЕМ БЛЮР
            if (HubBorder.Effect is System.Windows.Media.Effects.BlurEffect blur)
            {
                blur.BeginAnimation(System.Windows.Media.Effects.BlurEffect.RadiusProperty,
                    new DoubleAnimation(0, TimeSpan.FromMilliseconds(400)) { EasingFunction = ease });
            }
        }

        // Событие для невидимого триггера в тайтлбаре
        private void HubTrigger_MouseEnter(object sender, MouseEventArgs e)
        {
            ExpandIsland();
        }

        // Событие для самого островка (чтобы не закрывался при переходе курсора)
        private void HubCard_MouseEnter(object sender, MouseEventArgs e)
        {
            ExpandIsland();
        }

        // Событие ухода мыши
        private async void HubCard_MouseLeave(object sender, MouseEventArgs e)
        {
            await Task.Delay(100);
            if (HubCard.IsMouseOver || HubTrigger.IsMouseOver) return;

            _isIslandExpanded = false;

            var duration = TimeSpan.FromMilliseconds(300);
            var ease = new CubicEase { EasingMode = EasingMode.EaseInOut };

            playerBadge.IsHitTestVisible = false;
            if (HubBorder.Effect is System.Windows.Media.Effects.BlurEffect blur)
            {
                blur.BeginAnimation(System.Windows.Media.Effects.BlurEffect.RadiusProperty,
                    new DoubleAnimation(20, TimeSpan.FromMilliseconds(300)) { EasingFunction = ease });
            }
            // Считаем ширину чисто под текст без лимита в 320
            double textWidth = MeasureStringWidth(LyricsDisplay.Text, LyricsDisplay.FontSize);
            double collapsedWidth = string.IsNullOrEmpty(LyricsDisplay.Text)
                                    ? 180
                                    : Math.Max(180, textWidth + 160);

            HubCard.BeginAnimation(WidthProperty, new DoubleAnimation(collapsedWidth, duration) { EasingFunction = ease });
            HubCard.BeginAnimation(HeightProperty, new DoubleAnimation(40, duration) { EasingFunction = ease });
            playerBadge.BeginAnimation(OpacityProperty, new DoubleAnimation(0, TimeSpan.FromMilliseconds(100)));
        }


        // === Загрузка трека (Парсинг имени файла + загрузка LRC) ===
        private async Task LoadTrackAsync(int index, bool startPlaying = true)
        {
            if (_isClosed) return;
            await _playerLock.WaitAsync();

            _isLoading = true;
            _currentLyrics.Clear();
            LyricsDisplay.Text = "";
            songProgress.Value = 0;
            songProgress.IsIndeterminate = true;

            try
            {
                string trackUrl = _playlist[index];

                // 1. Лирика
                if (_lrcMap.TryGetValue(trackUrl, out string? lrcUrl))
                {
                    try { using var client = new HttpClient(); string lrcContent = await client.GetStringAsync(lrcUrl); ParseLrc(lrcContent); } catch { }
                }

                // 2. Аудио
                var result = await Task.Run(() =>
                {
                    try
                    {
                        _waveOut?.Stop();
                        _mediaReader?.Dispose();
                        var reader = new MediaFoundationReader(trackUrl);
                        var visProvider = new VisualizationProvider(reader.ToSampleProvider());
                        string cleanName = System.Net.WebUtility.UrlDecode(Path.GetFileNameWithoutExtension(trackUrl));
                        string artist = ""; string title = cleanName;
                        if (cleanName.Contains(" - ")) { var parts = cleanName.Split(new[] { " - " }, 2, StringSplitOptions.None); artist = parts[0].Trim(); title = parts[1].Trim(); }
                        return (reader, visProvider, artist, title, null as Exception);
                    }
                    catch (Exception ex) { return (null, null, "", "", ex); }
                });

                if (result.Item5 != null) throw result.Item5;

                _mediaReader = result.Item1;
                _visProvider = result.Item2;
                _waveOut.Init(_visProvider);

                songProgress.Maximum = _mediaReader.TotalTime.TotalSeconds;
                ArtistTitle.Text = result.Item3;
                SongTitle.Text = result.Item4;
                songProgress.IsIndeterminate = false;

                // --- ОБНОВЛЕНИЕ ЦВЕТОВ И ОБЛОЖКИ ---
                UpdateVisualizerColor(trackUrl);
                var coverBrush = await GetTrackCoverAsync(trackUrl);

                if (coverBrush != null)
                {
                    HubBorder.Background = coverBrush;
                    HubBorder.Effect = new System.Windows.Media.Effects.BlurEffect
                    {
                        Radius = _isIslandExpanded ? 0 : 20,
                        KernelType = System.Windows.Media.Effects.KernelType.Gaussian
                    };
                }
                else
                {
                    HubBorder.Background = new SolidColorBrush(Color.FromArgb(255, 18, 18, 18));
                    HubBorder.Effect = null;
                }

                if (startPlaying) { _waveOut.Play(); PlayIcon.Symbol = SymbolRegular.Pause48; SubscribeToRendering(); }
                else { PlayIcon.Symbol = SymbolRegular.Play48; UnsubscribeFromRendering(); }
            }
            catch { SongTitle.Text = "Ошибка загрузки"; }
            finally { _isLoading = false; _playerLock.Release(); }
        }

        // Метод, который создает идеальную "пилюлю" для обрезки
        private void UpdateIslandClip()
        {
            if (HubCard == null) return;

            // Создаем геометрию прямоугольника с радиусом скругления 20
            var clipGeometry = new RectangleGeometry
            {
                RadiusX = 20,
                RadiusY = 20,
                Rect = new Rect(0, 0, HubCard.ActualWidth, HubCard.ActualHeight)
            };

            // Накладываем эту маску на саму карточку
            // Теперь НИЧТО (ни блюр, ни картинка) не вылезет за эти границы
            HubCard.Clip = clipGeometry;
        }

        // Событие, которое срабатывает при ЛЮБОМ изменении размера (в т.ч. во время анимации)
        private void HubCard_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            UpdateIslandClip();
        }

        private void ParseLrc(string lrcContent)
        {
            _currentLyrics.Clear();

            // Регулярка для поиска [мм:сс.фф] или [мм:сс:фф]
            var regex = new Regex(@"\[(?<min>\d+):(?<sec>\d+)(?:[.:](?<ms>\d+))?\]");
            var lines = lrcContent.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries);

            foreach (var line in lines)
            {
                // Ищем все временные метки в строке (бывает, что одна строка относится к нескольким меткам)
                var matches = regex.Matches(line);
                if (matches.Count == 0) continue;

                // Очищаем текст строки от всех временных меток [00:00.00]
                string text = regex.Replace(line, "").Trim();

                foreach (Match match in matches)
                {
                    int m = int.Parse(match.Groups["min"].Value);
                    int s = int.Parse(match.Groups["sec"].Value);
                    int ms = 0;

                    if (match.Groups["ms"].Success)
                    {
                        string msVal = match.Groups["ms"].Value;
                        ms = int.Parse(msVal);

                        // Стандарт LRC: .75 — это 750мс, .7 — это 700мс, .750 — это 750мс
                        if (msVal.Length == 2) ms *= 10;
                        else if (msVal.Length == 1) ms *= 100;
                    }

                    // ИСПРАВЛЕНО: Правильный конструктор (0 дней, 0 часов, m минут, s секунд, ms миллисекунд)
                    var timeSpan = new TimeSpan(0, 0, m, s, ms);

                    _currentLyrics.Add(new LrcLine
                    {
                        Time = timeSpan,
                        Text = text
                    });
                }
            }
            // Сортируем по времени на случай, если метки в файле идут вразнобой
            _currentLyrics = _currentLyrics.OrderBy(l => l.Time).ToList();
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
        private bool _isShowingProgress = false;
        private void InitializeDownloadProgressTracking()
        {
            DownloadTaskManager.Instance.Tasks.CollectionChanged += Tasks_CollectionChanged;

            foreach (var task in DownloadTaskManager.Instance.Tasks)
            {
                task.PropertyChanged += Task_PropertyChanged;
            }

            UpdateGlobalDownloadProgress();
        }

        private void Tasks_CollectionChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
        {
            if (e.NewItems != null)
            {
                foreach (DownloadTask task in e.NewItems)
                {
                    task.PropertyChanged += Task_PropertyChanged;
                }
            }
            if (e.OldItems != null)
            {
                foreach (DownloadTask task in e.OldItems)
                {
                    task.PropertyChanged -= Task_PropertyChanged;
                }
            }
            UpdateGlobalDownloadProgress();
        }

        private void Task_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(DownloadTask.Progress) ||
                e.PropertyName == nameof(DownloadTask.IsCompleted) ||
                e.PropertyName == nameof(DownloadTask.IsIndeterminate) ||
                e.PropertyName == nameof(DownloadTask.IsError))
            {
                Dispatcher.BeginInvoke(new Action(UpdateGlobalDownloadProgress), System.Windows.Threading.DispatcherPriority.Background);
            }
        }

        private enum IslandStatusState
        {
            Normal,     // Иконка музыки
            Progress,   // Прогресс-ринг скачивания
            Success,    // Зеленая галочка
            Error       // Красный крестик
        }

        private IslandStatusState _currentIslandState = IslandStatusState.Normal;
        private System.Threading.CancellationTokenSource? _stateResetCts;

        private async void UpdateGlobalDownloadProgress()
        {
            var activeTasks = DownloadTaskManager.Instance.Tasks
                .Where(t => !t.IsCompleted && !t.IsError)
                .ToList();

            if (activeTasks.Count > 0)
            {
                // Если появились или идут активные задачи, сбрасываем любые таймеры возврата к ноте
                _stateResetCts?.Cancel();
                _stateResetCts = null;

                if (_currentIslandState != IslandStatusState.Progress)
                {
                    TransitionIslandState(IslandStatusState.Progress);
                }

                // Вычисляем прогресс
                bool isIndeterminate = activeTasks.Any(t => t.IsIndeterminate || t.Progress <= 0);
                if (isIndeterminate)
                {
                    DownloadProgressRing.IsIndeterminate = true;
                }
                else
                {
                    double average = activeTasks.Average(t => t.Progress);
                    DownloadProgressRing.IsIndeterminate = false;
                    DownloadProgressRing.Progress = average;
                }
                return;
            }

            // Активных задач больше нет!
            // Но мы должны проверить: перешли ли мы только что из состояния Progress (скачивания)?
            if (_currentIslandState == IslandStatusState.Progress)
            {
                // Ищем последнюю завершенную задачу в списке, чтобы понять, успешна ли она
                var lastFinishedTask = DownloadTaskManager.Instance.Tasks
                    .OrderByDescending(t => t.StartTime)
                    .FirstOrDefault(t => t.IsCompleted || t.IsError);

                IslandStatusState targetState = IslandStatusState.Success;
                if (lastFinishedTask != null && lastFinishedTask.IsError)
                {
                    targetState = IslandStatusState.Error;
                }

                // Показываем галочку или крестик
                TransitionIslandState(targetState);

                // Запускаем асинхронный таймер возврата в Normal через 5 секунд
                _stateResetCts?.Cancel();
                _stateResetCts = new System.Threading.CancellationTokenSource();
                var token = _stateResetCts.Token;

                try
                {
                    await Task.Delay(5000, token);
                    if (!token.IsCancellationRequested)
                    {
                        TransitionIslandState(IslandStatusState.Normal);
                    }
                }
                catch (TaskCanceledException) { }
            }
            else if (_currentIslandState != IslandStatusState.Success && _currentIslandState != IslandStatusState.Error)
            {
                // Если мы не в состоянии Успеха/Ошибки и нет активных задач — возвращаем ноту
                TransitionIslandState(IslandStatusState.Normal);
            }
        }

        private void TransitionIslandState(IslandStatusState newState)
        {
            if (_currentIslandState == newState) return;

            var oldState = _currentIslandState;
            _currentIslandState = newState;

            var duration = TimeSpan.FromMilliseconds(250);
            var ease = new CubicEase { EasingMode = EasingMode.EaseInOut };

            var fadeOut = new DoubleAnimation(0, duration) { EasingFunction = ease };
            var fadeIn = new DoubleAnimation(1, duration) { EasingFunction = ease };

            // Плавно прячем предыдущее состояние
            GetElementByState(oldState)?.BeginAnimation(OpacityProperty, fadeOut);

            // Плавно проявляем новое состояние
            GetElementByState(newState)?.BeginAnimation(OpacityProperty, fadeIn);
        }

        private UIElement? GetElementByState(IslandStatusState state)
        {
            return state switch
            {
                IslandStatusState.Normal => MusicIcon,
                IslandStatusState.Progress => DownloadProgressRing,
                IslandStatusState.Success => SuccessIcon,
                IslandStatusState.Error => ErrorIcon,
                _ => null
            };
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
            Default
        }

        private void UpdateVisualizerColor(string filename)
        {
            VisualizerStyle style = VisualizerStyle.Default;
            string lowerName = System.IO.Path.GetFileName(filename).ToLower();

            FontFamily font = this.FontFamily;
            LinearGradientBrush gradient = new LinearGradientBrush { StartPoint = new Point(0, 1), EndPoint = new Point(0, 0) };

            // === ВОТ ЗДЕСЬ БЕРЕМ СИСТЕМНЫЙ ЦВЕТ ===
            // Создаем градиент из одного цвета (системного), чтобы он был совместим с остальной логикой
            Color sysColor = SystemColors.AccentColor;
            gradient.GradientStops.Add(new GradientStop(sysColor, 0.0));
            gradient.GradientStops.Add(new GradientStop(sysColor, 1.0));
            font = this.FontFamily;

            // 2. Прогресс бар (Крутим)
            // Тут используем gradient (в котором теперь лежит системный цвет для Default)
            var progressGradient = gradient.Clone();
            progressGradient.RelativeTransform = new RotateTransform { Angle = 90, CenterX = 0.5, CenterY = 0.5 };
            songProgress.Foreground = progressGradient;
            SongTitle.FontFamily = font;

            // 3. Бордер и фон
            if (style == VisualizerStyle.Default)
            {
                HubCard.BorderBrush = Brushes.Transparent;

                // Проверяем на принадлежность к базовому типу TileBrush (подойдет и ImageBrush, и DrawingBrush)
                if (!(HubBorder.Background is TileBrush))
                {
                    HubBorder.Background = new SolidColorBrush(Color.FromRgb(18, 18, 18));
                }

                HubBorder.OpacityMask = null;
                SongTitle.Foreground = this.Foreground;
            }

        }


        private const int BANDS = 60; // Делаем много точек для плавности линии



        private bool _isLyricsAnimating = false;

        private void AnimateLyricsChange(string newText)
        {
            // Если текст тот же или сейчас идет анимация - выходим
            if (LyricsDisplay.Text == newText || _isLyricsAnimating) return;
            _isLyricsAnimating = true;

            var duration = TimeSpan.FromMilliseconds(300);
            var ease = new CubicEase { EasingMode = EasingMode.EaseOut };

            // 1. Исчезновение старого текста
            var fadeOut = new DoubleAnimation(0, TimeSpan.FromMilliseconds(150));
            fadeOut.Completed += (s, e) =>
            {
                LyricsDisplay.Text = newText;

                // 2. Рассчитываем и анимируем размеры островка
                AdjustIslandAndText(newText);

                // 3. Появление нового текста (выплывает снизу вверх)
                var fadeIn = new DoubleAnimation(1, duration);
                var moveUp = new DoubleAnimation(10, 0, duration) { EasingFunction = ease };

                fadeIn.Completed += (s2, e2) => _isLyricsAnimating = false;

                LyricsDisplay.BeginAnimation(OpacityProperty, fadeIn);

                _lyricsTranslate.BeginAnimation(TranslateTransform.YProperty, moveUp);
            };

            LyricsDisplay.BeginAnimation(OpacityProperty, fadeOut);
        }

        private void AdjustIslandAndText(string text)
        {
            // 1. Сначала подбираем размер шрифта БЕЗ анимации
            double targetFontSize = 14;
            double textWidth = MeasureStringWidth(text, 14); // Считаем ширину для 14 шрифта

            if (textWidth + 160 > this.ActualWidth - 40) // Если не влезает в окно
            {
                targetFontSize = 11;
            }

            LyricsDisplay.FontSize = targetFontSize;

            // 2. Теперь считаем ширину с нужным шрифтом
            textWidth = MeasureStringWidth(text, targetFontSize);
            double neededWidth = Math.Max(180, textWidth + 160);

            // 3. Анимируем ширину
            var widthAnim = new DoubleAnimation(neededWidth, TimeSpan.FromMilliseconds(300))
            {
                EasingFunction = new QuarticEase { EasingMode = EasingMode.EaseOut }
            };
            HubCard.BeginAnimation(WidthProperty, widthAnim);
        }

        // Перегрузка для удобства
        private double MeasureStringWidth(string text, double fontSize)
        {
            if (string.IsNullOrEmpty(text)) return 0;

            var formattedText = new FormattedText(
                text,
                System.Globalization.CultureInfo.CurrentCulture,
                FlowDirection.LeftToRight,
                new Typeface(
                    LyricsDisplay.FontFamily,
                    LyricsDisplay.FontStyle,
                    LyricsDisplay.FontWeight,
                    LyricsDisplay.FontStretch),
                fontSize, // Теперь мы передаем сюда double
                Brushes.Black,
                VisualTreeHelper.GetDpi(this).PixelsPerDip);

            return formattedText.Width;
        }

        private float _visualizerMaxPeak = 0.5f; // Для авто-усиления

        // --- Отрисовка и Обновление текста (OnRendering) ---
        private void OnRendering(object? sender, EventArgs e)
        {
            if (!_globalVisualizerEnabled || _visProvider == null || _visProvider.FftData == null) return;

            // 1. ЛОГИКА ТЕКСТА (Лирика или Название трека)
            if (_mediaReader != null && !_isLoading && _waveOut?.PlaybackState == PlaybackState.Playing)
            {
                songProgress.Value = _mediaReader.CurrentTime.TotalSeconds;

                // Ищем текущую строку лирики
                var line = _currentLyrics.LastOrDefault(x => x.Time <= _mediaReader.CurrentTime);

                // Если лирики нет или она еще не началась — берем название песни
                string targetDisplay = (!string.IsNullOrWhiteSpace(line?.Text))
                                       ? line.Text
                                       : SongTitle.Text;

                if (LyricsDisplay.Text != targetDisplay)
                    AnimateLyricsChange(targetDisplay);
            }

            float[] fft = _visProvider.FftData;

            // --- ДЕТЕКТОР БИТА (УСИЛЕННЫЙ) ---
            float currentBass = 0;
            for (int i = 1; i <= 5; i++) currentBass += fft[i];
            currentBass /= 5.0f;

            float currentMax = 0;
            for (int i = 0; i < 20; i++) if (fft[i] > currentMax) currentMax = fft[i];
            if (currentMax > _visualizerMaxPeak) _visualizerMaxPeak = currentMax;
            else _visualizerMaxPeak -= (_visualizerMaxPeak - Math.Max(0.01f, currentMax)) * 0.02f;

            float normalizedBass = currentBass / Math.Max(0.05f, _visualizerMaxPeak);

            if (normalizedBass > _beatPulse)
                _beatPulse += (normalizedBass - _beatPulse) * 0.8f;
            else
                _beatPulse += (normalizedBass - _beatPulse) * 0.15f;

            _beatPulse = Math.Clamp(_beatPulse, 0f, 1.2f);

            // ==========================================
            // 1. АВАТАРКА (СИЛЬНЫЙ БИТ)
            // ==========================================
            double avatarTarget = 1.0 + (_beatPulse * 0.25); // Увеличение на 25% (было 15)
            AvatarScale.ScaleX += (avatarTarget - AvatarScale.ScaleX) * 0.35;
            AvatarScale.ScaleY += (avatarTarget - AvatarScale.ScaleY) * 0.35;

            // ==========================================
            // 2. КОЛЬЦО (УДАРНАЯ ВОЛНА)
            // ==========================================
            double ringTarget = 1.0 + (_beatPulse * 1.2); // Кольцо разлетается еще дальше
            RingScale.ScaleX += (ringTarget - RingScale.ScaleX) * 0.2;
            RingScale.ScaleY += (ringTarget - RingScale.ScaleY) * 0.2;
            AvatarRing.Opacity = Math.Clamp(_beatPulse * 0.7, 0, 0.7);

            // ==========================================
            // 3. ТЕКСТ (ТЕПЕРЬ ОН ПРЫГАЕТ!)
            // ==========================================
            // Увеличиваем множитель до 0.12 (12% роста вместо 3%). Теперь это будет видно!
            double textTarget = 1.0 + (_beatPulse * 0.12);
            _lyricsScale.ScaleX += (textTarget - _lyricsScale.ScaleX) * 0.3;
            _lyricsScale.ScaleY += (textTarget - _lyricsScale.ScaleY) * 0.3;

            // ДОПОЛНИТЕЛЬНО: Текст вспыхивает (свечение) под бит
            if (LyricsDisplay.Effect is System.Windows.Media.Effects.DropShadowEffect glow)
            {
                glow.Opacity = 0.3 + (_beatPulse * 0.7);
                glow.BlurRadius = 5 + (_beatPulse * 15);
            }

            // ==========================================
            // 4. ОБЩИЙ ФОН
            // ==========================================
            double bgTarget = 1.0 + (_beatPulse * 0.04);
            _backgroundScale.ScaleX += (bgTarget - _backgroundScale.ScaleX) * 0.2;
            _backgroundScale.ScaleY += (bgTarget - _backgroundScale.ScaleY) * 0.2;
        }
        private void SubscribeToRendering()
        {
            // Если в настройках выключено, даже не подписываемся на событие таймера/рендера
            if (!_globalVisualizerEnabled) return;

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



        private async void FluentWindow_Initialized(object sender, EventArgs e)
        {
            AppSettings.Load();
            InitializeDownloadProgressTracking();

            // Запускаем проверку соединения
            var connectionTask = ConnectionCheck();

            string userName = Environment.UserName;
            string greeting;
            int hour = DateTime.Now.Hour;
            if (hour >= 5 && hour < 12) greeting = "Доброе утро";
            else if (hour >= 12 && hour < 17) greeting = "Добрый день";
            else if (hour >= 17 && hour < 23) greeting = "Добрый вечер";
            else greeting = "Доброй ночи";

            TitleBar.Title = $"{greeting}, {userName}!";
            Picture.Source = GetUserAvatar();

            // Ждем завершения проверки сети
            await connectionTask;
            InitializePlayer();

            if (!ThemeChanger.IsSystemInDarkMode())
            {
                var res = CustomMessageBox.Show("Рекомендуется использовать тёмную тему.", "", System.Windows.MessageBoxButton.YesNo);
                if (res == CustomMessageBox.MessageBoxResult.Yes) ThemeChanger.ToggleWindowsTheme();
            }

            // Запускаем автоматическую фоновую проверку обновлений через Velopack
            // Метод вызывается без await, чтобы не блокировать UI-поток приложения при старте
            _ = CheckForUpdatesOnStartupAsync();
        }

        /// <summary>
        /// Автоматическая фоновая проверка обновлений Velopack при старте
        /// </summary>
        private async Task CheckForUpdatesOnStartupAsync()
        {
            try
            {
                // Подключаемся к релизам вашего репозитория на GitHub
                var source = new GithubSource("https://github.com/Helitop/Helinstaller", accessToken: null, prerelease: false);
                var mgr = new UpdateManager(source);

                // 1. Проверяем наличие новой версии на сервере
                var newVersion = await mgr.CheckForUpdatesAsync();
                if (newVersion == null)
                {
                    Debug.WriteLine("[Velopack] Обновлений не найдено. Работаем на актуальной версии.");
                    return;
                }

                Debug.WriteLine($"[Velopack] Найдено обновление: {newVersion.TargetFullRelease.Version}. Начинаем фоновую загрузку...");

                // 2. Тихо скачиваем обновление в фоновом режиме (пользователь может продолжать пользоваться программой)
                await mgr.DownloadUpdatesAsync(newVersion);

                Debug.WriteLine("[Velopack] Обновление успешно скачано в фоновом режиме.");

                // 3. Выводим диалоговое окно с предложением перезапуститься прямо сейчас
                var uiMessageBox = new Wpf.Ui.Controls.MessageBox
                {
                    Title = "Доступно обновление",
                    Content = $"Для Helinstaller скачана новая версия: {newVersion.TargetFullRelease.Version}.\n\nПерезапустить приложение сейчас, чтобы применить изменения?",
                    PrimaryButtonText = "Перезапустить",
                    SecondaryButtonText = "Позже (при следующем запуске)",
                    CloseButtonText = "Отмена"
                };

                var result = await uiMessageBox.ShowDialogAsync();
                if (result == Wpf.Ui.Controls.MessageBoxResult.Primary)
                {
                    // Velopack мгновенно применит обновление и автоматически откроет программу заново
                    mgr.ApplyUpdatesAndRestart(newVersion);
                }
            }
            catch (Exception ex)
            {
                // Ошибки автообновления пишем в дебаг, чтобы не мешать пользователю, если, например, пропал интернет
                Debug.WriteLine($"[Velopack] Ошибка автообновления: {ex.Message}");
            }
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

        private async Task<Brush?> GetTrackCoverAsync(string url)
        {
            try
            {
                using var client = new HttpClient();
                client.DefaultRequestHeaders.Range = new System.Net.Http.Headers.RangeHeaderValue(0, 3000000);

                var response = await client.GetAsync(url);
                if (!response.IsSuccessStatusCode) return null;

                var bytes = await response.Content.ReadAsByteArrayAsync();
                using var ms = new MemoryStream(bytes);

                var abstraction = new SimpleFileAbstraction(Path.GetFileName(url), ms);

                try
                {
                    using var tagFile = TagLib.File.Create(abstraction);
                    if (tagFile != null && tagFile.Tag.Pictures.Length > 0)
                    {
                        var bin = tagFile.Tag.Pictures[0].Data.Data;
                        var bitmap = new BitmapImage();
                        using (var stream = new MemoryStream(bin))
                        {
                            bitmap.BeginInit();
                            bitmap.StreamSource = stream;
                            bitmap.CacheOption = BitmapCacheOption.OnLoad;
                            bitmap.EndInit();
                        }
                        bitmap.Freeze();

                        // === МАГИЯ КОМПОЗИЦИИ ДЛЯ ИСКЛЮЧЕНИЯ ПРОЗРАЧНОСТИ ===
                        var drawingGroup = new DrawingGroup();

                        // 1. Создаем гарантированную сплошную темную подложку
                        var backgroundBrush = new SolidColorBrush(Color.FromRgb(18, 18, 18));
                        var backgroundGeometry = new RectangleGeometry(new Rect(0, 0, 1, 1));
                        var backgroundDrawing = new GeometryDrawing(backgroundBrush, null, backgroundGeometry);
                        drawingGroup.Children.Add(backgroundDrawing);

                        // 2. Создаем рисунок обложки
                        var imageDrawing = new ImageDrawing(bitmap, new Rect(0, 0, 1, 1));

                        // 3. Заворачиваем обложку в группу с Opacity = 0.4 (настраиваем степень затемнения)
                        var imageGroup = new DrawingGroup { Opacity = 0.4 };
                        imageGroup.Children.Add(imageDrawing);
                        drawingGroup.Children.Add(imageGroup);

                        // 4. Помещаем скомпонованный рисунок в непрозрачную кисть
                        var drawingBrush = new DrawingBrush(drawingGroup)
                        {
                            Stretch = Stretch.UniformToFill
                        };
                        drawingBrush.Freeze(); // Замораживаем для производительности

                        return drawingBrush;
                    }
                }
                catch (TagLib.CorruptFileException) { }
            }
            catch (Exception ex) { Debug.WriteLine("Ошибка сети/чтения: " + ex.Message); }
            return null;
        }

        private class SimpleFileAbstraction : TagLib.File.IFileAbstraction
        {
            public SimpleFileAbstraction(string name, Stream stream)
            {
                Name = name;
                ReadStream = stream;
                WriteStream = stream;
            }
            public string Name { get; }
            public Stream ReadStream { get; }
            public Stream WriteStream { get; }
            public void CloseStream(Stream stream) { }
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

        private void Donate_Click(object sender, RoutedEventArgs e)
        {
            Navigate(typeof(Helinstaller.Views.Pages.Donate));
        }
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