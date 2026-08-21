using Helinstaller.Models;
using Helinstaller.Views.Windows;
using Microsoft.Win32;
using Schneegans.Unattend;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Media;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Wpf.Ui.Controls;

namespace Helinstaller.Views.Pages
{
    public partial class Ventoy : Page, INotifyPropertyChanged
    {
        private FileSystemWatcher? _downloadsWatcher;
        private string? _lastPromptedFile;
        private DateTime _lastPromptTime = DateTime.MinValue;
        private DownloadTask? _ventoyTask;
        public event PropertyChangedEventHandler? PropertyChanged;
        private CancellationTokenSource? _transferCts;

        private DateTime _lastUpdate = DateTime.MinValue;
        private const int MinUpdateIntervalMs = 200;

        public ObservableCollection<UsbDriveItem> UsbDrives { get; } = new ObservableCollection<UsbDriveItem>();
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

        private string _deviceLeftText = "Устройство не выбрано.";
        public string DeviceLeftText
        {
            get => _deviceLeftText;
            set
            {
                if (_deviceLeftText != value)
                {
                    _deviceLeftText = value;
                    OnPropertyChanged(nameof(DeviceLeftText));
                }
            }
        }

        private string _deviceRightText = "";
        public string DeviceRightText
        {
            get => _deviceRightText;
            set
            {
                if (_deviceRightText != value)
                {
                    _deviceRightText = value;
                    OnPropertyChanged(nameof(DeviceRightText));
                }
            }
        }

        public bool CanFormat => SelectedDrive != null && !IsRefreshing;

        public Ventoy()
        {
            InitializeComponent();
            DataContext = this;

            _ = RefreshUsbListAsync();
            StartDownloadsWatcher();

            this.Unloaded += (s, e) =>
            {
                if (_downloadsWatcher != null)
                {
                    _downloadsWatcher.EnableRaisingEvents = false;
                    _downloadsWatcher.Dispose();
                    _downloadsWatcher = null;
                }
            };
        }

        private void StartDownloadsWatcher()
        {
            try
            {
                string downloadsPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
                if (!Directory.Exists(downloadsPath)) return;

                _downloadsWatcher = new FileSystemWatcher
                {
                    Path = downloadsPath,
                    Filter = "*.*",
                    EnableRaisingEvents = true
                };

                _downloadsWatcher.Renamed += OnDownloadsFileRenamed;
                _downloadsWatcher.Created += OnDownloadsFileCreated;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[FileSystemWatcher Error]: {ex.Message}");
            }
        }

        private void OnDownloadsFileCreated(object sender, FileSystemEventArgs e) => HandleDetectedFile(e.FullPath);
        private void OnDownloadsFileRenamed(object sender, RenamedEventArgs e) => HandleDetectedFile(e.FullPath);

        private void HandleDetectedFile(string filePath)
        {
            string ext = Path.GetExtension(filePath).ToLower();
            if (ext == ".iso" || ext == ".img")
            {
                Dispatcher.BeginInvoke(async () =>
                {
                    await PromptToCopyIsoAsync(filePath);
                });
            }
        }

        private void SearchImagesButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                string url = "https://massgrave.dev/genuine-installation-media";
                Process.Start(new ProcessStartInfo { FileName = url, UseShellExecute = true });
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[Massgrave Open Error]: {ex.Message}");
            }
        }

        private async Task PromptToCopyIsoAsync(string filePath)
        {
            if (filePath == _lastPromptedFile && (DateTime.Now - _lastPromptTime).TotalSeconds < 5)
                return;

            _lastPromptedFile = filePath;
            _lastPromptTime = DateTime.Now;

            await Task.Delay(1000);

            if (!File.Exists(filePath)) return;
            if (IsRefreshing) return;

            string fileName = Path.GetFileName(filePath);

            bool confirm = await ShowUiConfirmBoxAsync(
                "Загрузка завершена!",
                $"Обнаружен готовый образ системы в папке «Загрузки»:\n\n\"{fileName}\"\n\nХотите автоматически выбрать его и начать запись на флешку?"
            );

            if (confirm)
            {
                LocalFilePathTextBox.Text = filePath;
                DownloadButton_Click(this, new RoutedEventArgs());
            }
        }

        private void OnPropertyChanged(string name) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

        private async void UpdateDeviceInfo()
        {
            if (SelectedDrive == null)
            {
                DeviceLeftText = "Устройство не выбрано.";
                DeviceRightText = "";
                return;
            }

            try
            {
                var di = SelectedDrive.ToDriveInfo();
                if (di == null || !di.IsReady)
                {
                    DeviceLeftText = $"{SelectedDrive.DisplayName} (Диск не готов)";
                    DeviceRightText = "";
                    return;
                }

                long total = di.TotalSize;
                long free = di.TotalFreeSpace;
                long used = total - free;
                double usedPercent = total > 0 ? Math.Round(used * 100.0 / total, 1) : 0.0;

                SelectedDrive.TotalBytes = total;
                SelectedDrive.FreeBytes = free;
                SelectedDrive.UsedPercent = usedPercent;

                DeviceLeftText = $"{SelectedDrive.DisplayName}\n{(string.IsNullOrWhiteSpace(di.VolumeLabel) ? "" : $"[{di.VolumeLabel}]")}";
                DeviceRightText = $"Всего: {FormatBytes(total)}\nСвободно: {FormatBytes(free)}\nЗанято: {usedPercent}%";
            }
            catch
            {
                DeviceLeftText = SelectedDrive.DisplayName;
                DeviceRightText = "Ошибка чтения данных";
            }

            OnPropertyChanged(nameof(SelectedDrive));
            await ScanIsoImagesAsync();
        }

        private async Task ScanIsoImagesAsync()
        {
            await Dispatcher.InvokeAsync(FoundIsoImages.Clear);

            if (SelectedDrive == null) return;
            var driveInfo = SelectedDrive.ToDriveInfo();
            if (driveInfo == null || !driveInfo.IsReady) return;

            if (!IsVentoyInstalled(SelectedDrive)) return;

            try
            {
                var images = await Task.Run(() =>
                {
                    var foundFiles = new List<IsoImageItem>();
                    var extensions = new[] { "*.iso", "*.img" };

                    string rootPath = driveInfo.RootDirectory.FullName;
                    string isoFolderPath = Path.Combine(rootPath, "ISO");
                    var isoDir = new DirectoryInfo(isoFolderPath);

                    if (!isoDir.Exists)
                    {
                        return foundFiles;
                    }

                    foreach (var ext in extensions)
                    {
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
                        DeviceLeftText = "Съёмные USB-устройства не найдены.";
                        DeviceRightText = "";
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
                await ShowUiMessageBoxAsync("Ошибка", $"Ошибка при сканировании USB: {ex.Message}");
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

        public string DeviceInfoText
        {
            get => DeviceLeftText;
            set
            {
                DeviceLeftText = value;
                DeviceRightText = "";
            }
        }

        private bool IsVentoyInstalled(UsbDriveItem? drive)
        {
            if (drive == null) return false;
            return drive.DisplayName.ToLower().Contains("ventoy") && !drive.DisplayName.ToLower().Contains("efi");
        }

        private async void FormatButton_Click(object sender, RoutedEventArgs e)
        {
            if (SelectedDrive == null)
            {
                await ShowUiMessageBoxAsync("Внимание", "Выберите накопитель для форматирования.");
                return;
            }

            var res = await ShowUiConfirmBoxAsync("Подтвердите форматирование",
                $"Вы действительно хотите форматировать {SelectedDrive.DisplayName}?\nВсе данные на устройстве будут безвозвратно удалены.");

            if (!res) return;

            string? scriptPath = null;
            try
            {
                IsRefreshing = true;
                DeviceInfoText = "Форматирование... Подождите.";

                await Task.Run(() =>
                {
                    var drive = SelectedDrive.ToDriveInfo();
                    if (drive == null)
                        throw new Exception("Не удалось получить информацию о диске.");

                    string diskNumber = GetDiskNumber(drive.RootDirectory.FullName);
                    if (diskNumber == null)
                        throw new Exception("Не удалось определить номер физического диска для DiskPart.");

                    scriptPath = Path.Combine(Path.GetTempPath(), $"diskpart_script_{Guid.NewGuid():N}.txt");
                    File.WriteAllText(scriptPath,
$@"select disk {diskNumber}
clean
create partition primary
format fs=FAT32 quick
assign
exit");

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

                await ShowUiMessageBoxAsync("Готово", "Форматирование успешно завершено.");
            }
            catch (Exception ex)
            {
                await ShowUiMessageBoxAsync("Ошибка форматирования", ex.Message);
            }
            finally
            {
                if (scriptPath != null && File.Exists(scriptPath))
                {
                    try { File.Delete(scriptPath); } catch { }
                }

                IsRefreshing = false;
                await RefreshUsbListAsync();
                await ScanIsoImagesAsync();
            }
        }

        private string? GetDiskNumber(string driveLetter)
        {
            try
            {
                var query = $"ASSOCIATORS OF {{Win32_LogicalDisk.DeviceID='{driveLetter.TrimEnd('\\')}'}} WHERE AssocClass=Win32_LogicalDiskToPartition";
                using var searcher = new System.Management.ManagementObjectSearcher(query);
                using var results = searcher.Get();

                foreach (System.Management.ManagementObject partition in results)
                {
                    using (partition)
                    {
                        string? deviceId = partition["DeviceID"]?.ToString();
                        if (deviceId != null && deviceId.Contains("#"))
                        {
                            int idx = deviceId.IndexOf('#') + 1;
                            int comma = deviceId.IndexOf(',', idx);
                            string num = comma > 0 ? deviceId.Substring(idx, comma - idx) : deviceId.Substring(idx);
                            return num.Trim();
                        }
                    }
                }
            }
            catch { }
            return null;
        }

        private async void InstallButton_Click(object sender, RoutedEventArgs e) => await RunVentoyAsync(true);
        private async void UpdateButton_Click(object sender, RoutedEventArgs e) => await RunVentoyAsync(false);

        private async Task RunVentoyAsync(bool install)
        {
            if (SelectedDrive == null)
            {
                await ShowUiMessageBoxAsync("Внимание", "Выберите накопитель для операции.");
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

                await ShowUiMessageBoxAsync("Готово", install ? "Установка Ventoy завершена." : "Обновление Ventoy завершено.");
            }
            catch (Exception ex)
            {
                await ShowUiMessageBoxAsync("Ошибка", $"Ошибка при работе Ventoy: {ex.Message}");
            }
            finally
            {
                IsRefreshing = false;
                await RefreshUsbListAsync();
            }
        }

        private void BrowseButton_Click(object sender, RoutedEventArgs e)
        {
            OpenFileDialog openFileDialog = new OpenFileDialog
            {
                Filter = "Образы ISO/IMG (*.iso;*.img)|*.iso;*.img|Все файлы (*.*)|*.*",
                Title = "Выберите образ системы"
            };

            bool? result = openFileDialog.ShowDialog();
            if (result == true)
            {
                LocalFilePathTextBox.Text = openFileDialog.FileName;
            }
        }

        private async void DownloadButton_Click(object sender, RoutedEventArgs e)
        {
            if (SelectedDrive == null)
            {
                await ShowUiMessageBoxAsync("Ошибка", "Сначала выберите USB-накопитель.");
                return;
            }

            if (!IsVentoyInstalled(SelectedDrive))
            {
                await ShowUiMessageBoxAsync("Ventoy не найден",
                    "На выбранном накопителе не обнаружен установленный Ventoy.\n\nПожалуйста, сначала установите Ventoy перед записью образов.");
                return;
            }

            string sourcePath = LocalFilePathTextBox.Text.Trim();
            if (string.IsNullOrWhiteSpace(sourcePath) || !File.Exists(sourcePath))
            {
                await ShowUiMessageBoxAsync("Ошибка", "Укажите корректный путь к существующему .ISO/.IMG файлу.");
                return;
            }

            string fileName = Path.GetFileName(sourcePath);
            var usbDriveInfo = SelectedDrive.ToDriveInfo();
            var usbRootPath = usbDriveInfo?.RootDirectory.FullName;

            if (usbRootPath == null)
            {
                await ShowUiMessageBoxAsync("Ошибка", "Не удалось определить путь к флешке.");
                return;
            }

            string usbIsoFolder = Path.Combine(usbRootPath, "ISO");
            if (!Directory.Exists(usbIsoFolder))
            {
                try { Directory.CreateDirectory(usbIsoFolder); }
                catch { await ShowUiMessageBoxAsync("Ошибка", "Не удалось создать папку ISO на флешке."); return; }
            }

            string destPathOnUsb = Path.Combine(usbIsoFolder, fileName);

            try
            {
                _transferCts = new CancellationTokenSource();
                var token = _transferCts.Token;
                IsRefreshing = true;
                SetUiEnabled(false);

                _ventoyTask = new DownloadTask
                {
                    Title = $"Копирование: {fileName}",
                    AppName = "Ventoy",
                    IconPath = ""
                };
                DownloadTaskManager.Instance.AddTask(_ventoyTask);

                UpdateProgress(0, 0);

                isoText.Text = "Копирование файла на накопитель...";
                _ventoyTask.Status = "Копирование файла...";

                await Task.Run(async () => await CopyFileWithProgressAsync(sourcePath, destPathOnUsb, token), token);

                isoText.Text = "Настройка ventoy.json...";
                _ventoyTask.Status = "Настройка OOBE...";
                await InjectOobeAutoAsync(usbRootPath);

                SystemSounds.Beep.Play();

                if (_ventoyTask != null)
                {
                    _ventoyTask.Progress = 100;
                    _ventoyTask.Status = "Завершено";
                    _ventoyTask.IsCompleted = true;
                }

                isoText.Text = "Готово! Образ успешно записан.";
                await ShowUiMessageBoxAsync("Успех", "Образ успешно записан в папку ISO!\nКонфигурация автоустановщика (OOBE) настроена.");
            }
            catch (OperationCanceledException)
            {
                isoText.Text = "Операция отменена.";
                if (_ventoyTask != null)
                {
                    _ventoyTask.Status = "Отменено";
                    _ventoyTask.IsCompleted = true;
                }
            }
            catch (Exception ex)
            {
                if (_ventoyTask != null)
                {
                    _ventoyTask.Status = "Ошибка";
                    _ventoyTask.IsError = true;
                    _ventoyTask.ErrorMessage = ex.Message;
                }
                await ShowUiMessageBoxAsync("Ошибка", $"Произошла ошибка при копировании: {ex.Message}");
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

        private async Task CopyFileWithProgressAsync(string sourcePath, string destPath, CancellationToken token)
        {
            const int bufferSize = 1024 * 1024;
            long totalBytes = new FileInfo(sourcePath).Length;
            long totalRead = 0;
            var sw = Stopwatch.StartNew();

            using var source = new FileStream(sourcePath, FileMode.Open, FileAccess.Read, FileShare.Read, bufferSize, true);
            using var dest = new FileStream(destPath, FileMode.Create, FileAccess.Write, FileShare.None, bufferSize, true);

            byte[] buffer = new byte[bufferSize];
            int bytesRead;
            _lastUpdate = DateTime.MinValue;

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

        private double _driveTransferProgress;
        public double DriveTransferProgress
        {
            get => _driveTransferProgress;
            set
            {
                if (Math.Abs(_driveTransferProgress - value) > 0.01)
                {
                    _driveTransferProgress = value;
                    OnPropertyChanged(nameof(DriveTransferProgress));
                }
            }
        }

        private string _driveTransferStatus = "Ожидание...";
        public string DriveTransferStatus
        {
            get => _driveTransferStatus;
            set
            {
                if (_driveTransferStatus != value)
                {
                    _driveTransferStatus = value;
                    OnPropertyChanged(nameof(DriveTransferStatus));
                }
            }
        }

        private async Task ShowUiMessageBoxAsync(string title, string content)
        {
            await Dispatcher.Invoke(async () =>
            {
                var msg = new Wpf.Ui.Controls.MessageBox
                {
                    Title = title,
                    Content = content,
                    CloseButtonText = "ОК"
                };
                await msg.ShowDialogAsync();
            });
        }

        private async Task<bool> ShowUiConfirmBoxAsync(string title, string content)
        {
            return await Dispatcher.Invoke(async () =>
            {
                var msg = new Wpf.Ui.Controls.MessageBox
                {
                    Title = title,
                    Content = content,
                    PrimaryButtonText = "Да",
                    CloseButtonText = "Нет"
                };
                var result = await msg.ShowDialogAsync();
                return result == Wpf.Ui.Controls.MessageBoxResult.Primary;
            });
        }

        private void ShowHelpButton_Click(object sender, RoutedEventArgs e)
        {
            HelpDialog.ShowHelp(Window.GetWindow(this), "ventoy");
        }

        private void UpdateProgress(double percent, double mbps)
        {
            if ((DateTime.Now - _lastUpdate).TotalMilliseconds < MinUpdateIntervalMs && percent < 100)
                return;

            _lastUpdate = DateTime.Now;

            Dispatcher.BeginInvoke(() =>
            {
                if (progressBar != null)
                {
                    progressBar.IsIndeterminate = percent <= 0;
                    progressBar.Value = percent;
                }

                string status = percent >= 100
                    ? "Завершено"
                    : $"{percent:F1}% ({mbps:F2} МБ/с)";

                if (isoText != null)
                    isoText.Text = status;

                DriveTransferProgress = percent;
                DriveTransferStatus = status;

                if (_ventoyTask != null)
                {
                    _ventoyTask.Progress = percent;
                    _ventoyTask.Status = status;
                }

            }, DispatcherPriority.Background);
        }

        private void SetUiEnabled(bool enabled)
        {
            DownloadButton.IsEnabled = enabled;
            Browse.IsEnabled = enabled;
            DrivesListView.IsEnabled = enabled;
            RefreshButton.IsEnabled = enabled;
            UpdateButton.IsEnabled = enabled;
            InstallButton.IsEnabled = enabled;
            CancelButton.IsEnabled = !enabled;
        }

        private async void Button_Click_1(object sender, RoutedEventArgs e)
        {
            if (ImagesList.SelectedItem is IsoImageItem selectedIso)
            {
                var resp = await ShowUiConfirmBoxAsync("Подтверждение удаления", "Вы точно хотите удалить образ с выбранного накопителя?");
                if (resp)
                {
                    try
                    {
                        if (File.Exists(selectedIso.FullPath))
                        {
                            File.Delete(selectedIso.FullPath);
                        }
                        await ScanIsoImagesAsync();
                    }
                    catch (Exception ex)
                    {
                        await ShowUiMessageBoxAsync("Ошибка", ex.Message);
                    }
                }
            }
            else
            {
                await ShowUiMessageBoxAsync("Внимание", "Сначала выберите файл для удаления.");
            }
        }

        private async void DeleteCardButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement element && element.DataContext is IsoImageItem selectedItem)
            {
                var resp = await ShowUiConfirmBoxAsync("Подтверждение удаления",
                    $"Вы действительно хотите безвозвратно удалить образ:\n\"{selectedItem.FileName}\"?");

                if (resp)
                {
                    try
                    {
                        if (File.Exists(selectedItem.FullPath))
                        {
                            File.Delete(selectedItem.FullPath);
                        }
                        await ScanIsoImagesAsync();
                    }
                    catch (Exception ex)
                    {
                        await ShowUiMessageBoxAsync("Ошибка удаления",
                            $"Не удалось удалить файл. Возможно, он занят другим процессом.\n\nОшибка: {ex.Message}");
                    }
                }
            }
        }

        private async Task CreateAutounattend(string driveRootPath)
        {
            try
            {
                string sourcePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Assets", "autounattend.xml");
                string destPath = Path.Combine(driveRootPath, "autounattend.xml");

                if (!File.Exists(sourcePath))
                {
                    await ShowUiMessageBoxAsync("Ошибка конфигурации",
                        $"Файл не найден по пути:\n{sourcePath}\n\nУбедитесь, что папка 'Assets' существует рядом с запускаемым .exe.");
                    return;
                }

                await Task.Run(() =>
                {
                    File.Copy(sourcePath, destPath, true);
                });
            }
            catch (Exception ex)
            {
                await ShowUiMessageBoxAsync("Ошибка копирования", $"Ошибка при развертывании autounattend.xml: {ex.Message}");
            }
        }

        private async void InjectOnlyButton_Click(object sender, RoutedEventArgs e)
        {
            var root = SelectedDrive?.ToDriveInfo()?.RootDirectory.FullName;
            if (root == null)
            {
                await ShowUiMessageBoxAsync("Ошибка", "Сначала выберите подключенный USB накопитель.");
                return;
            }

            if (!IsVentoyInstalled(SelectedDrive))
            {
                await ShowUiMessageBoxAsync("Накопитель не готов", "Для конфигурирования автоустановки на флешке должен быть установлен Ventoy.");
                return;
            }

            var configWindow = new UnattendConfigWindow
            {
                Owner = Window.GetWindow(this)
            };

            if (configWindow.ShowDialog() == true && configWindow.GeneratedXmlBytes != null)
            {
                try
                {
                    IsRefreshing = true;
                    isoText.Text = "Запись файлов автоустановщика...";

                    string xmlDestPath = Path.Combine(root, "autounattend.xml");

                    await Task.Run(() =>
                    {
                        File.WriteAllBytes(xmlDestPath, configWindow.GeneratedXmlBytes);
                    });

                    await InjectOobeAutoAsync(root);

                    isoText.Text = "Готово!";
                    await ShowUiMessageBoxAsync("Успех", "Конфигурация автоустановщика Windows успешно сгенерирована и записана на накопитель!");
                }
                catch (Exception ex)
                {
                    await ShowUiMessageBoxAsync("Ошибка записи", $"Не удалось записать конфигурацию: {ex.Message}");
                }
                finally
                {
                    IsRefreshing = false;
                }
            }
        }

        private async Task InjectOobeAutoAsync(string usbRootPath)
        {
            string ventoyDir = Path.Combine(usbRootPath, "ventoy");
            string jsonPath = Path.Combine(ventoyDir, "ventoy.json");
            if (!Directory.Exists(ventoyDir))
            {
                Directory.CreateDirectory(ventoyDir);
            }
            try
            {
                IsRefreshing = true;
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
                            parent = "/ISO",
                            template = new[]
                            {
                                "/autounattend.xml"
                            }
                        }
                    }
                };

                var jsonOptions = new JsonSerializerOptions { WriteIndented = true };
                string jsonString = JsonSerializer.Serialize(ventoyConfig, jsonOptions);
                await File.WriteAllTextAsync(jsonPath, jsonString, Encoding.UTF8);
                await CreateAutounattend(usbRootPath);
            }
            catch (Exception ex)
            {
                await ShowUiMessageBoxAsync("Ошибка Ventoy", ex.Message);
            }
            finally
            {
                IsRefreshing = false;
            }
        }
    }
}