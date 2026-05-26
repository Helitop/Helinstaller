using System;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Microsoft.Win32;
using Wpf.Ui.Appearance;
using Helinstaller.Models;

namespace Helinstaller.Helpers
{
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
                    object? value = key?.GetValue(SystemUsesLightThemeKey);
                    return value is int intValue && intValue == 0;
                }
            }
            catch { return false; }
        }

        public static async Task ToggleWindowsTheme(TweakItem? item = null)
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
                ApplicationThemeManager.Apply(isDark ? ApplicationTheme.Dark : ApplicationTheme.Light);

                if (item != null)
                {
                    item.IsChecked = isDark;
                }
            }
            catch (Exception ex)
            {
                var msg = new Wpf.Ui.Controls.MessageBox { Title = "Ошибка темы", Content = ex.Message, CloseButtonText = "ОК" };
                await msg.ShowDialogAsync();
            }
        }
    }
}