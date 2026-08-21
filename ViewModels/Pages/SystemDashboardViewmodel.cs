// FILE [CS]: .\ViewModels\Pages\SystemDashboardViewmodel.cs

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Management;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Runtime.InteropServices.ComTypes;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using Helinstaller.Helpers;
using Helinstaller.Models;
using Microsoft.Win32;
using Wpf.Ui.Abstractions.Controls;

namespace Helinstaller.ViewModels.Pages
{
    public partial class SystemDashboardViewModel : ObservableObject, INavigationAware
    {
        private DispatcherTimer? _updateTimer;
        private bool _isInitialized = false;
        private const int HistoryCapacity = 20;

        private readonly Queue<double> _cpuHistory = new();
        private readonly Queue<double> _ramHistory = new();
        private readonly Queue<double> _gpuHistory = new();
        private readonly Queue<double> _netHistory = new();

        [ObservableProperty] private PointCollection _cpuLinePoints = new();
        [ObservableProperty] private PointCollection _cpuPolyPoints = new();
        [ObservableProperty] private PointCollection _ramLinePoints = new();
        [ObservableProperty] private PointCollection _ramPolyPoints = new();
        [ObservableProperty] private PointCollection _gpuLinePoints = new();
        [ObservableProperty] private PointCollection _gpuPolyPoints = new();
        [ObservableProperty] private PointCollection _netLinePoints = new();
        [ObservableProperty] private PointCollection _netPolyPoints = new();

        // Спецификации
        [ObservableProperty] private string _cpuName = "Загрузка...";
        [ObservableProperty] private string _gpuName = "Загрузка...";
        [ObservableProperty] private string _totalRamText = "Загрузка...";
        [ObservableProperty] private string _osVersionText = "Загрузка...";
        [ObservableProperty] private string _uptimeText = "00:00:00";

        // Динамические датчики нагрузки
        [ObservableProperty] private double _cpuUsage = 0;
        [ObservableProperty] private double _gpuUsage = 0;
        [ObservableProperty] private double _ramUsagePercent = 0;
        [ObservableProperty] private string _ramUsageText = "0 GB / 0 GB";
        [ObservableProperty] private string _networkSpeedText = "0 KB/s";

        // ТЕМПЕРАТУРЫ И АППАРАТНЫЕ ДАТЧИКИ
        [ObservableProperty] private string _cpuTempText = "—";
        [ObservableProperty] private string _gpuTempText = "—";

        // АККУМУЛЯТОР (ДЛЯ НОУТБУКОВ)
        [ObservableProperty] private bool _isBatteryPresent = false;
        [ObservableProperty] private int _batteryPercent = 100;
        [ObservableProperty] private string _batteryStatusText = "От сети";
        [ObservableProperty] private string _batteryHealthText = "100%";
        [ObservableProperty] private bool _isBatteryCharging = false;

        private long _prevBytesReceived = 0;
        private long _prevBytesSent = 0;
        private DateTime _prevNetworkTime = DateTime.MinValue;
        private double _maxObservedNetSpeed = 1024 * 1024;

        private static FILETIME _prevIdleTime;
        private static FILETIME _prevKernelTime;
        private static FILETIME _prevUserTime;

        public ObservableCollection<DiskInfoItem> Disks { get; } = new();
        [ObservableProperty] private bool _isLoading = true;

        public SystemDashboardViewModel()
        {
            for (int i = 0; i < HistoryCapacity; i++)
            {
                _cpuHistory.Enqueue(0);
                _ramHistory.Enqueue(0);
                _gpuHistory.Enqueue(0);
                _netHistory.Enqueue(0);
            }
            UpdateAllChartPoints();

            _updateTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(1500)
            };
            _updateTimer.Tick += Timer_Tick;
        }

        public async Task OnNavigatedToAsync()
        {
            if (!_isInitialized)
            {
                IsLoading = true;
                await Task.Run(() =>
                {
                    var cpu = GetCpuNameFromRegistry();
                    var os = GetFormattedWindowsVersion();
                    var gpu = GetGpuNameFromWmi();
                    var memory = GetMemoryStatus();

                    Application.Current.Dispatcher.Invoke(() =>
                    {
                        CpuName = cpu;
                        OsVersionText = os;
                        GpuName = gpu;
                        TotalRamText = $"{memory.TotalGB:F1} GB";
                    });
                });

                _isInitialized = true;
                IsLoading = false;
            }

            _ = RefreshStatsAsync();
            _updateTimer?.Start();
        }

        public Task OnNavigatedFromAsync()
        {
            _updateTimer?.Stop();
            return Task.CompletedTask;
        }

        private async void Timer_Tick(object? sender, EventArgs e)
        {
            await RefreshStatsAsync();
        }

        private async Task RefreshStatsAsync()
        {
            await Task.Run(() =>
            {
                double cpuLoad = Math.Round(GetCpuUsageInternal(), 1);
                double gpuLoad = Math.Round(GetGpuUsageInternal(), 1);

                var memory = GetMemoryStatus();
                double ramPct = Math.Round(memory.Percent, 1);
                string ramText = $"{memory.UsedGB:F1} GB / {memory.TotalGB:F1} GB";

                var (netSpeedStr, rawBytesSpeed) = GetNetworkSpeedDataInternal();
                var uptime = TimeSpan.FromMilliseconds(Environment.TickCount64);
                string uptimeStr = $"{(int)uptime.TotalDays}д {uptime.Hours}ч {uptime.Minutes}м {uptime.Seconds}с";

                // Опрос температур и батареи
                string cpuTemp = GetCpuTemperatureInternal();
                string gpuTemp = GetGpuTemperatureInternal();
                var batteryInfo = GetBatteryStatusInternal();

                // Опрос накопителей со SMART
                var diskDataList = GetDisksInfoWithSmartInternal();

                PushSample(_cpuHistory, cpuLoad);
                PushSample(_ramHistory, ramPct);
                PushSample(_gpuHistory, gpuLoad);

                if (rawBytesSpeed > _maxObservedNetSpeed) _maxObservedNetSpeed = rawBytesSpeed;
                PushSample(_netHistory, rawBytesSpeed);

                var cpuPts = GeneratePoints(_cpuHistory, 100.0);
                var ramPts = GeneratePoints(_ramHistory, 100.0);
                var gpuPts = GeneratePoints(_gpuHistory, 100.0);
                var netPts = GeneratePoints(_netHistory, Math.Max(1024 * 512, _maxObservedNetSpeed));

                Application.Current.Dispatcher.Invoke(() =>
                {
                    CpuUsage = cpuLoad;
                    GpuUsage = gpuLoad;
                    RamUsagePercent = ramPct;
                    RamUsageText = ramText;
                    NetworkSpeedText = netSpeedStr;
                    UptimeText = uptimeStr;

                    CpuTempText = cpuTemp;
                    GpuTempText = gpuTemp;

                    IsBatteryPresent = batteryInfo.IsPresent;
                    BatteryPercent = batteryInfo.Percent;
                    BatteryStatusText = batteryInfo.Status;
                    BatteryHealthText = batteryInfo.Health;
                    IsBatteryCharging = batteryInfo.IsCharging;

                    CpuLinePoints = cpuPts.Line;
                    CpuPolyPoints = cpuPts.Poly;
                    RamLinePoints = ramPts.Line;
                    RamPolyPoints = ramPts.Poly;
                    GpuLinePoints = gpuPts.Line;
                    GpuPolyPoints = gpuPts.Poly;
                    NetLinePoints = netPts.Line;
                    NetPolyPoints = netPts.Poly;

                    SyncDisksCollection(diskDataList);
                });
            });
        }

        private static void PushSample(Queue<double> queue, double value)
        {
            queue.Enqueue(value);
            while (queue.Count > HistoryCapacity)
                queue.Dequeue();
        }

        private (PointCollection Line, PointCollection Poly) GeneratePoints(Queue<double> history, double maxExpected)
        {
            var linePts = new PointCollection(HistoryCapacity);
            var polyPts = new PointCollection(HistoryCapacity + 2);
            polyPts.Add(new Point(0, 36));

            var array = history.ToArray();
            double stepX = 100.0 / Math.Max(1, HistoryCapacity - 1);

            for (int i = 0; i < array.Length; i++)
            {
                double x = i * stepX;
                double normalized = Math.Clamp(array[i] / maxExpected, 0.0, 1.0);
                double y = 36.0 - (normalized * 34.0);
                var pt = new Point(x, y);

                linePts.Add(pt);
                polyPts.Add(pt);
            }

            polyPts.Add(new Point(100, 36));
            linePts.Freeze();
            polyPts.Freeze();
            return (linePts, polyPts);
        }

        private void UpdateAllChartPoints()
        {
            var cpu = GeneratePoints(_cpuHistory, 100.0);
            var ram = GeneratePoints(_ramHistory, 100.0);
            var gpu = GeneratePoints(_gpuHistory, 100.0);
            var net = GeneratePoints(_netHistory, 1024 * 1024);

            CpuLinePoints = cpu.Line; CpuPolyPoints = cpu.Poly;
            RamLinePoints = ram.Line; RamPolyPoints = ram.Poly;
            GpuLinePoints = gpu.Line; GpuPolyPoints = gpu.Poly;
            NetLinePoints = net.Line; NetPolyPoints = net.Poly;
        }

        private void SyncDisksCollection(List<DiskInfoItem> sourceList)
        {
            var currentLetters = sourceList.Select(s => s.DriveLetter).ToList();
            var existingLetters = Disks.Select(d => d.DriveLetter).ToList();

            foreach (var letter in existingLetters.Except(currentLetters).ToList())
            {
                var toRemove = Disks.FirstOrDefault(d => d.DriveLetter == letter);
                if (toRemove != null) Disks.Remove(toRemove);
            }

            foreach (var source in sourceList)
            {
                var item = Disks.FirstOrDefault(d => d.DriveLetter == source.DriveLetter);
                if (item == null)
                {
                    item = new DiskInfoItem { DriveLetter = source.DriveLetter };
                    Disks.Add(item);
                }

                item.Name = source.Name;
                item.VolumeLabel = source.VolumeLabel;
                item.TotalSpaceText = source.TotalSpaceText;
                item.FreeSpaceText = source.FreeSpaceText;
                item.UsedSpaceText = source.UsedSpaceText;
                item.UsedPercent = source.UsedPercent;
                item.HealthStatus = source.HealthStatus;
                item.HealthPercent = source.HealthPercent;
                item.TemperatureText = source.TemperatureText;
                item.MediaType = source.MediaType;
            }
        }

        #region Аппаратный мониторинг (Температуры, Батарея, SMART)

        private string GetCpuTemperatureInternal()
        {
            try
            {
                using var searcher = new ManagementObjectSearcher(@"root\WMI", "SELECT CurrentTemperature FROM MSAcpi_ThermalZoneTemperature");
                foreach (ManagementObject obj in searcher.Get())
                {
                    double tempKelvin = Convert.ToDouble(obj["CurrentTemperature"]);
                    double celsius = (tempKelvin - 2732) / 10.0;
                    if (celsius is > 10 and < 115)
                    {
                        return $"{Math.Round(celsius)}°C";
                    }
                }
            }
            catch { }
            return "—";
        }

        private string GetGpuTemperatureInternal()
        {
            try
            {
                // Попытка опроса через WMI NVIDIA/AMD
                using var searcher = new ManagementObjectSearcher(@"root\CIMV2", "SELECT Temperature FROM Win32_PerfFormattedData_GPUPerformanceCounters_GPUNonLocalAdapter");
                foreach (ManagementObject obj in searcher.Get())
                {
                    var temp = obj["Temperature"];
                    if (temp != null && Convert.ToDouble(temp) > 0)
                        return $"{temp}°C";
                }
            }
            catch { }
            return "—";
        }

        private (bool IsPresent, int Percent, string Status, string Health, bool IsCharging) GetBatteryStatusInternal()
        {
            try
            {
                var sps = new SYSTEM_POWER_STATUS();
                if (GetSystemPowerStatus(out sps))
                {
                    // 128 = нет батареи
                    if (sps.BatteryFlag == 128 || sps.BatteryLifePercent > 100)
                    {
                        return (false, 100, "От сети (ПК)", "100%", false);
                    }

                    int percent = sps.BatteryLifePercent;
                    bool isCharging = (sps.BatteryFlag & 8) != 0;
                    string status = isCharging ? $"Заряжается ({percent}%)" : (sps.ACLineStatus == 1 ? $"От сети ({percent}%)" : $"От батареи ({percent}%)");

                    // Расчет износа через WMI
                    string healthText = "Хорошее";
                    try
                    {
                        using var searcherStatic = new ManagementObjectSearcher(@"root\WMI", "SELECT DesignedCapacity FROM BatteryStaticData");
                        using var searcherFull = new ManagementObjectSearcher(@"root\WMI", "SELECT FullChargedCapacity FROM BatteryFullChargedCapacity");

                        uint design = 0, full = 0;
                        foreach (var o in searcherStatic.Get()) design = Convert.ToUInt32(o["DesignedCapacity"]);
                        foreach (var o in searcherFull.Get()) full = Convert.ToUInt32(o["FullChargedCapacity"]);

                        if (design > 0 && full > 0)
                        {
                            double wearPercent = Math.Clamp(100.0 - ((double)full / design * 100.0), 0, 100);
                            healthText = wearPercent < 5 ? "100% (Идеальное)" : $"{100 - (int)wearPercent}% (Износ {(int)wearPercent}%)";
                        }
                    }
                    catch { }

                    return (true, percent, status, healthText, isCharging);
                }
            }
            catch { }
            return (false, 100, "От сети", "100%", false);
        }

        private List<DiskInfoItem> GetDisksInfoWithSmartInternal()
        {
            var list = new List<DiskInfoItem>();
            try
            {
                var currentDrives = DriveInfo.GetDrives()
                    .Where(d => d.DriveType == DriveType.Fixed && d.IsReady)
                    .ToList();

                // Словарь физических накопителей из WMI для SMART
                var smartDictionary = new Dictionary<string, (string Health, int Temp, string Type)>();
                try
                {
                    using var storageSearcher = new ManagementObjectSearcher(@"root\Microsoft\Windows\Storage", "SELECT DeviceId, Temperature, Wear, HealthStatus FROM MSFT_StorageReliabilityCounter");
                    foreach (ManagementObject obj in storageSearcher.Get())
                    {
                        string id = obj["DeviceId"]?.ToString() ?? "";
                        int temp = obj["Temperature"] != null ? Convert.ToInt32(obj["Temperature"]) : 0;
                        int wear = obj["Wear"] != null ? Convert.ToInt32(obj["Wear"]) : 0;
                        smartDictionary[id] = (wear > 0 ? $"{100 - wear}%" : "100%", temp, "SSD");
                    }
                }
                catch { }

                foreach (var drive in currentDrives)
                {
                    string letter = drive.Name.Replace("\\", "");
                    long total = drive.TotalSize;
                    long free = drive.TotalFreeSpace;
                    long used = total - free;
                    double usedPercent = total > 0 ? Math.Round(used * 100.0 / total, 1) : 0;

                    var item = new DiskInfoItem
                    {
                        DriveLetter = letter,
                        Name = string.IsNullOrWhiteSpace(drive.VolumeLabel) ? "Локальный диск" : drive.VolumeLabel,
                        VolumeLabel = drive.VolumeLabel,
                        TotalSpaceText = FormatBytes(total),
                        FreeSpaceText = FormatBytes(free),
                        UsedSpaceText = FormatBytes(used),
                        UsedPercent = usedPercent,
                        HealthStatus = "OK",
                        HealthPercent = 100,
                        TemperatureText = "34°C",
                        MediaType = "SSD"
                    };

                    if (smartDictionary.Count > 0)
                    {
                        var firstSmart = smartDictionary.Values.FirstOrDefault();
                        if (firstSmart.Temp > 0) item.TemperatureText = $"{firstSmart.Temp}°C";
                    }

                    list.Add(item);
                }
            }
            catch { }
            return list;
        }

        #endregion

        #region Базовые метрики

        private double GetCpuUsageInternal()
        {
            if (!GetSystemTimes(out var idleTime, out var kernelTime, out var userTime)) return 0;

            ulong idle = ((ulong)idleTime.dwHighDateTime << 32) | (uint)idleTime.dwLowDateTime;
            ulong kernel = ((ulong)kernelTime.dwHighDateTime << 32) | (uint)kernelTime.dwLowDateTime;
            ulong user = ((ulong)userTime.dwHighDateTime << 32) | (uint)userTime.dwLowDateTime;

            ulong prevIdle = ((ulong)_prevIdleTime.dwHighDateTime << 32) | (uint)_prevIdleTime.dwLowDateTime;
            ulong prevKernel = ((ulong)_prevKernelTime.dwHighDateTime << 32) | (uint)_prevKernelTime.dwLowDateTime;
            ulong prevUser = ((ulong)_prevUserTime.dwHighDateTime << 32) | (uint)_prevUserTime.dwLowDateTime;

            _prevIdleTime = idleTime;
            _prevKernelTime = kernelTime;
            _prevUserTime = userTime;

            if (prevIdle == 0) return 0;

            ulong diffIdle = idle - prevIdle;
            ulong diffKernel = kernel - prevKernel;
            ulong diffUser = user - prevUser;

            ulong total = diffKernel + diffUser;
            if (total == 0) return 0;

            return (double)(total - diffIdle) * 100.0 / total;
        }

        private List<PerformanceCounter>? _gpuCounters;
        private DateTime _lastGpuScanTime = DateTime.MinValue;

        private double GetGpuUsageInternal()
        {
            try
            {
                if (_gpuCounters == null || (DateTime.Now - _lastGpuScanTime).TotalSeconds > 20)
                {
                    _gpuCounters?.ForEach(c => { try { c.Dispose(); } catch { } });
                    _gpuCounters = new List<PerformanceCounter>();

                    if (PerformanceCounterCategory.Exists("GPU Engine"))
                    {
                        var category = new PerformanceCounterCategory("GPU Engine");
                        var instanceNames = category.GetInstanceNames();

                        foreach (var name in instanceNames)
                        {
                            if (name.EndsWith("engtype_3D", StringComparison.OrdinalIgnoreCase))
                            {
                                try
                                {
                                    var counter = new PerformanceCounter("GPU Engine", "Utilization Percentage", name, true);
                                    counter.NextValue();
                                    _gpuCounters.Add(counter);
                                }
                                catch { }
                            }
                        }
                    }
                    _lastGpuScanTime = DateTime.Now;
                }

                if (_gpuCounters != null && _gpuCounters.Count > 0)
                {
                    float totalUsage = 0;
                    for (int i = _gpuCounters.Count - 1; i >= 0; i--)
                    {
                        try
                        {
                            totalUsage += _gpuCounters[i].NextValue();
                        }
                        catch
                        {
                            try { _gpuCounters[i].Dispose(); } catch { }
                            _gpuCounters.RemoveAt(i);
                        }
                    }

                    return Math.Clamp(totalUsage, 0.0, 100.0);
                }
            }
            catch { }
            return 0;
        }

        private (string Formatted, double RawBytes) GetNetworkSpeedDataInternal()
        {
            try
            {
                var interfaces = NetworkInterface.GetAllNetworkInterfaces()
                    .Where(ni => ni.OperationalStatus == OperationalStatus.Up &&
                                 ni.NetworkInterfaceType != NetworkInterfaceType.Loopback)
                    .ToList();

                long totalReceived = interfaces.Sum(ni => ni.GetIPStatistics().BytesReceived);
                long totalSent = interfaces.Sum(ni => ni.GetIPStatistics().BytesSent);
                var now = DateTime.Now;

                if (_prevNetworkTime == DateTime.MinValue)
                {
                    _prevBytesReceived = totalReceived;
                    _prevBytesSent = totalSent;
                    _prevNetworkTime = now;
                    return ("0 KB/s", 0);
                }

                double seconds = (now - _prevNetworkTime).TotalSeconds;
                if (seconds <= 0) seconds = 1;

                double speedReceived = (totalReceived - _prevBytesReceived) / seconds;
                double speedSent = (totalSent - _prevBytesSent) / seconds;

                _prevBytesReceived = totalReceived;
                _prevBytesSent = totalSent;
                _prevNetworkTime = now;

                double totalSpeed = Math.Max(0, speedReceived + speedSent);
                return (FormatNetworkSpeed(totalSpeed), totalSpeed);
            }
            catch
            {
                return ("0 KB/s", 0);
            }
        }

        private string GetCpuNameFromRegistry()
        {
            try
            {
                object? val = Registry.GetValue(@"HKEY_LOCAL_MACHINE\HARDWARE\DESCRIPTION\System\CentralProcessor\0", "ProcessorNameString", null);
                return val?.ToString()?.Trim() ?? "Центральный процессор";
            }
            catch { return "Центральный процессор"; }
        }

        private string GetFormattedWindowsVersion()
        {
            try
            {
                string rName = Registry.GetValue(@"HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Windows NT\CurrentVersion", "ProductName", "Windows")?.ToString() ?? "Windows";
                string displayVersion = Registry.GetValue(@"HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Windows NT\CurrentVersion", "DisplayVersion", "")?.ToString() ?? "";
                string currentBuild = Registry.GetValue(@"HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Windows NT\CurrentVersion", "CurrentBuild", "")?.ToString() ?? "";

                if (rName.Contains("Microsoft ")) rName = rName.Replace("Microsoft ", "");
                if (rName.Contains("Windows 10") && int.TryParse(currentBuild, out int buildNum) && buildNum >= 22000)
                    rName = rName.Replace("Windows 10", "Windows 11");

                return $"{rName} {displayVersion} (Сборка {currentBuild})".Trim();
            }
            catch
            {
                return Environment.OSVersion.ToString();
            }
        }

        private string GetGpuNameFromWmi()
        {
            try
            {
                using var searcher = new ManagementObjectSearcher("select Name from Win32_VideoController");
                using var collection = searcher.Get();
                foreach (ManagementObject obj in collection)
                {
                    using (obj)
                    {
                        string? name = obj["Name"]?.ToString();
                        if (!string.IsNullOrEmpty(name)) return name;
                    }
                }
            }
            catch { }
            return "Базовый видеоадаптер";
        }

        private string FormatBytes(long bytes)
        {
            string[] units = { "B", "KB", "MB", "GB", "TB" };
            double size = bytes;
            int unitIndex = 0;
            while (size >= 1024 && unitIndex < units.Length - 1)
            {
                size /= 1024;
                unitIndex++;
            }
            return $"{size:F1} {units[unitIndex]}";
        }

        private string FormatNetworkSpeed(double bytesPerSecond)
        {
            if (bytesPerSecond < 1024) return $"{bytesPerSecond:F0} B/s";
            double kb = bytesPerSecond / 1024.0;
            if (kb < 1024) return $"{kb:F1} KB/s";
            double mb = kb / 1024.0;
            return $"{mb:F1} MB/s";
        }

        private MemoryInfo GetMemoryStatus()
        {
            var msex = new MEMORYSTATUSEX();
            if (GlobalMemoryStatusEx(msex))
            {
                double total = msex.ullTotalPhys / 1024.0 / 1024.0 / 1024.0;
                double avail = msex.ullAvailPhys / 1024.0 / 1024.0 / 1024.0;
                return new MemoryInfo
                {
                    TotalGB = total,
                    UsedGB = total - avail,
                    Percent = msex.dwMemoryLoad
                };
            }
            return new MemoryInfo();
        }

        #endregion

        #region P/Invoke Structures

        public struct MemoryInfo
        {
            public double TotalGB;
            public double UsedGB;
            public double Percent;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
        private class MEMORYSTATUSEX
        {
            public uint dwLength;
            public uint dwMemoryLoad;
            public ulong ullTotalPhys;
            public ulong ullAvailPhys;
            public ulong ullTotalPageFile;
            public ulong ullAvailPageFile;
            public ulong ullTotalVirtual;
            public ulong ullAvailVirtual;
            public ulong ullAvailExtendedVirtual;
            public MEMORYSTATUSEX() { dwLength = (uint)Marshal.SizeOf(typeof(MEMORYSTATUSEX)); }
        }

        public struct SYSTEM_POWER_STATUS
        {
            public byte ACLineStatus;
            public byte BatteryFlag;
            public byte BatteryLifePercent;
            public byte SystemStatusFlag;
            public int BatteryLifeTime;
            public int BatteryFullLifeTime;
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool GetSystemPowerStatus(out SYSTEM_POWER_STATUS lpSystemPowerStatus);

        [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GlobalMemoryStatusEx([In, Out] MEMORYSTATUSEX lpBuffer);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetSystemTimes(
            out FILETIME lpIdleTime,
            out FILETIME lpKernelTime,
            out FILETIME lpUserTime);

        #endregion
    }
}