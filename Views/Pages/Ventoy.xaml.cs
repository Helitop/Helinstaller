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
using Wpf.Ui.Controls;
using System.IO.Compression; // Для архивов
using System.Text.Json.Nodes; // Для работы с JSON Ventoy
using System.Text; // Для кодировок
using System.Text.Json; // Для сохранения JSON
using MessageBoxButton = System.Windows.MessageBoxButton;

namespace Helinstaller.Views.Pages
{

    public partial class Ventoy : Page, INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler PropertyChanged;
        private CancellationTokenSource? _transferCts;

        private DateTime _lastUpdate = DateTime.MinValue; // Новое поле для контроля частоты
        private const int MinUpdateIntervalMs = 200;     // Обновлять не чаще, чем раз в 150 мс


        public ObservableCollection<UsbDriveItem> UsbDrives { get; } = new ObservableCollection<UsbDriveItem>();
        // Коллекция для списка найденных ISO/IMG образов
        public ObservableCollection<IsoImageItem> FoundIsoImages { get; } = new ObservableCollection<IsoImageItem>();
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

        public class IsoImageItem
        {
            public string FileName { get; set; } = string.Empty;
            public string FullPath { get; set; } = string.Empty;

            public string Size { get; set; } = string.Empty;
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

        private async void UpdateDeviceInfo()
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
            await ScanIsoImagesAsync();
        }

        private async Task ScanIsoImagesAsync()
        {
            // Очищаем список в потоке UI
            await Dispatcher.InvokeAsync(FoundIsoImages.Clear);

            if (SelectedDrive == null) return;
            var driveInfo = SelectedDrive.ToDriveInfo();
            if (driveInfo == null || !driveInfo.IsReady) return;

            // Проверка, что Ventoy установлен
            if (!IsVentoyInstalled(SelectedDrive)) return;

            try
            {
                var images = await Task.Run(() =>
                {
                    var foundFiles = new List<IsoImageItem>();
                    var extensions = new[] { "*.iso", "*.img" };

                    // --- ИЗМЕНЕНИЕ: Получаем путь к папке ISO ---
                    string rootPath = driveInfo.RootDirectory.FullName;
                    string isoFolderPath = Path.Combine(rootPath, "ISO");
                    var isoDir = new DirectoryInfo(isoFolderPath);

                    // Если папки ISO нет, просто возвращаем пустой список (или можно искать в корне как запасной вариант)
                    if (!isoDir.Exists)
                    {
                        return foundFiles;
                    }

                    foreach (var ext in extensions)
                    {
                        // Ищем файлы внутри папки G:\ISO\
                        // SearchOption.TopDirectoryOnly берет файлы только из папки ISO (без вложенных)
                        foreach (var file in isoDir.GetFiles(ext, SearchOption.TopDirectoryOnly))
                        {
                            foundFiles.Add(new IsoImageItem
                            {
                                FileName = file.Name,
                                FullPath = file.FullName,
                                Size = FormatBytes(file.Length)
                            });
                        }
                    }
                    return foundFiles.OrderBy(f => f.FileName).ToList();
                });

                // Обновляем коллекцию в потоке UI
                await Dispatcher.InvokeAsync(() =>
                {
                    foreach (var item in images)
                    {
                        FoundIsoImages.Add(item);
                    }
                });
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Ошибка сканирования ISO: {ex.Message}");
            }
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
                CustomMessageBox.Show($"Ошибка при сканировании USB: {ex.Message}", "Ошибка", System.Windows.MessageBoxButton.OK);
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
                CustomMessageBox.Show("Выберите накопитель для форматирования.", "Внимание", System.Windows.MessageBoxButton.OK);
                return;
            }

            var res = CustomMessageBox.Show($"Вы действительно хотите форматировать {SelectedDrive.DisplayName}?\nВсе данные на устройстве будут удалены.", "Подтвердите форматирование", System.Windows.MessageBoxButton.YesNo);
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

                CustomMessageBox.Show("Форматирование завершено.", "Готово", System.Windows.MessageBoxButton.OK);
            }
            catch (Exception ex)
            {
                CustomMessageBox.Show($"Ошибка форматирования: {ex.Message}", "Ошибка", System.Windows.MessageBoxButton.OK);
            }
            finally
            {
                IsRefreshing = false;
                await RefreshUsbListAsync();
                await ScanIsoImagesAsync();
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
                CustomMessageBox.Show("Выберите накопитель для операции.", "Внимание", System.Windows.MessageBoxButton.OK);
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

                CustomMessageBox.Show(install ? "Установка Ventoy завершена." : "Обновление Ventoy завершено.", "Готово", System.Windows.MessageBoxButton.OK);
            }
            catch (Exception ex)
            {
                CustomMessageBox.Show($"Ошибка при запуске Ventoy: {ex.Message}", "Ошибка", System.Windows.MessageBoxButton.OK);
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
            // 1. Проверки UI
            if (IsoBox.SelectedItem is not ComboBoxItem selectedItem)
            {
                CustomMessageBox.Show("Выберите образ для загрузки или копирования.", "Ошибка", MessageBoxButton.OK);
                return;
            }

            if (SelectedDrive == null)
            {
                CustomMessageBox.Show("Сначала выберите USB-накопитель.", "Ошибка", MessageBoxButton.OK);
                return;
            }

            if (!IsVentoyInstalled(SelectedDrive))
            {
                CustomMessageBox.Show("На выбранном накопителе не обнаружен Ventoy.\nСначала установите Ventoy.", "Ventoy не найден", MessageBoxButton.OK);
                return;
            }

            // 2. Подготовка путей
            string? tag = selectedItem.Tag?.ToString();
            string isoName = selectedItem.Content?.ToString() ?? "image.iso";

            // Формируем имя файла (заменяем пробелы)
            string cleanIsoName = $"{isoName.Replace(" ", "_")}.iso";
            if (!cleanIsoName.EndsWith(".iso", StringComparison.OrdinalIgnoreCase))
                cleanIsoName += ".iso";

            string downloads = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
            string localTempIsoPath = Path.Combine(downloads, cleanIsoName);

            // Получаем путь к корню флешки (например G:\)
            var usbDriveInfo = SelectedDrive.ToDriveInfo();
            var usbRootPath = usbDriveInfo?.RootDirectory.FullName;

            if (usbRootPath == null)
            {
                CustomMessageBox.Show("Не удалось определить путь к флешке.", "Ошибка", MessageBoxButton.OK);
                return;
            }

            // --- НОВОЕ: Создаем папку ISO, если её нет ---
            string usbIsoFolder = Path.Combine(usbRootPath, "ISO");
            if (!Directory.Exists(usbIsoFolder))
            {
                try { Directory.CreateDirectory(usbIsoFolder); }
                catch { CustomMessageBox.Show("Не удалось создать папку ISO на флешке.", "Ошибка", MessageBoxButton.OK); return; }
            }

            // Итоговый путь на флешке: G:\ISO\Windows11.iso
            string destPathOnUsb = Path.Combine(usbIsoFolder, cleanIsoName);

            try
            {
                _transferCts = new CancellationTokenSource();
                var token = _transferCts.Token;
                IsRefreshing = true;
                SetUiEnabled(false);
                UpdateProgress(0, 0);

                // --- ЛОГИКА ЗАГРУЗКИ / КОПИРОВАНИЯ ---
                if (IsoBox.SelectedIndex == 0) // Локальный файл
                {
                    string sourcePath = LocalFilePathTextBox.Text.Trim();
                    if (!File.Exists(sourcePath))
                    {
                        CustomMessageBox.Show("Укажите корректный путь к .ISO файлу.", "Ошибка", MessageBoxButton.OK);
                        return;
                    }

                    // Переопределяем имя файла, если копируем локальный (чтобы сохранить оригинальное имя)
                    string localFileName = Path.GetFileName(sourcePath);
                    destPathOnUsb = Path.Combine(usbIsoFolder, localFileName);
                    cleanIsoName = localFileName; // Обновляем имя для JSON

                    isoText.Text = "Копирование ISO в папку /ISO/ ...";
                    await Task.Run(async () => await CopyFileWithProgressAsync(sourcePath, destPathOnUsb, token), token);
                }
                else if (!string.IsNullOrWhiteSpace(tag)) // Скачивание из сети
                {
                    isoText.Text = "Проверка ссылки...";
                    if (!await IsDirectDownloadLinkAsync(tag))
                    {
                        Process.Start(new ProcessStartInfo { FileName = tag, UseShellExecute = true });
                        CustomMessageBox.Show("Нет прямой ссылки. Загрузите вручную.", "Ошибка", MessageBoxButton.OK);
                        return;
                    }

                    await Task.Run(async () =>
                    {
                        Dispatcher.BeginInvoke(() => isoText.Text = "Загрузка ISO...");
                        // Скачиваем сначала в Загрузки
                        await DownloadFileWithProgressAsync(tag, localTempIsoPath, token);

                        token.ThrowIfCancellationRequested();

                        Dispatcher.BeginInvoke(() => isoText.Text = "Перенос в папку /ISO/...");
                        // Копируем из Загрузок на Флешку в папку ISO
                        await CopyFileWithProgressAsync(localTempIsoPath, destPathOnUsb, token);
                    }, token);
                }

                // --- ВШИВАНИЕ OOBE (С учетом новой папки) ---
                isoText.Text = "Настройка ventoy.json...";

                // Мы передаем имя файла, чтобы сформировать путь "/ISO/Windows.iso"
                await InjectOobeAutoAsync(usbRootPath);

                SystemSounds.Beep.Play();
                isoText.Text = "Готово! Образ в папке ISO, JSON обновлен.";
                CustomMessageBox.Show("Образ записан в папку ISO.\nКонфигурация Ventoy обновлена!", "Успех", MessageBoxButton.OK);
            }
            catch (OperationCanceledException)
            {
                isoText.Text = "Операция отменена.";
            }
            catch (Exception ex)
            {
                CustomMessageBox.Show($"Ошибка: {ex.Message}", "Ошибка", MessageBoxButton.OK);
                isoText.Text = "Ошибка операции.";
            }
            finally
            {
                IsRefreshing = false;
                SetUiEnabled(true);
                _transferCts?.Dispose();
                _transferCts = null;
                await ScanIsoImagesAsync();
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
            if (IsoBox.SelectedIndex == 0)
            {
                LocalFilePathTextBox.IsEnabled = enabled;
            }
            DrivesListView.IsEnabled = enabled;
            RefreshButton.IsEnabled = enabled;
            UpdateButton.IsEnabled = enabled;
            InstallButton.IsEnabled = enabled;

            CancelButton.IsEnabled = !enabled; // наоборот: если интерфейс выключен — отмена включена
        }
        private bool IsVentoyInstalled(UsbDriveItem usbRoot)
        {
            try
            {
                if (usbRoot.DisplayName.ToLower().Contains("ventoy") && !usbRoot.DisplayName.ToLower().Contains("efi"))
                    return true;
                else
                {
                    CustomMessageBox.Show("Вероятно, Ventoy не установлен на накопителе, или вы пытаетесь взаимодействовать с EFI разделом Ventoy","");
                    return false;
                }
                    
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

        private async void Button_Click_1(object sender, RoutedEventArgs e)
        {
            if (ImagesList.SelectedItem != null)
            {
                var resp = CustomMessageBox.Show("Вы точно хотите удалить образ с выбранного накопителя?", "", System.Windows.MessageBoxButton.YesNo);
                if (resp == CustomMessageBox.MessageBoxResult.Yes)
                {
                    string path = (ImagesList.SelectedItem as IsoImageItem).FullPath;
                    File.Delete(path);
                    await ScanIsoImagesAsync();
                }
            }
            else { CustomMessageBox.Show("Сначала выберите файл для удаления", "", System.Windows.MessageBoxButton.OK); }
            
        }

        /// <summary>
        /// Копирует autounattend.xml из локальной папки Assets в корень флешки.
        /// </summary>
        private async Task CreateAutounattend(string driveRootPath)
        {
            try
            {
                // 1. Вычисляем путь к исходному файлу.
                // AppDomain.CurrentDomain.BaseDirectory — это папка, где лежит запущенный .exe
                string sourcePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Assets", "autounattend.xml");

                // 2. Вычисляем путь назначения (корень флешки)
                string destPath = Path.Combine(driveRootPath, "autounattend.xml");

                // 3. Проверяем, существует ли файл в папке программы
                if (!File.Exists(sourcePath))
                {
                    CustomMessageBox.Show($"Файл не найден по пути:\n{sourcePath}\n\nУбедитесь, что папка 'Assets' существует рядом с exe и в ней есть файл.",
                                    "Ошибка конфигурации", MessageBoxButton.OK);
                    return;
                }

                // 4. Копируем файл (overwrite: true разрешает замену старого файла)
                // Используем Task.Run, чтобы операция ввода-вывода не морозила интерфейс
                await Task.Run(() =>
                {
                    File.Copy(sourcePath, destPath, true);
                });

                // (Опционально) Можно вывести сообщение об успехе или просто молча продолжить
                // MessageBox.Show("Файл ответов успешно скопирован!", "Успех", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                CustomMessageBox.Show($"Ошибка при копировании autounattend.xml: {ex.Message}",
                                "Ошибка", MessageBoxButton.OK);
            }
        }

        private async Task InjectOobeAutoAsync(string usbRootPath)
        {
            string ventoyDir = Path.Combine(usbRootPath, "ventoy");
            string jsonPath = Path.Combine(ventoyDir, "ventoy.json");
            if (!Path.Exists(ventoyDir))
            {
                Directory.CreateDirectory(ventoyDir);
            }
            try
            {
                IsRefreshing = true;

                // Настройка JSON (Правильная версия с полной перезаписью)
                Dispatcher.Invoke(() => isoText.Text = "Настройка Ventoy (JSON)...");

                var ventoyConfig = new
                {
                    control = new[]
    {
        new { VTOY_MENU_LANGUAGE = "ru_RU" }
    },
                    auto_install = new[]
    {
        new
        {
            parent = "/ISO",      // Папка, к образам в которой применится скрипт
            template = new[]
            {
                "/autounattend.xml" // Путь к скрипту (в корне флешки)
            }
        }
    }
                };

                var jsonOptions = new JsonSerializerOptions { WriteIndented = true };
                string jsonString = JsonSerializer.Serialize(ventoyConfig, jsonOptions);
                File.WriteAllText(jsonPath, jsonString, Encoding.UTF8);
                await CreateAutounattend(usbRootPath);
            }
            catch (Exception ex)
            {
                CustomMessageBox.Show(ex.Message,"");
            }
            finally
            {
                IsRefreshing = false;
            }
        }

        private async void InjectOnlyButton_Click(object sender, RoutedEventArgs e)
        {
            var root = SelectedDrive.ToDriveInfo()?.RootDirectory.FullName;
            if (root == null || !IsVentoyInstalled(SelectedDrive)) return;
            await InjectOobeAutoAsync(root);
            isoText.Text = "Готово!";
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
