using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Helinstaller.Models;

namespace Helinstaller.Services
{
    public class VentoyService : IVentoyService
    {
        public bool IsVentoyInstalled(UsbDriveItem drive)
        {
            try
            {
                if (drive == null) return false;
                if (drive.DisplayName.ToLower().Contains("ventoy") && !drive.DisplayName.ToLower().Contains("efi"))
                    return true;
                return false;
            }
            catch
            {
                return false;
            }
        }

        public async Task<bool> InstallOrUpdateVentoyAsync(UsbDriveItem drive, bool install)
        {
            return await Task.Run(() =>
            {
                try
                {
                    if (drive == null) return false;

                    string ventoyPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Ventoy", "Ventoy2Disk.exe");
                    if (!File.Exists(ventoyPath)) return false;

                    string driveParam = $"/Drive:{drive.DriveLetter}";
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

        public async Task<bool> CopyAutounattendAsync(string driveRootPath)
        {
            return await Task.Run(() =>
            {
                try
                {
                    string sourcePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Assets", "autounattend.xml");
                    string destPath = Path.Combine(driveRootPath, "autounattend.xml");

                    if (!File.Exists(sourcePath)) return false;

                    File.Copy(sourcePath, destPath, true);
                    return true;
                }
                catch
                {
                    return false;
                }
            });
        }

        public async Task<bool> InjectOobeAutoAsync(string usbRootPath)
        {
            return await Task.Run(() =>
            {
                try
                {
                    string ventoyDir = Path.Combine(usbRootPath, "ventoy");
                    string jsonPath = Path.Combine(ventoyDir, "ventoy.json");
                    if (!Directory.Exists(ventoyDir))
                    {
                        Directory.CreateDirectory(ventoyDir);
                    }

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
                                template = new[] { "/autounattend.xml" }
                            }
                        }
                    };

                    var jsonOptions = new JsonSerializerOptions { WriteIndented = true };
                    string jsonString = JsonSerializer.Serialize(ventoyConfig, jsonOptions);
                    File.WriteAllText(jsonPath, jsonString, Encoding.UTF8);

                    return CopyAutounattendAsync(usbRootPath).Result;
                }
                catch
                {
                    return false;
                }
            });
        }
    }
}