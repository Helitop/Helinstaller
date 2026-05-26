using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Microsoft.Win32;

namespace Helinstaller.Services
{
    public class TweaksService : ITweaksService
    {
        [StructLayout(LayoutKind.Sequential)]
        public struct STICKYKEYS
        {
            public uint cbSize;
            public uint dwFlags;
        }

        private const uint SPI_GETSTICKYKEYS = 0x003A;
        private const uint SPI_SETSTICKYKEYS = 0x003B;
        private const uint SKF_STICKYKEYSON = 0x00000001;

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool SystemParametersInfo(uint uiAction, uint uiParam, ref STICKYKEYS pvParam, uint fWinIni);

        public async Task RunYandexAnnihilatorAsync()
        {
            string script = "Get-AppxPackage -AllUsers *Yandex* | Remove-AppxPackage -AllUsers; " +
                            "Get-AppxProvisionedPackage -Online | Where-Object { $_.DisplayName -like '*Yandex*' } | Remove-AppxProvisionedPackage -Online";

            await Task.Run(() =>
            {
                var ps = new ProcessStartInfo
                {
                    FileName = "powershell.exe",
                    Arguments = $"-NoProfile -ExecutionPolicy Bypass -Command \"{script}\"",
                    CreateNoWindow = true,
                    UseShellExecute = false,
                    Verb = "runas"
                };
                Process.Start(ps)?.WaitForExit();
            });

            string cdmPath = @"Software\Microsoft\Windows\CurrentVersion\ContentDeliveryManager";
            using (var key = Registry.CurrentUser.OpenSubKey(cdmPath, true))
            {
                if (key != null)
                {
                    key.SetValue("SilentInstalledAppsEnabled", 0, RegistryValueKind.DWord);
                    key.SetValue("PreInstalledAppsEnabled", 0, RegistryValueKind.DWord);
                }
            }

            string edgePolicyPath = @"SOFTWARE\Policies\Microsoft\Edge";
            using (var key = Registry.LocalMachine.CreateSubKey(edgePolicyPath))
            {
                key.SetValue("DefaultSearchProviderEnabled", 1, RegistryValueKind.DWord);
                key.SetValue("DefaultSearchProviderSearchURL", "https://www.google.com/search?q={searchTerms}", RegistryValueKind.String);
            }
        }

        public bool ToggleTaskbarEndTask(bool enable)
        {
            const string keyPath = @"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced\TaskbarDeveloperSettings";
            int newVal = enable ? 1 : 0;
            Registry.SetValue(keyPath, "TaskbarEndTask", newVal, RegistryValueKind.DWord);
            return enable;
        }

        public bool IsTaskbarEndTaskEnabled()
        {
            const string keyPath = @"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced\TaskbarDeveloperSettings";
            var val = Registry.GetValue(keyPath, "TaskbarEndTask", 0);
            return val is int i && i == 1;
        }

        public bool ToggleStickyKeys(bool enable)
        {
            var sk = new STICKYKEYS { cbSize = (uint)Marshal.SizeOf(typeof(STICKYKEYS)) };
            if (!SystemParametersInfo(SPI_GETSTICKYKEYS, sk.cbSize, ref sk, 0)) return false;

            if (enable) sk.dwFlags |= SKF_STICKYKEYSON;
            else sk.dwFlags &= ~SKF_STICKYKEYSON;

            SystemParametersInfo(SPI_SETSTICKYKEYS, sk.cbSize, ref sk, 0);
            return enable;
        }

        public bool IsStickyKeysEnabled()
        {
            var sk = new STICKYKEYS { cbSize = (uint)Marshal.SizeOf(typeof(STICKYKEYS)) };
            if (!SystemParametersInfo(SPI_GETSTICKYKEYS, sk.cbSize, ref sk, 0)) return false;
            return (sk.dwFlags & SKF_STICKYKEYSON) != 0;
        }

        public async Task ActivateWinRARAsync()
        {
            string? winRarPath = GetRegistryPath(@"SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths\WinRAR.exe");

            if (string.IsNullOrEmpty(winRarPath) || !Directory.Exists(winRarPath))
            {
                throw new Exception("WinRAR не найден в системе. Сначала установите его.");
            }

            string keyContent = "RAR registration data\r\nWinRAR\r\nUnlimited Company License\r\nUID=4b914fb772c8376bf571\r\n6412212250f5711ad072cf351cfa39e2851192daf8a362681bbb1d\r\ncd48da1d14d995f0bbf960fce6cb5ffde62890079861be57638717\r\n7131ced835ed65cc743d9777f2ea71a8e32c7e593cf66794343565\r\nb41bcf56929486b8bcdac33d50ecf773996052598f1f556defffbd\r\n982fbe71e93df6b6346c37a3890f3c7edc65d7f5455470d13d1190\r\n6e6fb824bcf25f155547b5fc41901ad58c0992f570be1cf5608ba9\r\naef69d48c864bcd72d15163897773d314187f6a9af350808719796";
            await File.WriteAllTextAsync(Path.Combine(winRarPath, "rarreg.key"), keyContent);
        }

        private string? GetRegistryPath(string keyPath)
        {
            using (var keyLM = Registry.LocalMachine.OpenSubKey(keyPath))
                if (keyLM?.GetValue(null) is string pathLM) return Path.GetDirectoryName(pathLM);
            using (var keyCU = Registry.CurrentUser.OpenSubKey(keyPath))
                if (keyCU?.GetValue(null) is string pathCU) return Path.GetDirectoryName(pathCU);
            return null;
        }

        public async Task ReplaceHostsFileAsync(string url)
        {
            string hostsPath = Path.Combine(Environment.SystemDirectory, @"drivers\etc\hosts");
            
            using var client = new HttpClient();
            client.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64)");
            string newContent = await client.GetStringAsync(url);

            if (File.Exists(hostsPath))
            {
                var attributes = File.GetAttributes(hostsPath);
                if ((attributes & FileAttributes.ReadOnly) == FileAttributes.ReadOnly)
                    File.SetAttributes(hostsPath, attributes & ~FileAttributes.ReadOnly);
                File.Copy(hostsPath, hostsPath + ".bak", true);
            }

            await File.WriteAllTextAsync(hostsPath, newContent);
        }
    }
}
