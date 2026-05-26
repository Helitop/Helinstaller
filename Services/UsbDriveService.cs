using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Management;
using System.Threading.Tasks;
using Helinstaller.Models;

namespace Helinstaller.Services
{
    public class UsbDriveService : IUsbDriveService
    {
        public async Task<List<UsbDriveItem>> GetUsbDrivesAsync()
        {
            var result = new List<UsbDriveItem>();

            await Task.Run(() =>
            {
                var drives = DriveInfo.GetDrives()
                    .Where(d => d.DriveType == DriveType.Removable && d.IsReady)
                    .ToArray();

                foreach (var d in drives)
                {
                    try
                    {
                        var item = new UsbDriveItem
                        {
                            DriveLetter = d.RootDirectory.FullName.Replace("\\", ""),
                            VolumeLabel = SafeGet(() => d.VolumeLabel, string.Empty),
                            DisplayName = $"{d.Name} {(string.IsNullOrWhiteSpace(d.VolumeLabel) ? "" : d.VolumeLabel)}",
                            TotalBytes = SafeGet(() => d.TotalSize, 0L),
                            FreeBytes = SafeGet(() => d.TotalFreeSpace, 0L)
                        };
                        item.UsedPercent = item.TotalBytes > 0
                            ? Math.Round((item.TotalBytes - item.FreeBytes) * 100.0 / item.TotalBytes, 1)
                            : 0.0;
                        result.Add(item);
                    }
                    catch { continue; }
                }
            });

            return result;
        }

        public async Task<bool> FormatDriveAsync(UsbDriveItem drive)
        {
            return await Task.Run(() =>
            {
                try
                {
                    var driveInfo = drive.ToDriveInfo();
                    if (driveInfo == null) return false;

                    string? diskNumber = GetDiskNumber(driveInfo.RootDirectory.FullName);
                    if (diskNumber == null) return false;

                    string scriptPath = Path.Combine(Path.GetTempPath(), "diskpart_script.txt");
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
                    if (proc == null) return false;

                    proc.WaitForExit();
                    return proc.ExitCode == 0;
                }
                catch
                {
                    return false;
                }
            });
        }

        public string? GetDiskNumber(string driveLetter)
        {
            try
            {
                var query = $"ASSOCIATORS OF {{Win32_LogicalDisk.DeviceID='{driveLetter.TrimEnd('\\')}'}} WHERE AssocClass=Win32_LogicalDiskToPartition";
                var searcher = new ManagementObjectSearcher(query);
                foreach (ManagementObject partition in searcher.Get())
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
            catch { }
            return null;
        }

        private static T SafeGet<T>(Func<T> getter, T fallback)
        {
            try { return getter(); }
            catch { return fallback; }
        }
    }
}
