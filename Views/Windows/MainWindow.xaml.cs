using CommunityToolkit.Mvvm.Messaging;
using Helinstaller.Helpers;
using Helinstaller.Models;
using Helinstaller.Services;
using Helinstaller.ViewModels.Windows;
using Helinstaller.Views.Pages;
using Microsoft.Win32;
using NAudio.Dsp;
using NAudio.Wave;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using Velopack;
using Velopack.Sources;
using Windows.Media;
using Windows.Storage;
using Windows.Storage.Streams;
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
        private SystemMediaTransportControls? _smtc;
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
        private float _beatPulse = 0;
        private float _eqMaxPeak = 25.0f;

        // Контроль потоков
        private readonly SemaphoreSlim _playerLock = new SemaphoreSlim(1, 1);
        private bool _isClosed = false;
        private bool _isLoading = false;

        // Поля для анимации UI
        private bool _isRenderingSubscribed = false;
        private bool _isIslandExpanded = false;

        // Контроль динамических тостов
        private CancellationTokenSource? _toastCts;
        private bool _isToastActive = false;

        public MainWindowViewModel ViewModel { get; }
        private int _currentTourStep = 0;

        private record TourStep(Type PageType, string StepBadge, string Title, string Description, string ButtonText);

        private readonly List<TourStep> _tourSteps = new()
        {
            new TourStep(
                typeof(Views.Pages.SystemDashboardPage),
                "Шаг 1 из 5",
                "🌡️ Чё с компом вообще?",
                "Тут датчики твоего железа в реальном времени. Если полоски короткие — кайфуем. Если полезли за 90% — значит комп пыхтит от нагрузки.",
                "Понял, дальше →"
            ),
            new TourStep(
                typeof(Views.Pages.DashboardPage),
                "Шаг 2 из 5",
                "📦 Софт без мусора и вирусов",
                "Забудь про поиск по помойкам в браузере. Выбрал прогу, нажал кнопку — она сама скачалась и тихо встала в систему. Сверху есть кнопка обновить всё в 1 клик.",
                "Круто, дальше →"
            ),
            new TourStep(
                typeof(Views.Pages.Tweaks),
                "Шаг 3 из 5",
                "🛠️ Делаем винду послушной",
                "Тут в один клик вырубаем бесячие всплывашки подтверждения прав, сносим яндекс-мусор, активируем винду и убираем лишние вкладки из Alt+Tab.",
                "Ясно, дальше →"
            ),
            new TourStep(
                typeof(Views.Pages.Ventoy),
                "Шаг 4 из 5",
                "💾 Флешка-выручалка",
                "Вставил любую флешку, нажал «Установить» — получил загрузочную флешку Ventoy. После этого можно просто перетаскивать на неё любые ISO-образы винды как файлы.",
                "Супер, дальше →"
            ),
            new TourStep(
                typeof(Views.Pages.SystemDashboardPage),
                "Шаг 5 из 5",
                "🎧 Музыкальный островок",
                "Сверху висит плеер с музыкой и текстами песен, который прыгает под бас. При наведении раскрывается панель управления треками.",
                "Всё понятно, закрыть!"
            )
        };

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

            var lyricsGroup = new TransformGroup();
            lyricsGroup.Children.Add(_lyricsScale);
            lyricsGroup.Children.Add(_lyricsTranslate);
            LyricsDisplay.RenderTransform = lyricsGroup;
            LyricsDisplay.RenderTransformOrigin = new Point(0.5, 0.5);

            TextOptions.SetTextFormattingMode(LyricsDisplay, TextFormattingMode.Ideal);
            TextOptions.SetTextHintingMode(LyricsDisplay, TextHintingMode.Animated);

            var bgGroup = new TransformGroup();
            bgGroup.Children.Add(_backgroundScale);
            HubBorder.RenderTransform = bgGroup;
            HubBorder.RenderTransformOrigin = new Point(0.5, 0.5);

            WeakReferenceMessenger.Default.Register<VisualizerStatusChangedMessage>(this, (r, m) =>
            {
                _globalVisualizerEnabled = m.Value;

                if (!_globalVisualizerEnabled)
                {
                    UnsubscribeFromRendering();
                    AvatarScale.ScaleX = 1;
                    AvatarScale.ScaleY = 1;
                    AvatarRing.Opacity = 0;
                    EqBar1.Height = 3;
                    EqBar2.Height = 3;
                    EqBar3.Height = 3;
                    EqBar4.Height = 3;
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

        private void CheckAndStartOnboardingTour()
        {
            if (!Models.AppSettings.IsOnboardingCompleted)
            {
                Task.Delay(1200).ContinueWith(_ =>
                {
                    Dispatcher.InvokeAsync(StartTour);
                });
            }
        }

        public void StartTour()
        {
            _currentTourStep = 0;
            ShowCurrentTourStep();

            TourOverlay.Visibility = Visibility.Visible;
            TourOverlay.Opacity = 0;

            var bounce = new BackEase { Amplitude = 0.3, EasingMode = EasingMode.EaseOut };
            TourOverlay.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromSeconds(0.3)));
            TourScale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(0.6, 1.0, TimeSpan.FromSeconds(0.35)) { EasingFunction = bounce });
        }

        private void ShowCurrentTourStep()
        {
            if (_currentTourStep < 0 || _currentTourStep >= _tourSteps.Count) return;

            var step = _tourSteps[_currentTourStep];
            Navigate(step.PageType);

            TourStepBadge.Content = step.StepBadge;
            TourTitleText.Text = step.Title;
            TourDescriptionText.Text = step.Description;
            NextTourBtn.Content = step.ButtonText;
        }

        private void NextTourStep_Click(object sender, RoutedEventArgs e)
        {
            _currentTourStep++;

            if (_currentTourStep >= _tourSteps.Count)
            {
                FinishTour();
            }
            else
            {
                var fade = new DoubleAnimation(0.2, 1.0, TimeSpan.FromSeconds(0.2));
                TourDescriptionText.BeginAnimation(OpacityProperty, fade);
                ShowCurrentTourStep();
            }
        }

        private void SkipTour_Click(object sender, RoutedEventArgs e)
        {
            FinishTour();
        }

        private void FinishTour()
        {
            Models.AppSettings.IsOnboardingCompleted = true;
            Models.AppSettings.Save();

            var smoothIn = new CubicEase { EasingMode = EasingMode.EaseIn };
            var fadeOut = new DoubleAnimation(1, 0, TimeSpan.FromSeconds(0.2)) { EasingFunction = smoothIn };
            var scaleDown = new DoubleAnimation(1, 0.7, TimeSpan.FromSeconds(0.2)) { EasingFunction = smoothIn };

            fadeOut.Completed += (s, e) => TourOverlay.Visibility = Visibility.Collapsed;

            TourOverlay.BeginAnimation(OpacityProperty, fadeOut);
            TourScale.BeginAnimation(ScaleTransform.ScaleYProperty, scaleDown);
        }

        public class GitHubFile
        {
            [System.Text.Json.Serialization.JsonPropertyName("name")]
            public string name { get; set; } = "";

            [System.Text.Json.Serialization.JsonPropertyName("download_url")]
            public string download_url { get; set; } = "";
        }

        public class LrcLine
        {
            public TimeSpan Time { get; set; }
            public string Text { get; set; } = "";
        }

        private const string MusicCacheFile = "music_cache.json";

        private List<LrcLine> _currentLyrics = new List<LrcLine>();
        private Dictionary<string, string> _lrcMap = new Dictionary<string, string>();

        private async Task InitializePlayerAsync()
        {
            string user = "Helitop";
            string repo = "Heli-Music";
            string path = "";
            string apiUrl = $"https://api.github.com/repos/{user}/{repo}/contents/{path}";

            _playlist.Clear();
            _lrcMap.Clear();

            try
            {
                using (HttpClient client = new HttpClient { Timeout = TimeSpan.FromSeconds(6) })
                {
                    client.DefaultRequestHeaders.UserAgent.ParseAdd("HelinstallerApp/1.0");
                    var response = await client.GetAsync(apiUrl);

                    if (response.IsSuccessStatusCode)
                    {
                        string json = await response.Content.ReadAsStringAsync();
                        var options = new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                        var files = System.Text.Json.JsonSerializer.Deserialize<List<GitHubFile>>(json, options);

                        if (files != null)
                        {
                            var audioFiles = files.Where(f => f.name.EndsWith(".mp3", StringComparison.OrdinalIgnoreCase) ||
                                                              f.name.EndsWith(".wav", StringComparison.OrdinalIgnoreCase) ||
                                                              f.name.EndsWith(".ogg", StringComparison.OrdinalIgnoreCase)).ToList();
                            var lrcFiles = files.Where(f => f.name.EndsWith(".lrc", StringComparison.OrdinalIgnoreCase)).ToList();

                            foreach (var file in audioFiles)
                            {
                                if (!string.IsNullOrEmpty(file.download_url))
                                {
                                    _playlist.Add(file.download_url);
                                    string baseName = Path.GetFileNameWithoutExtension(file.name);
                                    var matchingLrc = lrcFiles.FirstOrDefault(l => Path.GetFileNameWithoutExtension(l.name).Equals(baseName, StringComparison.OrdinalIgnoreCase));

                                    if (matchingLrc != null && !string.IsNullOrEmpty(matchingLrc.download_url))
                                    {
                                        _lrcMap[file.download_url] = matchingLrc.download_url;
                                    }
                                }
                            }

                            if (_playlist.Count > 0)
                            {
                                try { File.WriteAllText(MusicCacheFile, json); } catch { }
                            }
                        }
                    }
                    else
                    {
                        Logger.LogWarning($"Плеер: GitHub API вернул статус {response.StatusCode}. Пробуем кэш.");
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.LogWarning($"Плеер: Не удалось связаться с GitHub API ({ex.Message}). Пробуем локальный кэш.");
            }

            if (_playlist.Count == 0 && File.Exists(MusicCacheFile))
            {
                try
                {
                    string cachedJson = File.ReadAllText(MusicCacheFile);
                    var options = new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                    var files = System.Text.Json.JsonSerializer.Deserialize<List<GitHubFile>>(cachedJson, options);

                    if (files != null)
                    {
                        var audioFiles = files.Where(f => f.name.EndsWith(".mp3", StringComparison.OrdinalIgnoreCase) ||
                                                          f.name.EndsWith(".wav", StringComparison.OrdinalIgnoreCase) ||
                                                          f.name.EndsWith(".ogg", StringComparison.OrdinalIgnoreCase)).ToList();
                        var lrcFiles = files.Where(f => f.name.EndsWith(".lrc", StringComparison.OrdinalIgnoreCase)).ToList();

                        foreach (var file in audioFiles)
                        {
                            if (!string.IsNullOrEmpty(file.download_url))
                            {
                                _playlist.Add(file.download_url);
                                string baseName = Path.GetFileNameWithoutExtension(file.name);
                                var matchingLrc = lrcFiles.FirstOrDefault(l => Path.GetFileNameWithoutExtension(l.name).Equals(baseName, StringComparison.OrdinalIgnoreCase));

                                if (matchingLrc != null) _lrcMap[file.download_url] = matchingLrc.download_url;
                            }
                        }
                        Logger.LogInfo($"Плеер: Загружено {_playlist.Count} треков из локального кэша.");
                    }
                }
                catch { }
            }

            string localMusicDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Music");
            if (Directory.Exists(localMusicDir))
            {
                foreach (var file in Directory.GetFiles(localMusicDir, "*.*").Where(f => f.EndsWith(".mp3") || f.EndsWith(".wav")))
                {
                    if (!_playlist.Contains(file)) _playlist.Add(file);
                }
            }

            if (!_playlist.Any())
            {
                SongTitle.Text = "Плейлист пуст";
                ArtistTitle.Text = "Нет треков";
                LyricsDisplay.Text = "";
                playerBadge.Visibility = Visibility.Collapsed;
                Logger.LogWarning("Плеер: Плейлист пуст.");
                return;
            }

            playerBadge.Visibility = Visibility.Visible;

            var rng = new Random();
            _playlist = _playlist.OrderBy(a => rng.Next()).ToList();
            _currentTrackIndex = 0;

            _waveOut = new WaveOutEvent { Volume = 0.05f };
            _waveOut.PlaybackStopped += OnPlaybackStopped;

            await LoadTrackAsync(_currentTrackIndex, Models.AppSettings.IsMusicAutoPlayEnabled);
        }

        private void ExpandIsland()
        {
            if (_isIslandExpanded) return;
            _isIslandExpanded = true;

            var duration = TimeSpan.FromMilliseconds(400);
            var ease = new CubicEase { EasingMode = EasingMode.EaseOut };

            double textWidth = MeasureStringWidth(LyricsDisplay.Text, LyricsDisplay.FontSize);
            double expandedWidth = Math.Max(320, Math.Min(500, textWidth + 120));

            HubCard.BeginAnimation(WidthProperty, new DoubleAnimation(expandedWidth, duration) { EasingFunction = ease });
            HubCard.BeginAnimation(HeightProperty, new DoubleAnimation(160, duration) { EasingFunction = ease });

            playerBadge.IsHitTestVisible = true;
            playerBadge.BeginAnimation(OpacityProperty, new DoubleAnimation(1, duration) { BeginTime = TimeSpan.FromMilliseconds(150) });

            if (HubBorder.Effect is System.Windows.Media.Effects.BlurEffect blur)
            {
                blur.BeginAnimation(System.Windows.Media.Effects.BlurEffect.RadiusProperty,
                    new DoubleAnimation(0, TimeSpan.FromMilliseconds(400)) { EasingFunction = ease });
            }
        }

        private void HubTrigger_MouseEnter(object sender, MouseEventArgs e) => ExpandIsland();
        private void HubCard_MouseEnter(object sender, MouseEventArgs e) => ExpandIsland();

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

            double textWidth = MeasureStringWidth(LyricsDisplay.Text, LyricsDisplay.FontSize);
            double collapsedWidth = string.IsNullOrEmpty(LyricsDisplay.Text)
                                    ? 180
                                    : Math.Max(180, textWidth + 120);

            HubCard.BeginAnimation(WidthProperty, new DoubleAnimation(collapsedWidth, duration) { EasingFunction = ease });
            HubCard.BeginAnimation(HeightProperty, new DoubleAnimation(40, duration) { EasingFunction = ease });
            playerBadge.BeginAnimation(OpacityProperty, new DoubleAnimation(0, TimeSpan.FromMilliseconds(100)));
        }

        private int _consecutiveFailedTracks = 0;

        private async Task LoadTrackAsync(int index, bool startPlaying = true)
        {
            if (_isClosed || _playlist.Count == 0) return;
            await _playerLock.WaitAsync();

            _isLoading = true;
            _currentLyrics.Clear();
            LyricsDisplay.Text = "";
            songProgress.Value = 0;
            songProgress.IsIndeterminate = true;

            try
            {
                index = Math.Clamp(index, 0, _playlist.Count - 1);
                string trackUrl = _playlist[index];
                Logger.LogInfo($"Плеер: Загрузка трека {index + 1}/{_playlist.Count} -> {Path.GetFileName(trackUrl)}");

                if (_lrcMap.TryGetValue(trackUrl, out string? lrcUrl) && !string.IsNullOrEmpty(lrcUrl))
                {
                    try
                    {
                        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(4) };
                        string lrcContent = await client.GetStringAsync(lrcUrl);
                        ParseLrc(lrcContent);
                    }
                    catch { }
                }

                var result = await Task.Run(() =>
                {
                    try
                    {
                        _waveOut?.Stop();
                        _mediaReader?.Dispose();

                        var reader = new MediaFoundationReader(trackUrl);
                        var visProvider = new VisualizationProvider(reader.ToSampleProvider());

                        string cleanName = System.Net.WebUtility.UrlDecode(Path.GetFileNameWithoutExtension(trackUrl));
                        string artist = "";
                        string title = cleanName;

                        if (cleanName.Contains(" - "))
                        {
                            var parts = cleanName.Split(new[] { " - " }, 2, StringSplitOptions.None);
                            artist = parts[0].Trim();
                            title = parts[1].Trim();
                        }

                        return (reader, visProvider, artist, title, null as Exception);
                    }
                    catch (Exception ex)
                    {
                        return (null, null, "", "", ex);
                    }
                });

                if (result.Item5 != null) throw result.Item5;

                _mediaReader = result.Item1;
                _visProvider = result.Item2;

                _waveOut?.Init(_visProvider);

                songProgress.Maximum = _mediaReader!.TotalTime.TotalSeconds;
                ArtistTitle.Text = result.Item3;
                SongTitle.Text = result.Item4;
                songProgress.IsIndeterminate = false;
                _consecutiveFailedTracks = 0;

                UpdateVisualizerColor(trackUrl);
                var (coverBrush, rawBytes) = await GetTrackCoverAsync(trackUrl);

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

                await UpdateSmtcMetadataAsync(result.Item3, result.Item4, rawBytes);

                string displayTitle = !string.IsNullOrWhiteSpace(result.Item4) ? result.Item4 : result.Item3;
                if (!string.IsNullOrWhiteSpace(displayTitle))
                {
                    AnimateLyricsChange(displayTitle);
                }

                if (startPlaying)
                {
                    _waveOut?.Play();
                    PlayIcon.Symbol = SymbolRegular.Pause48;
                    SubscribeToRendering();
                    if (_smtc != null) _smtc.PlaybackStatus = MediaPlaybackStatus.Playing;
                }
                else
                {
                    PlayIcon.Symbol = SymbolRegular.Play48;
                    UnsubscribeFromRendering();
                    if (_smtc != null) _smtc.PlaybackStatus = MediaPlaybackStatus.Paused;
                }
            }
            catch (Exception ex)
            {
                Logger.LogWarning($"Плеер: Сбой воспроизведения трека: {ex.Message}");

                try { _waveOut?.Stop(); } catch { }
                try { _mediaReader?.Dispose(); } catch { }
                _mediaReader = null;
                _visProvider = null;

                songProgress.Value = 0;
                songProgress.IsIndeterminate = false;

                _consecutiveFailedTracks++;

                if (_consecutiveFailedTracks >= 3)
                {
                    SongTitle.Text = "Ошибка воспроизведения";
                    ArtistTitle.Text = "Проверьте соединение";
                    _consecutiveFailedTracks = 0;
                }
                else if (_playlist.Count > 1)
                {
                    _ = Task.Delay(1000).ContinueWith(async _ =>
                    {
                        if (!_isClosed)
                        {
                            await Dispatcher.InvokeAsync(NextButton_Click_Async);
                        }
                    });
                }
            }
            finally
            {
                _isLoading = false;
                _playerLock.Release();
            }
        }

        private void UpdateIslandClip()
        {
            if (HubCard == null) return;

            var clipGeometry = new RectangleGeometry
            {
                RadiusX = 20,
                RadiusY = 20,
                Rect = new Rect(0, 0, HubCard.ActualWidth, HubCard.ActualHeight)
            };

            HubCard.Clip = clipGeometry;
        }

        private void HubCard_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            UpdateIslandClip();
        }

        private void ParseLrc(string lrcContent)
        {
            _currentLyrics.Clear();
            var regex = new Regex(@"\[(?<min>\d+):(?<sec>\d+)(?:[.:](?<ms>\d+))?\]");
            var lines = lrcContent.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries);

            foreach (var line in lines)
            {
                var matches = regex.Matches(line);
                if (matches.Count == 0) continue;

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

                        if (msVal.Length == 2) ms *= 10;
                        else if (msVal.Length == 1) ms *= 100;
                    }

                    var timeSpan = new TimeSpan(0, 0, m, s, ms);

                    _currentLyrics.Add(new LrcLine
                    {
                        Time = timeSpan,
                        Text = text
                    });
                }
            }
            _currentLyrics = _currentLyrics.OrderBy(l => l.Time).ToList();
        }

        private void OnPlaybackStopped(object? sender, StoppedEventArgs e)
        {
            if (_isClosed) return;

            Application.Current.Dispatcher.Invoke(async () =>
            {
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

                if (_smtc != null) _smtc.PlaybackStatus = MediaPlaybackStatus.Paused;
            }
            else
            {
                _waveOut.Play();
                PlayPauseButton.Content = PlayIcon;
                PlayIcon.Symbol = SymbolRegular.Pause48;
                SubscribeToRendering();

                if (_smtc != null) _smtc.PlaybackStatus = MediaPlaybackStatus.Playing;
            }
        }

        private async void NextButton_Click(object? sender, RoutedEventArgs? e)
        {
            await NextButton_Click_Async();
        }

        private async Task NextButton_Click_Async()
        {
            try
            {
                if (_isLoading || _playlist.Count == 0) return;
                _currentTrackIndex = (_currentTrackIndex + 1) % _playlist.Count;
                await LoadTrackAsync(_currentTrackIndex);
            }
            catch (Exception ex)
            {
                Logger.LogError("Плеер: Ошибка перехода на следующий трек", ex);
            }
        }

        private async void PrevButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (_isLoading || _playlist.Count == 0) return;
                _currentTrackIndex = (_currentTrackIndex - 1 + _playlist.Count) % _playlist.Count;
                await LoadTrackAsync(_currentTrackIndex);
            }
            catch (Exception ex)
            {
                Logger.LogError("Плеер: Ошибка перехода на предыдущий трек", ex);
            }
        }

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
            if (sender is DownloadTask task)
            {
                if (e.PropertyName == nameof(DownloadTask.Progress) ||
                    e.PropertyName == nameof(DownloadTask.IsCompleted) ||
                    e.PropertyName == nameof(DownloadTask.IsIndeterminate) ||
                    e.PropertyName == nameof(DownloadTask.IsError))
                {
                    Dispatcher.BeginInvoke(new Action(UpdateGlobalDownloadProgress), System.Windows.Threading.DispatcherPriority.Background);
                }

                string tag = $"task_{task.Id}";
                string group = "helinstaller_tasks";

                if (e.PropertyName == nameof(DownloadTask.Status))
                {
                    if (task.Status == "В очереди..." || task.Status == "Подготовка..." || task.Status == "Копирование файла..." || task.Status == "Скачивание...")
                    {
                        ToastNotificationService.ShowProgressToast(tag, group, task.Title, task.Status);
                    }
                    else if (!task.IsCompleted && !task.IsError)
                    {
                        ToastNotificationService.UpdateProgressToast(tag, group, task.Title, task.Progress, task.Status);
                    }
                }
                else if (e.PropertyName == nameof(DownloadTask.Progress))
                {
                    if (!task.IsCompleted && !task.IsError)
                    {
                        ToastNotificationService.UpdateProgressToast(tag, group, task.Title, task.Progress, task.Status);
                    }
                }
                else if (e.PropertyName == nameof(DownloadTask.IsCompleted) && task.IsCompleted)
                {
                    string completionMessage = task.AppName == "Ventoy"
                        ? "Запись файла на флешку успешно завершена!"
                        : "Установка программы успешно завершена!";

                    ToastNotificationService.CompleteProgressToast(tag, group, task.Title, completionMessage, true);
                }
                else if (e.PropertyName == nameof(DownloadTask.IsError) && task.IsError)
                {
                    ToastNotificationService.CompleteProgressToast(tag, group, task.Title, $"Произошла ошибка: {task.ErrorMessage}", false);
                }
            }
        }

        private enum IslandStatusState
        {
            Normal,
            Progress,
            Success,
            Error
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
                _stateResetCts?.Cancel();
                _stateResetCts = null;

                if (_currentIslandState != IslandStatusState.Progress)
                {
                    TransitionIslandState(IslandStatusState.Progress);
                }

                QueueCounterText.Text = activeTasks.Count.ToString();

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

            if (_currentIslandState == IslandStatusState.Progress)
            {
                var lastFinishedTask = DownloadTaskManager.Instance.Tasks
                    .OrderByDescending(t => t.StartTime)
                    .FirstOrDefault(t => t.IsCompleted || t.IsError);

                IslandStatusState targetState = IslandStatusState.Success;
                if (lastFinishedTask != null && lastFinishedTask.IsError)
                {
                    targetState = IslandStatusState.Error;
                }

                TransitionIslandState(targetState);

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

            if (oldState == IslandStatusState.Normal) EqContainer.BeginAnimation(OpacityProperty, fadeOut);
            else GetElementByState(oldState)?.BeginAnimation(OpacityProperty, fadeOut);

            if (oldState == IslandStatusState.Progress) QueueCounterText.BeginAnimation(OpacityProperty, fadeOut);

            if (newState == IslandStatusState.Normal) EqContainer.BeginAnimation(OpacityProperty, fadeIn);
            else GetElementByState(newState)?.BeginAnimation(OpacityProperty, fadeIn);

            if (newState == IslandStatusState.Progress) QueueCounterText.BeginAnimation(OpacityProperty, fadeIn);
        }

        private UIElement? GetElementByState(IslandStatusState state)
        {
            return state switch
            {
                IslandStatusState.Normal => EqContainer,
                IslandStatusState.Progress => DownloadProgressRing,
                IslandStatusState.Success => SuccessIcon,
                IslandStatusState.Error => ErrorIcon,
                _ => null
            };
        }

        public enum VisualizerStyle { Default }

        private void UpdateVisualizerColor(string filename)
        {
            FontFamily font = this.FontFamily;
            LinearGradientBrush gradient = new LinearGradientBrush { StartPoint = new Point(0, 1), EndPoint = new Point(0, 0) };

            Color sysColor = SystemColors.AccentColor;
            gradient.GradientStops.Add(new GradientStop(sysColor, 0.0));
            gradient.GradientStops.Add(new GradientStop(sysColor, 1.0));

            var progressGradient = gradient.Clone();
            progressGradient.RelativeTransform = new RotateTransform { Angle = 90, CenterX = 0.5, CenterY = 0.5 };
            songProgress.Foreground = progressGradient;
            SongTitle.FontFamily = font;

            HubCard.BorderBrush = Brushes.Transparent;

            if (!(HubBorder.Background is TileBrush))
            {
                HubBorder.Background = new SolidColorBrush(Color.FromRgb(18, 18, 18));
            }

            HubBorder.OpacityMask = null;
            SongTitle.Foreground = this.Foreground;
        }

        private bool _isLyricsAnimating = false;

        private void AnimateLyricsChange(string newText)
        {
            if (LyricsDisplay.Text == newText || _isLyricsAnimating) return;
            _isLyricsAnimating = true;

            var duration = TimeSpan.FromMilliseconds(300);
            var ease = new CubicEase { EasingMode = EasingMode.EaseOut };

            var fadeOut = new DoubleAnimation(0, TimeSpan.FromMilliseconds(150));
            fadeOut.Completed += (s, e) =>
            {
                LyricsDisplay.Text = newText;
                AdjustIslandAndText(newText);

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
            double targetFontSize = 13;
            double textWidth = MeasureStringWidth(text, 13);

            if (textWidth + 120 > this.ActualWidth - 40)
            {
                targetFontSize = 11;
            }

            LyricsDisplay.FontSize = targetFontSize;

            textWidth = MeasureStringWidth(text, targetFontSize);
            double neededWidth = string.IsNullOrEmpty(text)
                                ? 180
                                : Math.Max(180, Math.Min(650, textWidth + 120));

            var widthAnim = new DoubleAnimation(neededWidth, TimeSpan.FromMilliseconds(300))
            {
                EasingFunction = new QuarticEase { EasingMode = EasingMode.EaseOut }
            };
            HubCard.BeginAnimation(WidthProperty, widthAnim);
        }

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
                fontSize,
                Brushes.Black,
                VisualTreeHelper.GetDpi(this).PixelsPerDip);

            return formattedText.Width;
        }

        private void OnRendering(object? sender, EventArgs e)
        {
            if (!_globalVisualizerEnabled || _visProvider == null || _visProvider.FftData == null) return;

            if (!_isToastActive && _mediaReader != null && !_isLoading && _waveOut?.PlaybackState == PlaybackState.Playing)
            {
                songProgress.Value = _mediaReader.CurrentTime.TotalSeconds;

                var line = _currentLyrics.LastOrDefault(x => x.Time <= _mediaReader.CurrentTime);
                string targetDisplay = (!string.IsNullOrWhiteSpace(line?.Text))
                                       ? line.Text
                                       : SongTitle.Text;

                if (LyricsDisplay.Text != targetDisplay)
                    AnimateLyricsChange(targetDisplay);
            }

            float[] fft = _visProvider.FftData;

            if (_waveOut?.PlaybackState == PlaybackState.Playing)
            {
                float b1 = (fft[1] + fft[2]) * 0.5f * 1.0f;
                float b2 = (fft[3] + fft[4] + fft[5] + fft[6]) * 0.25f * 1.4f;
                float b3 = (fft[7] + fft[9] + fft[12] + fft[15]) * 0.25f * 2.2f;
                float b4 = (fft[18] + fft[24] + fft[30] + fft[38]) * 0.25f * 3.5f;

                float frameMax = Math.Max(Math.Max(b1, b2), Math.Max(b3, b4));
                if (frameMax > _eqMaxPeak)
                    _eqMaxPeak = frameMax;
                else
                    _eqMaxPeak -= (_eqMaxPeak - Math.Max(5f, frameMax)) * 0.03f;

                double norm1 = Math.Clamp(b1 / Math.Max(5f, _eqMaxPeak), 0.0, 1.0);
                double norm2 = Math.Clamp(b2 / Math.Max(5f, _eqMaxPeak), 0.0, 1.0);
                double norm3 = Math.Clamp(b3 / Math.Max(5f, _eqMaxPeak), 0.0, 1.0);
                double norm4 = Math.Clamp(b4 / Math.Max(5f, _eqMaxPeak), 0.0, 1.0);

                double targetH1 = 3.0 + Math.Pow(norm1, 1.2) * 12.0;
                double targetH2 = 3.0 + Math.Pow(norm2, 1.2) * 12.0;
                double targetH3 = 3.0 + Math.Pow(norm3, 1.2) * 12.0;
                double targetH4 = 3.0 + Math.Pow(norm4, 1.2) * 12.0;

                EqBar1.Height += (targetH1 - EqBar1.Height) * 0.45;
                EqBar2.Height += (targetH2 - EqBar2.Height) * 0.45;
                EqBar3.Height += (targetH3 - EqBar3.Height) * 0.45;
                EqBar4.Height += (targetH4 - EqBar4.Height) * 0.45;

                float currentBass = (fft[1] + fft[2] + fft[3]) / 3.0f;
                float normBass = currentBass / Math.Max(5f, _eqMaxPeak);
                _beatPulse += (normBass - _beatPulse) * 0.35f;
                _beatPulse = Math.Clamp(_beatPulse, 0f, 1f);

                double avatarTarget = 1.0 + (_beatPulse * 0.15);
                AvatarScale.ScaleX += (avatarTarget - AvatarScale.ScaleX) * 0.35;
                AvatarScale.ScaleY += (avatarTarget - AvatarScale.ScaleY) * 0.35;

                double ringTarget = 1.0 + (_beatPulse * 1.1);
                RingScale.ScaleX += (ringTarget - RingScale.ScaleX) * 0.2;
                RingScale.ScaleY += (ringTarget - RingScale.ScaleY) * 0.2;
                AvatarRing.Opacity = Math.Clamp(_beatPulse * 0.6, 0, 0.6);
            }
            else
            {
                EqBar1.Height += (3 - EqBar1.Height) * 0.2;
                EqBar2.Height += (3 - EqBar2.Height) * 0.2;
                EqBar3.Height += (3 - EqBar3.Height) * 0.2;
                EqBar4.Height += (3 - EqBar4.Height) * 0.2;

                AvatarScale.ScaleX += (1.0 - AvatarScale.ScaleX) * 0.2;
                AvatarScale.ScaleY += (1.0 - AvatarScale.ScaleY) * 0.2;
                AvatarRing.Opacity = 0;
            }

            double textTarget = 1.0 + (_beatPulse * 0.06);
            _lyricsScale.ScaleX += (textTarget - _lyricsScale.ScaleX) * 0.3;
            _lyricsScale.ScaleY += (textTarget - _lyricsScale.ScaleY) * 0.3;

            double bgTarget = 1.0 + (_beatPulse * 0.03);
            _backgroundScale.ScaleX += (bgTarget - _backgroundScale.ScaleX) * 0.2;
            _backgroundScale.ScaleY += (bgTarget - _backgroundScale.ScaleY) * 0.2;
        }

        private void SubscribeToRendering()
        {
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

        private void OnCapsuleToastReceived(string message, ToastType type)
        {
            Dispatcher.InvokeAsync(async () =>
            {
                await ShowDynamicIslandToastAsync(message, type);
            });
        }

        private async Task ShowDynamicIslandToastAsync(string message, ToastType type)
        {
            _toastCts?.Cancel();
            _toastCts = new CancellationTokenSource();
            var token = _toastCts.Token;

            _isToastActive = true;

            SymbolRegular symbol;
            Color iconColor;
            Color badgeColor;

            switch (type)
            {
                case ToastType.Success:
                    symbol = SymbolRegular.Checkmark24;
                    iconColor = Color.FromRgb(76, 217, 100);
                    badgeColor = Color.FromArgb(60, 76, 217, 100);
                    break;

                case ToastType.Warning:
                    symbol = SymbolRegular.Warning24;
                    iconColor = Color.FromRgb(255, 204, 0);
                    badgeColor = Color.FromArgb(60, 255, 204, 0);
                    break;

                case ToastType.Error:
                    symbol = SymbolRegular.Dismiss24;
                    iconColor = Color.FromRgb(255, 59, 48);
                    badgeColor = Color.FromArgb(60, 255, 59, 48);
                    break;

                case ToastType.Info:
                default:
                    symbol = SymbolRegular.Info24;
                    iconColor = Color.FromRgb(0, 122, 255);
                    badgeColor = Color.FromArgb(60, 0, 122, 255);
                    break;
            }

            ToastStatusIcon.Symbol = symbol;
            ToastStatusIcon.Foreground = new SolidColorBrush(iconColor);
            LeftToastBadge.Background = new SolidColorBrush(badgeColor);

            var duration = TimeSpan.FromMilliseconds(200);
            var ease = new CubicEase { EasingMode = EasingMode.EaseInOut };

            LeftToastBadge.Visibility = Visibility.Visible;
            Picture.BeginAnimation(OpacityProperty, new DoubleAnimation(0, duration) { EasingFunction = ease });
            LeftToastBadge.BeginAnimation(OpacityProperty, new DoubleAnimation(1, duration) { EasingFunction = ease });

            AnimateLyricsChange(message);

            try
            {
                await Task.Delay(3000, token);

                if (!token.IsCancellationRequested)
                {
                    _isToastActive = false;

                    var fadeOut = new DoubleAnimation(0, duration) { EasingFunction = ease };
                    fadeOut.Completed += (s, e) => LeftToastBadge.Visibility = Visibility.Collapsed;
                    LeftToastBadge.BeginAnimation(OpacityProperty, fadeOut);
                    Picture.BeginAnimation(OpacityProperty, new DoubleAnimation(1, duration) { EasingFunction = ease });

                    string returnText = "";
                    if (_mediaReader != null && _waveOut?.PlaybackState == PlaybackState.Playing)
                    {
                        var line = _currentLyrics.LastOrDefault(x => x.Time <= _mediaReader.CurrentTime);
                        returnText = (!string.IsNullOrWhiteSpace(line?.Text)) ? line.Text : SongTitle.Text;
                    }
                    else if (!string.IsNullOrWhiteSpace(SongTitle.Text) && SongTitle.Text != "...")
                    {
                        returnText = SongTitle.Text;
                    }

                    AnimateLyricsChange(returnText);
                }
            }
            catch (TaskCanceledException) { }
        }

        private async void FluentWindow_Initialized(object sender, EventArgs e)
        {
            AppSettings.Load();
            InitializeDownloadProgressTracking();

            CapsuleToastService.OnShowToast += OnCapsuleToastReceived;

            string userName = Environment.UserName;
            string greeting;
            int hour = DateTime.Now.Hour;
            if (hour >= 5 && hour < 12) greeting = "Доброе утро";
            else if (hour >= 12 && hour < 17) greeting = "Добрый день";
            else if (hour >= 17 && hour < 23) greeting = "Добрый вечер";
            else greeting = "Доброй ночи";

            TitleBar.Title = $"{greeting}, {userName}!";
            Picture.Source = GetUserAvatar();
            InitializeSmtc();

            // Запускаем единый плавный пайплайн запуска
            await RunStartupPipelineAsync();
        }

        private async Task RunStartupPipelineAsync()
        {
            Logger.LogInfo("=== Старт пайплайна инициализации и проверки обновлений ===");

            SplashErrorPanel.Visibility = Visibility.Collapsed;
            SplashProgressRing.Visibility = Visibility.Visible;
            SplashProgressRing.IsIndeterminate = true;
            SplashPercentText.Visibility = Visibility.Collapsed;
            SplashSubStatusText.Visibility = Visibility.Collapsed;

            // Плавный старт
            SetSplashStatus("Инициализация системы...");
            await Task.Delay(350);

            // 1. ТЕСТОВЫЙ РЕЖИМ (Симуляция без релиза на GitHub)
            // Активируется зажатой клавишей SHIFT при запуске или аргументом --test-update
            bool isShiftPressed = (Keyboard.Modifiers & ModifierKeys.Shift) != 0 || Keyboard.IsKeyDown(Key.LeftShift) || Keyboard.IsKeyDown(Key.RightShift);
            bool hasTestArg = Environment.GetCommandLineArgs().Any(a => a.Equals("--test-update", StringComparison.OrdinalIgnoreCase));

            if (isShiftPressed || hasTestArg)
            {
                Logger.LogInfo("Сплэш: Активирован режим симуляции обновления (Shift / --test-update).");
                await SimulateUpdateAnimationAsync();
            }
            else
            {
                // 2. ПРОВЕРКА СЕТИ
                SetSplashStatus("Проверка соединения с сетью...");
                await Task.Delay(300);

                bool isOnline = await CheckNetworkConnectionAsync();

                if (!isOnline)
                {
                    Logger.LogWarning("Сетевой тест не пройден: запуск в автономном режиме или ошибка.");
                    SplashProgressRing.Visibility = Visibility.Collapsed;
                    SplashErrorPanel.Visibility = Visibility.Visible;
                    return;
                }

                // 3. ПРОВЕРКА И ПРИМЕНЕНИЕ РЕАЛЬНЫХ ОБНОВЛЕНИЙ VELOPACK
                bool isUpdating = await CheckAndApplyVelopackUpdateAsync();
                if (isUpdating)
                {
                    // Если пошел процесс перезапуска инсталлятора, дальнейшая инициализация не требуется
                    return;
                }
            }

            // 4. ЗАВЕРШЕНИЕ ЗАГРУЗКИ И ПЕРЕХОД В ОСНОВНОЙ ИНТЕРФЕЙС
            SetSplashStatus("Загрузка медиаплеера и треков...");
            await InitializePlayerAsync(); // <-- теперь сплэш ждет скачивания плейлиста и обложки

            SetSplashStatus("Запуск интерфейса...");
            await Task.Delay(200);

            if (!ThemeChanger.IsSystemInDarkMode())
            {
                var res = CustomMessageBox.Show("Рекомендуется использовать тёмную тему.", "", System.Windows.MessageBoxButton.YesNo);
                if (res == CustomMessageBox.MessageBoxResult.Yes) ThemeChanger.ToggleWindowsTheme();
            }

            await AnimateSplashExitAsync();
            CheckAndStartOnboardingTour();
        }

        private async Task SimulateUpdateAnimationAsync()
        {
            SetSplashStatus("Поиск обновлений...");
            await Task.Delay(600);

            await Dispatcher.InvokeAsync(() =>
            {
                SplashStatusText.Text = "Найдена новая версия: v2.0.0 (Тест)";
                SplashSubStatusText.Text = "Загрузка тестового пакета обновлений...";
                SplashSubStatusText.Visibility = Visibility.Visible;
                SplashProgressRing.IsIndeterminate = false;
                SplashProgressRing.Progress = 0;
                SplashPercentText.Text = "0%";
                SplashPercentText.Visibility = Visibility.Visible;
            });

            await Task.Delay(400);

            // Плавная симуляция скачивания 0 -> 100%
            for (int p = 1; p <= 100; p++)
            {
                await Dispatcher.InvokeAsync(() =>
                {
                    SplashProgressRing.Progress = p;
                    SplashPercentText.Text = $"{p}%";
                }, System.Windows.Threading.DispatcherPriority.Background);

                // Динамическая пауза для реалистичности
                int delay = p switch
                {
                    < 25 => 20,
                    < 60 => 14,
                    < 85 => 22,
                    _ => 15
                };
                await Task.Delay(delay);
            }

            await Dispatcher.InvokeAsync(() =>
            {
                SplashStatusText.Text = "Установка компонентов обновления...";
                SplashSubStatusText.Text = "Тест успешно пройден! Открываем интерфейс...";
                SplashProgressRing.IsIndeterminate = true;
                SplashPercentText.Visibility = Visibility.Collapsed;
            });

            await Task.Delay(900);
        }

        private async Task<bool> CheckNetworkConnectionAsync()
        {
            var targets = new[] { "https://google.com", "https://github.com", "https://massgrave.dev" };

            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(4) };
            client.DefaultRequestHeaders.UserAgent.ParseAdd("Helinstaller-Check/1.0");

            foreach (var url in targets)
            {
                try
                {
                    using var response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
                    if (response.IsSuccessStatusCode)
                    {
                        return true;
                    }
                }
                catch { }
            }

            return false;
        }

        private async Task<bool> CheckAndApplyVelopackUpdateAsync()
        {
            try
            {
                SetSplashStatus("Проверка обновлений...");
                await Task.Delay(400);

                var source = new GithubSource("https://github.com/Helitop/Helinstaller", accessToken: null, prerelease: false);
                var mgr = new UpdateManager(source);

                if (!mgr.IsInstalled)
                {
                    Logger.LogInfo("Velopack: Приложение запущено в Debug/Dev режиме (не установлено через Setup). Пропуск автообновления.");
                    SetSplashStatus("Все компоненты актуальны");
                    await Task.Delay(300);
                    return false;
                }

                var newVersion = await mgr.CheckForUpdatesAsync();
                if (newVersion == null)
                {
                    Logger.LogInfo("Velopack: Установлена последняя версия программы.");
                    SetSplashStatus("Установлена актуальная версия");
                    await Task.Delay(300);
                    return false;
                }

                Logger.LogInfo($"Velopack: Обнаружена новая версия {newVersion.TargetFullRelease.Version}. Начинаем загрузку...");

                // Переводим Splash в режим отображения скачивания
                await Dispatcher.InvokeAsync(() =>
                {
                    SplashStatusText.Text = $"Загрузка обновления v{newVersion.TargetFullRelease.Version}...";
                    SplashSubStatusText.Text = "Пожалуйста, подождите завершения";
                    SplashSubStatusText.Visibility = Visibility.Visible;
                    SplashProgressRing.IsIndeterminate = false;
                    SplashProgressRing.Progress = 0;
                    SplashPercentText.Text = "0%";
                    SplashPercentText.Visibility = Visibility.Visible;
                });

                // Скачиваем пакет обновления с передачей прогресса (0-100)
                await mgr.DownloadUpdatesAsync(newVersion, progress =>
                {
                    Dispatcher.InvokeAsync(() =>
                    {
                        SplashProgressRing.Progress = progress;
                        SplashPercentText.Text = $"{progress}%";
                    }, System.Windows.Threading.DispatcherPriority.Background);
                });

                Logger.LogInfo("Velopack: Обновление скачано. Применение и перезапуск...");

                await Dispatcher.InvokeAsync(() =>
                {
                    SplashStatusText.Text = "Установка и перезапуск...";
                    SplashSubStatusText.Text = "Приложение откроется автоматически через секунду";
                    SplashProgressRing.IsIndeterminate = true;
                    SplashPercentText.Visibility = Visibility.Collapsed;
                });

                await Task.Delay(600);

                // Применяем скачанные дельты/пакет и перезапускаем обновленное приложение
                mgr.ApplyUpdatesAndRestart(newVersion);
                return true;
            }
            catch (Exception ex)
            {
                Logger.LogWarning($"Velopack: Сбой проверки/загрузки обновления: {ex.Message}");
                SetSplashStatus("Запуск в автономном режиме...");
                await Task.Delay(300);
                return false;
            }
        }

        private void SetSplashStatus(string status)
        {
            Dispatcher.InvokeAsync(() =>
            {
                SplashStatusText.Text = status;
            });
        }

        private async void Smtc_ButtonPressed(SystemMediaTransportControls sender, SystemMediaTransportControlsButtonPressedEventArgs args)
        {
            await Dispatcher.InvokeAsync(async () =>
            {
                switch (args.Button)
                {
                    case SystemMediaTransportControlsButton.Play:
                        if (_waveOut != null && _waveOut.PlaybackState != PlaybackState.Playing)
                        {
                            _waveOut.Play();
                            PlayIcon.Symbol = SymbolRegular.Pause48;
                            SubscribeToRendering();
                            if (_smtc != null) _smtc.PlaybackStatus = MediaPlaybackStatus.Playing;
                        }
                        break;

                    case SystemMediaTransportControlsButton.Pause:
                        if (_waveOut != null && _waveOut.PlaybackState == PlaybackState.Playing)
                        {
                            _waveOut.Pause();
                            PlayIcon.Symbol = SymbolRegular.Play48;
                            UnsubscribeFromRendering();
                            if (_smtc != null) _smtc.PlaybackStatus = MediaPlaybackStatus.Paused;
                        }
                        break;

                    case SystemMediaTransportControlsButton.Next:
                        await NextButton_Click_Async();
                        break;

                    case SystemMediaTransportControlsButton.Previous:
                        try
                        {
                            if (_isLoading) return;
                            _currentTrackIndex = (_currentTrackIndex - 1 + _playlist.Count) % _playlist.Count;
                            await LoadTrackAsync(_currentTrackIndex);
                        }
                        catch (Exception ex) { SongTitle.Text = ex.Message; }
                        break;
                }
            });
        }

        private async Task UpdateSmtcMetadataAsync(string artist, string title, byte[]? embeddedArtBytes)
        {
            if (_smtc == null) return;

            try
            {
                var updater = _smtc.DisplayUpdater;
                updater.Type = MediaPlaybackType.Music;
                updater.MusicProperties.Artist = string.IsNullOrWhiteSpace(artist) ? "Helinstaller Player" : artist;
                updater.MusicProperties.Title = string.IsNullOrWhiteSpace(title) ? "Без названия" : title;

                if (embeddedArtBytes != null && embeddedArtBytes.Length > 0)
                {
                    try
                    {
                        string tempFolder = Path.GetTempPath();
                        string tempFilePath = Path.Combine(tempFolder, "heli_current_cover.jpg");
                        await File.WriteAllBytesAsync(tempFilePath, embeddedArtBytes);

                        var file = await StorageFile.GetFileFromPathAsync(tempFilePath);
                        updater.Thumbnail = RandomAccessStreamReference.CreateFromFile(file);
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"[SMTC Cover Save Error]: {ex.Message}");
                        updater.Thumbnail = null;
                    }
                }
                else
                {
                    updater.Thumbnail = null;
                }

                updater.Update();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[SMTC Metadata Error]: {ex.Message}");
            }
        }

        private void InitializeSmtc()
        {
            try
            {
                IntPtr hwnd = new System.Windows.Interop.WindowInteropHelper(this).EnsureHandle();
                _smtc = SmtcInteropHelper.GetForWindow(hwnd);

                _smtc.IsPlayEnabled = true;
                _smtc.IsPauseEnabled = true;
                _smtc.IsNextEnabled = true;
                _smtc.IsPreviousEnabled = true;

                _smtc.ButtonPressed += Smtc_ButtonPressed;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[SMTC Init Error]: {ex.Message}");
            }
        }

        private async Task AnimateSplashExitAsync()
        {
            MainGrid.Visibility = Visibility.Visible;

            var duration = TimeSpan.FromMilliseconds(500);
            var ease = new CubicEase { EasingMode = EasingMode.EaseOut };

            var fadeOut = new DoubleAnimation(1, 0, duration) { EasingFunction = ease };
            var fadeInMain = new DoubleAnimation(0, 1, duration) { EasingFunction = ease };

            var tcs = new TaskCompletionSource();
            fadeOut.Completed += (s, e) =>
            {
                SplashContainer.Visibility = Visibility.Collapsed;
                tcs.SetResult();
            };

            SplashContainer.BeginAnimation(OpacityProperty, fadeOut);
            MainGrid.BeginAnimation(OpacityProperty, fadeInMain);

            await tcs.Task;
        }



        private async void SplashRetry_Click(object sender, RoutedEventArgs e)
        {
            await RunStartupPipelineAsync();
        }

        private async void SplashContinueOffline_Click(object sender, RoutedEventArgs e)
        {
            Logger.LogInfo("Пользователь выбрал автономный запуск.");
            SetSplashStatus("Загрузка плеера...");
            await InitializePlayerAsync();
            await AnimateSplashExitAsync();
            CheckAndStartOnboardingTour();
        }

        private void SplashOpenLog_Click(object sender, RoutedEventArgs e)
        {
            Logger.OpenLogFile();
        }

        private async Task<(Brush? CoverBrush, byte[]? RawBytes)> GetTrackCoverAsync(string url)
        {
            try
            {
                using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
                client.DefaultRequestHeaders.Range = new System.Net.Http.Headers.RangeHeaderValue(0, 1048576);

                var response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
                if (!response.IsSuccessStatusCode) return (null, null);

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

                        var drawingGroup = new DrawingGroup();
                        var backgroundBrush = new SolidColorBrush(Color.FromRgb(18, 18, 18));
                        var backgroundGeometry = new RectangleGeometry(new Rect(0, 0, 1, 1));
                        var backgroundDrawing = new GeometryDrawing(backgroundBrush, null, backgroundGeometry);
                        drawingGroup.Children.Add(backgroundDrawing);

                        var imageDrawing = new ImageDrawing(bitmap, new Rect(0, 0, 1, 1));
                        var imageGroup = new DrawingGroup { Opacity = 0.4 };
                        imageGroup.Children.Add(imageDrawing);
                        drawingGroup.Children.Add(imageGroup);

                        var drawingBrush = new DrawingBrush(drawingGroup) { Stretch = Stretch.UniformToFill };
                        drawingBrush.Freeze();

                        return (drawingBrush, bin);
                    }
                }
                catch (TagLib.CorruptFileException) { }
            }
            catch (Exception ex)
            {
                Logger.LogWarning($"Плеер: Не удалось загрузить обложку для трека: {ex.Message}");
            }
            return (null, null);
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
        public void SetServiceProvider(IServiceProvider serviceProvider) => RootNavigation.SetServiceProvider(serviceProvider);
        #endregion

        protected override void OnClosed(EventArgs e)
        {
            _isClosed = true;
            WingetService.KillStuckProcesses();

            UnsubscribeFromRendering();
            _waveOut?.Stop();
            _waveOut?.Dispose();
            _mediaReader?.Dispose();
            base.OnClosed(e);
            Application.Current.Shutdown();
        }

        private void Donate_Click(object sender, RoutedEventArgs e)
        {
            Navigate(typeof(Helinstaller.Views.Pages.Donate));
        }
    }

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