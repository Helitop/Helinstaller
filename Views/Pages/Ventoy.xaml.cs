using Microsoft.Win32;
using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Media;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace Helinstaller.Views.Pages
{
    public partial class Ventoy : Page, INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler PropertyChanged;
        private CancellationTokenSource? _transferCts;

        private DateTime _lastUpdate = DateTime.MinValue; // Новое поле для контроля частоты
        private const int MinUpdateIntervalMs = 200;     // Обновлять не чаще, чем раз в 150 мс


        public ObservableCollection<UsbDriveItem> UsbDrives { get; } = new ObservableCollection<UsbDriveItem>();

        private UsbDriveItem? _selectedDrive;
        public UsbDriveItem? SelectedDrive
        {
            get => _selectedDrive;
            set
            {
                if (_selectedDrive != value)
                {
                    _selectedDrive = value;
                    OnPropertyChanged(nameof(SelectedDrive));
                    UpdateDeviceInfo();
                    OnPropertyChanged(nameof(CanFormat));
                }
            }
        }

        private bool _isRefreshing = false;
        public bool IsRefreshing
        {
            get => _isRefreshing;
            private set
            {
                if (_isRefreshing != value)
                {
                    _isRefreshing = value;
                    OnPropertyChanged(nameof(IsRefreshing));
                    OnPropertyChanged(nameof(IsRefreshEnabled));
                    OnPropertyChanged(nameof(IconVisibility));
                    OnPropertyChanged(nameof(RingVisibility));
                    OnPropertyChanged(nameof(CanFormat));
                    OnPropertyChanged(nameof(CanInstallUpdate));
                }
            }
        }

        public bool IsRefreshEnabled => !IsRefreshing;
        public bool CanInstallUpdate => SelectedDrive != null && !IsRefreshing;

        public Visibility IconVisibility => IsRefreshing ? Visibility.Collapsed : Visibility.Visible;
        public Visibility RingVisibility => IsRefreshing ? Visibility.Visible : Visibility.Collapsed;

        private string _deviceInfoText = "Устройство не выбрано.";
        public string DeviceInfoText
        {
            get => _deviceInfoText;
            set
            {
                if (_deviceInfoText != value)
                {
                    _deviceInfoText = value;
                    OnPropertyChanged(nameof(DeviceInfoText));
                }
            }
        }

        public bool CanFormat => SelectedDrive != null && !IsRefreshing;

        public Ventoy()
        {
            InitializeComponent();
            DataContext = this;

            _ = RefreshUsbListAsync();
        }

        private void OnPropertyChanged(string name) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

        private void UpdateDeviceInfo()
        {
            if (SelectedDrive == null)
            {
                DeviceInfoText = "Устройство не выбрано.";
                return;
            }

            try
            {
                var di = SelectedDrive.ToDriveInfo();
                if (di == null || !di.IsReady)
                {
                    DeviceInfoText = $"{SelectedDrive.DisplayName}\nДиск не готов.";
                    return;
                }

                long total = di.TotalSize;
                long free = di.TotalFreeSpace;
                long used = total - free;
                double usedPercent = total > 0 ? Math.Round(used * 100.0 / total, 1) : 0.0;

                SelectedDrive.TotalBytes = total;
                SelectedDrive.FreeBytes = free;
                SelectedDrive.UsedPercent = usedPercent;

                DeviceInfoText = $"{SelectedDrive.DisplayName}\nМетка: {di.VolumeLabel}\nВсего: {FormatBytes(total)} • Свободно: {FormatBytes(free)} • Занято: {usedPercent}%";
            }
            catch
            {
                DeviceInfoText = $"{SelectedDrive.DisplayName}\nОшибка чтения. Возможно, устройство не готово или повреждено.";
            }

            OnPropertyChanged(nameof(SelectedDrive));
        }

        private static string FormatBytes(long bytes)
        {
            const long KB = 1024;
            const long MB = KB * 1024;
            const long GB = MB * 1024;
            if (bytes >= GB) return $"{Math.Round(bytes / (double)GB, 2)} GB";
            if (bytes >= MB) return $"{Math.Round(bytes / (double)MB, 2)} MB";
            if (bytes >= KB) return $"{Math.Round(bytes / (double)KB, 2)} KB";
            return $"{bytes} B";
        }

        private async void RefreshButton_Click(object sender, RoutedEventArgs e)
        {
            await RefreshUsbListAsync();
        }

        private async Task RefreshUsbListAsync()
        {
            try
            {
                IsRefreshing = true;
                await Task.Delay(350);

                var drives = DriveInfo.GetDrives()
                    .Where(d => d.DriveType == DriveType.Removable && d.IsReady)
                    .ToArray();

                await Dispatcher.InvokeAsync(() =>
                {
                    UsbDrives.Clear();
                    foreach (var d in drives)
                    {
                        try
                        {
                            var item = new UsbDriveItem
                            {
                                DriveLetter = d.RootDirectory.FullName.Replace("\\", ""),
                                VolumeLabel = SafeGet(() => d.VolumeLabel, string.Empty),
                                DisplayName = $"{d.Name} {(string.IsNullOrWhiteSpace(d.VolumeLabel) ? "" : $"{d.VolumeLabel}")}",
                                TotalBytes = SafeGet(() => d.TotalSize, 0L),
                                FreeBytes = SafeGet(() => d.TotalFreeSpace, 0L)
                            };
                            item.UsedPercent = item.TotalBytes > 0
                                ? Math.Round((item.TotalBytes - item.FreeBytes) * 100.0 / item.TotalBytes, 1)
                                : 0.0;
                            UsbDrives.Add(item);
                        }
                        catch { continue; }
                    }

                    if (UsbDrives.Count == 0)
                    {
                        DeviceInfoText = "Съёмные USB-устройства не найдены.";
                        SelectedDrive = null;
                    }
                    else
                    {
                        if (SelectedDrive != null)
                        {
                            var found = UsbDrives.FirstOrDefault(x => x.DriveLetter == SelectedDrive.DriveLetter);
                            if (found != null) SelectedDrive = found;
                        }
                        if (SelectedDrive == null && UsbDrives.Count > 0) SelectedDrive = UsbDrives[0];
                    }
                }, DispatcherPriority.Background);
            }
            catch (Exception ex)
            {
                CustomMessageBox.Show($"Ошибка при сканировании USB: {ex.Message}", "Ошибка", MessageBoxButton.OK);
            }
            finally
            {
                IsRefreshing = false;
            }
        }

        private static T SafeGet<T>(Func<T> getter, T fallback)
        {
            try { return getter(); }
            catch { return fallback; }
        }

        // ------------------- Форматирование через DiskPart -------------------
        private async void FormatButton_Click(object sender, RoutedEventArgs e)
        {
            if (SelectedDrive == null)
            {
                CustomMessageBox.Show("Выберите накопитель для форматирования.", "Внимание", MessageBoxButton.OK);
                return;
            }

            var res = CustomMessageBox.Show($"Вы действительно хотите форматировать {SelectedDrive.DisplayName}?\nВсе данные на устройстве будут удалены.", "Подтвердите форматирование", MessageBoxButton.YesNo);
            if (res != CustomMessageBox.MessageBoxResult.Yes) return;

            try
            {
                IsRefreshing = true;
                DeviceInfoText = "Форматирование... Подождите.";

                await Task.Run(() =>
                {
                    // Определяем диск по номеру физического диска
                    var drive = SelectedDrive.ToDriveInfo();
                    if (drive == null)
                        throw new Exception("Не удалось получить информацию о диске.");

                    string diskNumber = GetDiskNumber(drive.RootDirectory.FullName);
                    if (diskNumber == null)
                        throw new Exception("Не удалось определить номер физического диска для DiskPart.");

                    // Создаём временный скрипт DiskPart
                    string scriptPath = Path.Combine(Path.GetTempPath(), "diskpart_script.txt");
                    File.WriteAllText(scriptPath,
$@"select disk {diskNumber}
clean
create partition primary
format fs=FAT32 quick
assign
exit");

                    // Запуск DiskPart
                    ProcessStartInfo psi = new ProcessStartInfo
                    {
                        FileName = "diskpart.exe",
                        Arguments = $"/s \"{scriptPath}\"",
                        UseShellExecute = false,
                        CreateNoWindow = true,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true
                    };

                    using var proc = Process.Start(psi);
                    proc?.WaitForExit();
                    int exitCode = proc?.ExitCode ?? 1;
                    if (exitCode != 0)
                        throw new Exception($"DiskPart завершился с кодом {exitCode}.");
                });

                CustomMessageBox.Show("Форматирование завершено.", "Готово", MessageBoxButton.OK);
            }
            catch (Exception ex)
            {
                CustomMessageBox.Show($"Ошибка форматирования: {ex.Message}", "Ошибка", MessageBoxButton.OK);
            }
            finally
            {
                IsRefreshing = false;
                await RefreshUsbListAsync();
            }
        }

        private string? GetDiskNumber(string driveLetter)
        {
            // Получаем физический диск через WMI
            try
            {
                var query = $"ASSOCIATORS OF {{Win32_LogicalDisk.DeviceID='{driveLetter.TrimEnd('\\')}'}} WHERE AssocClass=Win32_LogicalDiskToPartition";
                var searcher = new System.Management.ManagementObjectSearcher(query);
                foreach (System.Management.ManagementObject partition in searcher.Get())
                {
                    string deviceId = partition["DeviceID"]?.ToString(); // например "Disk #1, Partition #0"
                    if (deviceId != null && deviceId.Contains("#"))
                    {
                        int idx = deviceId.IndexOf('#') + 1;
                        int comma = deviceId.IndexOf(',', idx);
                        string num = comma > 0 ? deviceId.Substring(idx, comma - idx) : deviceId.Substring(idx);
                        return num.Trim();
                    }
                }
            }
            catch { }
            return null;
        }

        // ------------------- Ventoy установка/обновление -------------------
        private async void InstallButton_Click(object sender, RoutedEventArgs e) => await RunVentoyAsync(true);
        private async void UpdateButton_Click(object sender, RoutedEventArgs e) => await RunVentoyAsync(false);

        private async Task RunVentoyAsync(bool install)
        {
            if (SelectedDrive == null)
            {
                CustomMessageBox.Show("Выберите накопитель для операции.", "Внимание", MessageBoxButton.OK);
                return;
            }

            try
            {
                IsRefreshing = true;
                DeviceInfoText = install ? "Установка Ventoy... Подождите." : "Обновление Ventoy... Подождите.";

                await Task.Run(() =>
                {
                    string ventoyPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Ventoy", "Ventoy2Disk.exe");
                    if (!File.Exists(ventoyPath))
                        throw new FileNotFoundException("Не найден Ventoy2Disk.exe в папке Ventoy.", ventoyPath);

                    string driveParam = $"/Drive:{SelectedDrive.DriveLetter}";
                    string modeParam = install ? "/I" : "/U";

                    ProcessStartInfo psi = new ProcessStartInfo
                    {
                        FileName = ventoyPath,
                        Arguments = $"VTOYCLI {modeParam} {driveParam}",
                        CreateNoWindow = true,
                        UseShellExecute = false,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true
                    };

                    using var proc = Process.Start(psi);
                    proc?.WaitForExit();
                    int exitCode = proc?.ExitCode ?? 1;
                    if (exitCode != 0)
                        throw new Exception($"Ventoy завершился с кодом {exitCode}.");
                });

                CustomMessageBox.Show(install ? "Установка Ventoy завершена." : "Обновление Ventoy завершено.", "Готово", MessageBoxButton.OK);
            }
            catch (Exception ex)
            {
                CustomMessageBox.Show($"Ошибка при запуске Ventoy: {ex.Message}", "Ошибка", MessageBoxButton.OK);
            }
            finally
            {
                IsRefreshing = false;
                await RefreshUsbListAsync();
            }
        }

        private void IsoBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (Browse != null && LocalFilePathTextBox != null) { 
                if (IsoBox.SelectedIndex == 0)
                {
                    Browse.IsEnabled = true;
                    LocalFilePathTextBox.IsEnabled = true;
                }
                else
                {
                    Browse.IsEnabled = false;
                    LocalFilePathTextBox.IsEnabled = false;
                }
                if (IsoBox.SelectedItem is ComboBoxItem selectedItem)
                {
                    // 2. Получаем свойство Tag. Оно имеет тип object.
                    object tagValue = selectedItem.Tag;

                    // 3. Проверяем, что Tag не null, и приводим его к строке
                    if (tagValue != null)
                    {
                        // Возвращаем значение Tag, преобразованное в строку
                        LocalFilePathTextBox.Text = tagValue.ToString();
                    }
                    else { LocalFilePathTextBox.Text = ""; }
                }
            }

        }

        private void BrowseButton_Click(object sender, RoutedEventArgs e)
        {
            // 1. Создаем объект OpenFileDialog
            OpenFileDialog openFileDialog = new OpenFileDialog();

            // 2. Настраиваем фильтр для .iso файлов
            // Format: "Текстовое описание | *.расширение"
            // Добавляем опцию "Все файлы (*.*)" для удобства
            openFileDialog.Filter = "Образы ISO (*.iso)|*.iso|Все файлы (*.*)|*.*";

            // 3. Устанавливаем заголовок окна
            openFileDialog.Title = "Выберите ISO-файл";

            // 4. Открываем диалоговое окно. ShowDialog() возвращает bool?
            bool? result = openFileDialog.ShowDialog();

            // 5. Обрабатываем результат
            if (result == true)
            {
                // Пользователь выбрал файл. FullPath содержит полный путь к файлу.
                string selectedFilePath = openFileDialog.FileName;

                // Отображаем путь в вашем TextBox (предполагается, что он называется LocalFilePathTextBox)
                LocalFilePathTextBox.Text = selectedFilePath;
            }
        }
        private async void DownloadButton_Click(object sender, RoutedEventArgs e)
        {
            if (IsoBox.SelectedItem is not ComboBoxItem selectedItem)
            {
                CustomMessageBox.Show("Выберите образ для загрузки или копирования.", "Ошибка", MessageBoxButton.OK);
                return;
            }

            string? tag = selectedItem.Tag?.ToString();
            string isoName = selectedItem.Content?.ToString() ?? "image.iso";
            string downloads = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
            string destIsoPath = Path.Combine(downloads, $"{isoName.Replace(" ", "_")}.iso");

            if (SelectedDrive == null)
            {
                CustomMessageBox.Show("Сначала выберите USB-накопитель.", "Ошибка", MessageBoxButton.OK);
                return;
            }

            string usbPath = SelectedDrive.ToDriveInfo()?.RootDirectory.FullName ?? null;
            if (usbPath == null)
            {
                CustomMessageBox.Show("Не удалось определить путь к флешке.", "Ошибка", MessageBoxButton.OK);
                return;
            }
            if (!IsVentoyInstalled(usbPath))
            {
                CustomMessageBox.Show(
                    "На выбранном накопителе не обнаружен Ventoy.\nПожалуйста, установите Ventoy перед загрузкой или копированием образа.",
                    "Ventoy не найден", MessageBoxButton.OK);
                return;
            }

            try
            {
                _transferCts = new CancellationTokenSource();
                var token = _transferCts.Token;
                IsRefreshing = true;
                SetUiEnabled(false); // 🔥 БЛОКИРУЕМ интерфейс
                UpdateProgress(0, 0); // Сбрасываем прогресс-бар

                if (IsoBox.SelectedIndex == 0)
                {
                    // ----- 1. Локальный файл -----
                    string sourcePath = LocalFilePathTextBox.Text.Trim();
                    if (!File.Exists(sourcePath))
                    {
                        CustomMessageBox.Show("Укажите корректный путь к .ISO файлу.", "Ошибка", MessageBoxButton.OK);
                        // Выходим, finally-блок всё почистит и разблокирует UI
                        return;
                    }

                    string destPath = Path.Combine(usbPath, Path.GetFileName(sourcePath));
                    isoText.Text = "Копирование ISO на флешку...";

                    // 🔥 ГЛАВНОЕ ИЗМЕНЕНИЕ: Запускаем копирование в фоновом потоке
                    await Task.Run(async () =>
                        await CopyFileWithProgressAsync(sourcePath, destPath, token),
                    token);
                    SystemSounds.Beep.Play();
                    isoText.Text = "Копирование завершено.";
                }
                else if (!string.IsNullOrWhiteSpace(tag))
                {
                    // ----- 2. Загрузка -----
                    isoText.Text = "Проверка ссылки...";
                    if (!await IsDirectDownloadLinkAsync(tag))
                    {
                        Process.Start(new ProcessStartInfo { FileName = tag, UseShellExecute = true });
                        CustomMessageBox.Show(
                            "Невозможно получить прямую ссылку к файлу, пожалуйста загрузите его вручную, а после выберите 'Локальный файл'.",
                            "Нет прямой ссылки", MessageBoxButton.OK);
                        // Выходим, finally-блок всё почистит
                        return;
                    }

                    // 🔥 ГЛАВНОЕ ИЗМЕНЕНИЕ: Запускаем всю связку (загрузка + копирование) в фоновом потоке
                    await Task.Run(async () =>
                    {
                        // Обновляем UI из Task.Run через Dispatcher
                        Dispatcher.BeginInvoke(() => isoText.Text = "Загрузка ISO в папку Downloads...");

                        await DownloadFileWithProgressAsync(tag, destIsoPath, token);

                        token.ThrowIfCancellationRequested(); // Проверяем отмену между шагами

                        Dispatcher.BeginInvoke(() => isoText.Text = "Копирование ISO на флешку...");

                        string destPath = Path.Combine(usbPath, Path.GetFileName(destIsoPath));
                        await CopyFileWithProgressAsync(destIsoPath, destPath, token);

                    }, token);
                    SystemSounds.Beep.Play();
                    isoText.Text = "Файл загружен и скопирован на флешку.";
                }
            }
            catch (OperationCanceledException)
            {
                isoText.Text = "Операция отменена.";
                // Удаляем недокачанный/недокопированный файл, если нужно
            }
            catch (Exception ex)
            {
                CustomMessageBox.Show($"Ошибка: {ex.Message}", "Ошибка", MessageBoxButton.OK);
                isoText.Text = "Ошибка операции.";
            }
            finally
            {
                IsRefreshing = false;
                SetUiEnabled(true); // 🔥 РАЗБЛОКИРУЕМ интерфейс
                _transferCts?.Dispose(); // Освобождаем ресурсы
                _transferCts = null;
            }
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            _transferCts?.Cancel();
            isoText.Text = "Операция отменена.";
        }



        // ---------------------------
        // Проверка прямой ссылки
        // ---------------------------
        private async Task<bool> IsDirectDownloadLinkAsync(string url)
        {
            try
            {
                using var http = new HttpClient(new HttpClientHandler
                {
                    AllowAutoRedirect = false
                });
                using var response = await http.SendAsync(new HttpRequestMessage(HttpMethod.Head, url));
                if (response.StatusCode == HttpStatusCode.Found ||
                    response.StatusCode == HttpStatusCode.MovedPermanently ||
                    response.StatusCode == HttpStatusCode.SeeOther)
                    return false; // редирект — не прямая ссылка

                if (response.Content.Headers.ContentType == null)
                    return false;

                string type = response.Content.Headers.ContentType.MediaType.ToLower();
                return type.Contains("octet-stream") || type.Contains("iso");
            }
            catch
            {
                return false;
            }
        }

        // ---------------------------
        // Загрузка с прогрессом
        // ---------------------------
        private async Task DownloadFileWithProgressAsync(string url, string destPath, CancellationToken token)
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromHours(3) };
            using var response = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, token);
            response.EnsureSuccessStatusCode();

            long? totalBytes = response.Content.Headers.ContentLength;
            using var contentStream = await response.Content.ReadAsStreamAsync(token);
            using var fileStream = new FileStream(destPath, FileMode.Create, FileAccess.Write, FileShare.None, 8192, true);

            var sw = Stopwatch.StartNew();
            long totalRead = 0;
            byte[] buffer = new byte[8192];
            int bytesRead;

            _lastUpdate = DateTime.MinValue; // Сброс таймера для первого обновления

            while ((bytesRead = await contentStream.ReadAsync(buffer, 0, buffer.Length, token)) > 0)
            {
                await fileStream.WriteAsync(buffer, 0, bytesRead, token);
                totalRead += bytesRead;

                if (totalBytes.HasValue)
                {
                    double progress = totalRead * 100.0 / totalBytes.Value;
                    double mbps = (totalRead / 1024.0 / 1024.0) / sw.Elapsed.TotalSeconds;
                    UpdateProgress(progress, mbps);
                }
            }
            UpdateProgress(100, 0);
        }

        // ---------------------------
        // Копирование с прогрессом
        // ---------------------------
        private async Task CopyFileWithProgressAsync(string sourcePath, string destPath, CancellationToken token)
        {
            const int bufferSize = 1024 * 1024; // 1 MB
            long totalBytes = new FileInfo(sourcePath).Length;
            long totalRead = 0;
            var sw = Stopwatch.StartNew();

            using var source = new FileStream(sourcePath, FileMode.Open, FileAccess.Read, FileShare.Read, bufferSize, true);
            using var dest = new FileStream(destPath, FileMode.Create, FileAccess.Write, FileShare.None, bufferSize, true);

            byte[] buffer = new byte[bufferSize];
            int bytesRead;
            _lastUpdate = DateTime.MinValue; // Сброс таймера для первого обновления

            while ((bytesRead = await source.ReadAsync(buffer, 0, buffer.Length, token)) > 0)
            {
                await dest.WriteAsync(buffer, 0, bytesRead, token);
                totalRead += bytesRead;

                double progress = totalRead * 100.0 / totalBytes;
                double mbps = (totalRead / 1024.0 / 1024.0) / sw.Elapsed.TotalSeconds;
                UpdateProgress(progress, mbps);
            }
            UpdateProgress(100, 0);
        }

        // ---------------------------
        // Обновление прогресса (в isoText)
        // ---------------------------
        private void UpdateProgress(double percent, double mbps)
        {
            // Ограничиваем частоту обновления UI (не чаще 6–7 раз в секунду)
            if ((DateTime.Now - _lastUpdate).TotalMilliseconds < MinUpdateIntervalMs && percent < 100)
                return;

            _lastUpdate = DateTime.Now;

            // Используем BeginInvoke — НЕ ждём выполнения, просто бросаем в очередь UI
            Dispatcher.BeginInvoke(() =>
            {
                if (progressBar != null)
                    progressBar.Value = percent;

                if (isoText != null)
                    isoText.Text = percent >= 100
                        ? "Завершено"
                        : $"{percent:F1}% ({mbps:F2} МБ/с)";
            }, DispatcherPriority.Background); // или Render
        }
        private void SetUiEnabled(bool enabled)
        {
            // всё, кроме кнопки "Отмена"
            IsoBox.IsEnabled = enabled;
            DownloadButton.IsEnabled = enabled;
            Browse.IsEnabled = enabled;
            LocalFilePathTextBox.IsEnabled = enabled;
            DrivesListView.IsEnabled = enabled;
            RefreshButton.IsEnabled = enabled;
            UpdateButton.IsEnabled = enabled;
            InstallButton.IsEnabled = enabled;

            CancelButton.IsEnabled = !enabled; // наоборот: если интерфейс выключен — отмена включена
        }
        private bool IsVentoyInstalled(string usbRoot)
        {
            try
            {
                if (!Directory.Exists(usbRoot))
                    return false;

                // Проверяем наличие файлов/папок, где встречается "ventoy"
                var entries = Directory.GetFileSystemEntries(usbRoot);
                foreach (var entry in entries)
                {
                    string name = Path.GetFileName(entry).ToLowerInvariant();
                    if (name.Contains("ventoy"))
                        return true;
                }

                // Иногда Ventoy создаёт скрытую подпапку (например, EFI)
                var ventoyDir = Path.Combine(usbRoot, "ventoy");
                if (Directory.Exists(ventoyDir))
                    return true;

                return false;
            }
            catch
            {
                return false;
            }
        }

        private void Button_Click(object sender, RoutedEventArgs e)
        {
            HackMenu.Visibility = Visibility.Visible;
            HackButton.Visibility = Visibility.Hidden;
        }
}



    public class UsbDriveItem : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;

        public string DriveLetter { get; set; } = string.Empty;
        public string VolumeLabel { get; set; } = string.Empty;
        public string DisplayName { get; set; } = string.Empty;

        private long _totalBytes;
        public long TotalBytes { get => _totalBytes; set { _totalBytes = value; OnProp(nameof(TotalBytes)); } }

        private long _freeBytes;
        public long FreeBytes { get => _freeBytes; set { _freeBytes = value; OnProp(nameof(FreeBytes)); } }

        private double _usedPercent;
        public double UsedPercent { get => _usedPercent; set { _usedPercent = value; OnProp(nameof(UsedPercent)); } }

        private void OnProp(string n) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));

        public DriveInfo? ToDriveInfo()
        {
            try
            {
                var letter = DriveLetter;
                if (!letter.EndsWith(":"))
                    letter = letter.Length == 1 ? letter + ":" : letter.TrimEnd('\\');
                return new DriveInfo(letter);
            }
            catch { return null; }
        }
    }
    public class IsoItem
    {
        public string Name { get; set; } = string.Empty;
        public string? DownloadUrl { get; set; } // null для локального файла
    }

}
