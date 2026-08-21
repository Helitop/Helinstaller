// FILE [CS]: .\ViewModels\Pages\ProxyViewModel.cs

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Helinstaller.Helpers;
using Helinstaller.Models;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using Wpf.Ui.Abstractions.Controls;

namespace Helinstaller.ViewModels.Pages
{
    public partial class ProxyViewModel : ObservableObject, INavigationAware
    {
        private static readonly HttpClient _httpClient = new()
        {
            Timeout = TimeSpan.FromSeconds(8),
            DefaultRequestHeaders =
            {
                { "User-Agent", "Helinstaller-App/1.0" },
                { "Cache-Control", "no-cache" }
            }
        };

        private const string SourcesConfigPath = "proxy_sources.json";
        private readonly List<MtprotoProxyItem> _allProxies = new();
        private readonly object _proxiesLock = new();

        public ObservableCollection<MtprotoProxyItem> Proxies { get; } = new();
        public ObservableCollection<string> Sources { get; } = new();

        [ObservableProperty] private bool _isLoading = false;
        [ObservableProperty] private bool _isChecking = false;
        [ObservableProperty] private double _checkProgress = 0;
        [ObservableProperty] private string _statusText = "Готов к проверке";
        [ObservableProperty] private int _totalFound = 0;
        [ObservableProperty] private int _onlineCount = 0;
        [ObservableProperty] private int _checkedCount = 0;
        [ObservableProperty] private string _searchQuery = string.Empty;
        [ObservableProperty] private string _newSourceUrl = string.Empty;
        [ObservableProperty] private bool _onlyWorking = false;

        private CancellationTokenSource? _workerCts;
        private CancellationTokenSource? _filterCts;
        private DispatcherTimer? _uiThrottleTimer;
        private int _rawCheckedCount = 0;
        private int _rawOnlineCount = 0;

        public ProxyViewModel()
        {
            LoadSources();
        }

        public Task OnNavigatedToAsync()
        {
            Logger.LogInfo("Прокси: Открыта страница MTProto Proxy.");
            if (_allProxies.Count == 0 && !IsLoading && !IsChecking)
            {
                Task.Delay(150).ContinueWith(_ =>
                {
                    Application.Current.Dispatcher.InvokeAsync(async () => await RefreshAndCheckProxies());
                });
            }
            return Task.CompletedTask;
        }

        public Task OnNavigatedFromAsync()
        {
            Logger.LogInfo("Прокси: Уход со страницы. Остановка задач.");
            CancelOperations();
            return Task.CompletedTask;
        }

        partial void OnOnlyWorkingChanged(bool value)
        {
            _ = ApplyDisplayFilterProgressiveAsync();
        }

        partial void OnSearchQueryChanged(string value)
        {
            _ = ApplyDisplayFilterProgressiveAsync();
        }

        private async Task ApplyDisplayFilterProgressiveAsync()
        {
            _filterCts?.Cancel();
            _filterCts?.Dispose();
            _filterCts = new CancellationTokenSource();
            var token = _filterCts.Token;

            try
            {
                List<MtprotoProxyItem> snapshot;
                lock (_proxiesLock)
                {
                    snapshot = _allProxies.ToList();
                }

                if (snapshot.Count == 0)
                {
                    await Application.Current.Dispatcher.InvokeAsync(() => Proxies.Clear());
                    return;
                }

                string query = SearchQuery.Trim();
                bool onlyOnline = OnlyWorking;

                var filtered = snapshot.Where(p =>
                {
                    if (onlyOnline && p.Status != ProxyStatus.Online)
                        return false;

                    if (!string.IsNullOrEmpty(query))
                    {
                        return p.Server.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                               p.Port.ToString().Contains(query);
                    }

                    return true;
                })
                .OrderBy(p => p.Status != ProxyStatus.Online)
                .ThenBy(p => p.Ping < 0 ? long.MaxValue : p.Ping)
                .Take(80)
                .ToList();

                await Application.Current.Dispatcher.InvokeAsync(() => Proxies.Clear(), DispatcherPriority.Background);

                const int batchSize = 5;
                for (int i = 0; i < filtered.Count; i += batchSize)
                {
                    if (token.IsCancellationRequested) return;

                    var batch = filtered.Skip(i).Take(batchSize).ToList();

                    await Application.Current.Dispatcher.InvokeAsync(() =>
                    {
                        if (token.IsCancellationRequested) return;
                        foreach (var item in batch)
                        {
                            Proxies.Add(item);
                        }
                    }, DispatcherPriority.Background);

                    await Task.Delay(16, token).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException)
            {
                // Штатная отмена при быстром вводе текста поиска
            }
            catch (Exception ex)
            {
                Logger.LogError("Прокси: Ошибка фильтрации списка", ex);
            }
        }

        private void LoadSources()
        {
            Sources.Clear();
            if (File.Exists(SourcesConfigPath))
            {
                try
                {
                    var loaded = JsonSerializer.Deserialize<List<string>>(File.ReadAllText(SourcesConfigPath));
                    if (loaded != null && loaded.Count > 0)
                    {
                        foreach (var src in loaded) Sources.Add(src);
                        return;
                    }
                }
                catch { }
            }

            // Свежие, регулярно обновляемые базы рабочих MTProto Fake-TLS прокси:
            Sources.Add("https://raw.githubusercontent.com/hookzof/socks5_list/master/tg/mtproto.json");
            Sources.Add("https://raw.githubusercontent.com/yebekhe/Telegram-Proxy-Collector/master/proxy.txt");
            Sources.Add("https://raw.githubusercontent.com/SoliSpirit/mtproto/master/all_proxies.txt");
            Sources.Add("https://raw.githubusercontent.com/Kort0881/telegram-proxy-collector/main/proxy_ru.txt");
            SaveSources();
        }

        private void SaveSources()
        {
            try
            {
                File.WriteAllText(SourcesConfigPath, JsonSerializer.Serialize(Sources.ToList(), new JsonSerializerOptions { WriteIndented = true }));
            }
            catch { }
        }

        [RelayCommand]
        private void AddSource()
        {
            string url = NewSourceUrl.Trim();
            if (string.IsNullOrWhiteSpace(url)) return;

            if (!Sources.Contains(url, StringComparer.OrdinalIgnoreCase))
            {
                Sources.Add(url);
                SaveSources();
                Logger.LogInfo($"Прокси: Добавлен источник -> {url}");
                NewSourceUrl = string.Empty;
                CapsuleToastService.Show("Источник добавлен", ToastType.Success);
            }
            else
            {
                CapsuleToastService.Show("Источник уже есть в списке", ToastType.Warning);
            }
        }

        [RelayCommand]
        private void RemoveSource(string source)
        {
            if (Sources.Contains(source))
            {
                Sources.Remove(source);
                SaveSources();
                Logger.LogInfo($"Прокси: Источник удален -> {source}");
                CapsuleToastService.Show("Источник удален", ToastType.Info);
            }
        }

        private void CancelOperations()
        {
            _uiThrottleTimer?.Stop();
            _uiThrottleTimer = null;

            try
            {
                _filterCts?.Cancel();
                _filterCts?.Dispose();
                _filterCts = null;

                _workerCts?.Cancel();
                _workerCts?.Dispose();
                _workerCts = null;
            }
            catch { }
        }

        [RelayCommand]
        public async Task RefreshAndCheckProxies()
        {
            CancelOperations();
            _workerCts = new CancellationTokenSource();
            var token = _workerCts.Token;

            Logger.LogInfo("=== Прокси: Принудительное обновление базы из сети ===");

            IsLoading = true;
            IsChecking = false;
            StatusText = "Загрузка свежих списков с серверов...";
            CheckProgress = 0;
            OnlineCount = 0;
            CheckedCount = 0;
            TotalFound = 0;

            lock (_proxiesLock)
            {
                _allProxies.Clear();
            }
            await Application.Current.Dispatcher.InvokeAsync(() => Proxies.Clear());

            var collected = new List<MtprotoProxyItem>();
            var seenKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            try
            {
                await Task.Run(async () =>
                {
                    var fetchTasks = Sources.Select(async src =>
                    {
                        string rawUrl = NormalizeToRawUrl(src);
                        try
                        {
                            using var req = new HttpRequestMessage(HttpMethod.Get, rawUrl);
                            req.Headers.CacheControl = new CacheControlHeaderValue { NoCache = true };

                            using var res = await _httpClient.SendAsync(req, token).ConfigureAwait(false);
                            if (res.IsSuccessStatusCode)
                            {
                                string content = await res.Content.ReadAsStringAsync(token).ConfigureAwait(false);
                                var parsed = ParseProxyLines(content);

                                lock (collected)
                                {
                                    foreach (var p in parsed)
                                    {
                                        string key = $"{p.Server}:{p.Port}";
                                        if (seenKeys.Add(key))
                                        {
                                            collected.Add(p);
                                        }
                                    }
                                }
                            }
                        }
                        catch (OperationCanceledException) { }
                        catch (Exception ex)
                        {
                            Logger.LogWarning($"Прокси: Не удалось получить {src}: {ex.Message}");
                        }
                    });

                    await Task.WhenAll(fetchTasks).ConfigureAwait(false);
                }, token).ConfigureAwait(false);

                if (token.IsCancellationRequested) return;

                lock (_proxiesLock)
                {
                    _allProxies.AddRange(collected);
                    TotalFound = _allProxies.Count;
                }

                IsLoading = false;

                if (TotalFound == 0)
                {
                    StatusText = "Прокси не найдены в указанных источниках";
                    CapsuleToastService.Show("Источники недоступны или пусты", ToastType.Warning);
                    return;
                }

                CapsuleToastService.Show($"База обновлена! Найдено {TotalFound} прокси", ToastType.Success);

                await ApplyDisplayFilterProgressiveAsync();
                await CheckAllProxiesInternalAsync(token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                Logger.LogInfo("Прокси: Загрузка базы прервана пользователем.");
            }
            catch (Exception ex)
            {
                Logger.LogError("Прокси: Сбой при загрузке базы", ex);
                StatusText = "Ошибка загрузки";
            }
            finally
            {
                IsLoading = false;
            }
        }

        [RelayCommand]
        public async Task CheckAllProxies()
        {
            lock (_proxiesLock)
            {
                if (_allProxies.Count == 0) return;
            }

            CancelOperations();
            _workerCts = new CancellationTokenSource();

            try
            {
                await CheckAllProxiesInternalAsync(_workerCts.Token);
            }
            catch (OperationCanceledException)
            {
                Logger.LogInfo("Прокси: Ручной пинг отменен.");
            }
            catch (Exception ex)
            {
                Logger.LogError("Прокси: Ошибка ручного пинга", ex);
            }
        }

        private async Task CheckAllProxiesInternalAsync(CancellationToken token)
        {
            List<MtprotoProxyItem> itemsToCheck;
            lock (_proxiesLock)
            {
                itemsToCheck = _allProxies.ToList();
            }

            int total = itemsToCheck.Count;
            if (total == 0) return;

            IsChecking = true;
            CheckProgress = 0;
            CheckedCount = 0;
            OnlineCount = 0;
            _rawCheckedCount = 0;
            _rawOnlineCount = 0;
            StatusText = "Проверка пинга (TCP Handshake)...";

            int maxParallelism = AppSettings.ProxyMaxParallelism >= 5 && AppSettings.ProxyMaxParallelism <= 100
                ? AppSettings.ProxyMaxParallelism
                : 30;

            int pingTimeout = AppSettings.ProxyPingTimeoutMs >= 500 && AppSettings.ProxyPingTimeoutMs <= 5000
                ? AppSettings.ProxyPingTimeoutMs
                : 2000;

            _uiThrottleTimer = new DispatcherTimer(DispatcherPriority.Background)
            {
                Interval = TimeSpan.FromMilliseconds(100)
            };
            _uiThrottleTimer.Tick += (s, e) =>
            {
                int currentChecked = Volatile.Read(ref _rawCheckedCount);
                int currentOnline = Volatile.Read(ref _rawOnlineCount);

                CheckedCount = currentChecked;
                OnlineCount = currentOnline;
                CheckProgress = total > 0 ? (double)currentChecked / total * 100.0 : 0;
                StatusText = $"Проверено {currentChecked} из {total} (Онлайн: {currentOnline})";
            };
            _uiThrottleTimer.Start();

            try
            {
                await Task.Run(async () =>
                {
                    var parallelOptions = new ParallelOptions
                    {
                        MaxDegreeOfParallelism = maxParallelism,
                        CancellationToken = token
                    };

                    await Parallel.ForEachAsync(itemsToCheck, parallelOptions, async (proxy, ct) =>
                    {
                        if (ct.IsCancellationRequested) return;

                        proxy.Status = ProxyStatus.Checking;
                        var (isOnline, ping) = await MtprotoProbeHelper.ProbeProxyAsync(
                            proxy.Server,
                            proxy.Port,
                            proxy.Secret,
                            pingTimeout,
                            ct).ConfigureAwait(false);

                        proxy.Ping = ping;
                        proxy.Status = isOnline ? ProxyStatus.Online : ProxyStatus.Offline;

                        if (isOnline) Interlocked.Increment(ref _rawOnlineCount);
                        Interlocked.Increment(ref _rawCheckedCount);
                    }).ConfigureAwait(false);
                }, token).ConfigureAwait(false);

                if (!token.IsCancellationRequested)
                {
                    await Application.Current.Dispatcher.InvokeAsync(() =>
                    {
                        CheckedCount = total;
                        OnlineCount = _rawOnlineCount;
                        CheckProgress = 100;
                        StatusText = $"Готово! Рабочих серверов: {OnlineCount} из {TotalFound}";
                        IsChecking = false;
                    });

                    await ApplyDisplayFilterProgressiveAsync();
                }
            }
            catch (OperationCanceledException)
            {
                Logger.LogInfo("Прокси: Массовый пинг безопасно остановлен.");
            }
            catch (Exception ex)
            {
                Logger.LogError("Прокси: Ошибка во время массового пинга", ex);
            }
            finally
            {
                _uiThrottleTimer?.Stop();
                _uiThrottleTimer = null;
                IsChecking = false;
            }
        }

        private static async Task<(bool IsOnline, long Ping)> PingProxyFastAsync(string host, int port, int timeoutMs, CancellationToken ct)
        {
            var sw = Stopwatch.StartNew();
            try
            {
                using var client = new TcpClient();
                using var timeoutCts = new CancellationTokenSource(timeoutMs);
                using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct, timeoutCts.Token);

                IPAddress targetIp;
                if (!IPAddress.TryParse(host, out var parsedIp))
                {
                    var addresses = await Dns.GetHostAddressesAsync(host, linkedCts.Token).ConfigureAwait(false);
                    if (addresses.Length == 0) return (false, -1);
                    targetIp = addresses[0];
                }
                else
                {
                    targetIp = parsedIp;
                }

                await client.ConnectAsync(targetIp, port, linkedCts.Token).ConfigureAwait(false);
                sw.Stop();
                return (true, sw.ElapsedMilliseconds);
            }
            catch
            {
                return (false, -1);
            }
        }

        private static string NormalizeToRawUrl(string url)
        {
            if (url.Contains("github.com") && url.Contains("/blob/"))
            {
                return url.Replace("github.com", "raw.githubusercontent.com")
                          .Replace("/blob/", "/");
            }
            return url;
        }

        private static List<MtprotoProxyItem> ParseProxyLines(string text)
        {
            var result = new List<MtprotoProxyItem>();
            var lines = text.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            var regex = new Regex(@"(?:tg:\/\/|https:\/\/t\.me\/)proxy\?(?:.*?&)?server=(?<server>[^&]+)&port=(?<port>\d+)&secret=(?<secret>[^&\s#]+)", RegexOptions.IgnoreCase);

            foreach (var rawLine in lines)
            {
                string line = rawLine.Trim();
                if (line.StartsWith("#") || string.IsNullOrWhiteSpace(line)) continue;

                var match = regex.Match(line);
                if (match.Success)
                {
                    // Очищаем хост от URL-кодирования и точек на конце
                    string server = WebUtility.UrlDecode(match.Groups["server"].Value.Trim()).TrimEnd('.');

                    if (int.TryParse(match.Groups["port"].Value, out int port) && port > 0 && port <= 65535)
                    {
                        string secret = WebUtility.UrlDecode(match.Groups["secret"].Value.Trim());

                        if (secret.Length < 16) continue;

                        result.Add(new MtprotoProxyItem
                        {
                            Server = server,
                            Port = port,
                            Secret = secret,
                            RawUrl = $"tg://proxy?server={server}&port={port}&secret={secret}"
                        });
                    }
                }
            }

            return result;
        }

        [RelayCommand]
        private void CopyAllWorking()
        {
            List<string> working;
            lock (_proxiesLock)
            {
                working = _allProxies.Where(p => p.Status == ProxyStatus.Online).Select(p => p.RawUrl).ToList();
            }

            if (working.Count == 0)
            {
                CapsuleToastService.Show("Нет рабочих прокси для копирования", ToastType.Warning);
                return;
            }

            Clipboard.SetText(string.Join(Environment.NewLine, working));
            CapsuleToastService.Show($"Скопировано {working.Count} прокси!", ToastType.Success);
        }

        [RelayCommand]
        private void CopySingle(MtprotoProxyItem proxy)
        {
            if (proxy == null) return;
            Clipboard.SetText(proxy.RawUrl);
            CapsuleToastService.Show("Ссылка скопирована в буфер!", ToastType.Success);
        }

        [RelayCommand]
        private void ConnectSingle(MtprotoProxyItem proxy)
        {
            if (proxy == null) return;
            try
            {
                Process.Start(new ProcessStartInfo { FileName = proxy.RawUrl, UseShellExecute = true });
            }
            catch (Exception ex)
            {
                CapsuleToastService.Show($"Не удалось открыть Telegram: {ex.Message}", ToastType.Error);
            }
        }
    }
}