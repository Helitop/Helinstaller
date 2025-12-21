using Helinstaller.ViewModels.Pages;
using Microsoft.Win32;
using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Reflection.Metadata;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using Windows.ApplicationModel.Store;
using Wpf.Ui.Abstractions.Controls;
using Wpf.Ui.Appearance;
using WpfAnimatedGif;

namespace Helinstaller.Views.Pages;

public class TweakItem : INotifyPropertyChanged
{
    public required string Title { get; set; }
    public required string Description { get; set; }
    public required string Tag { get; set; }
    public bool ShowSwitch { get; set; }

    private bool _isChecked;
    public bool IsChecked
    {
        get => _isChecked;
        set { _isChecked = value; OnPropertyChanged(); }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

public partial class Tweaks : INavigableView<TweaksViewModel>
{
    public TweaksViewModel ViewModel { get; }
    public ObservableCollection<TweakItem> TweakItems { get; set; } = new();

    public Tweaks(TweaksViewModel viewModel)
    {
        ViewModel = viewModel;
        DataContext = this;
        InitializeComponent();
        InitializeTweaks();
        SetRandomGif();
    }

    private void InitializeTweaks()
    {
        int taskbarVal = (int)(Registry.GetValue(@"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced\TaskbarDeveloperSettings", "TaskbarEndTask", 0) ?? 0);
        bool isTaskEndEnabled = taskbarVal == 1;

        var sk = new STICKYKEYS { cbSize = (uint)Marshal.SizeOf(typeof(STICKYKEYS)) };
        SystemParametersInfo(SPI_GETSTICKYKEYS, sk.cbSize, ref sk, 0);
        bool isStickyEnabled = (sk.dwFlags & SKF_STICKYKEYSON) != 0;

        TweakItems.Add(new TweakItem { Title = "Активация Windows/Office", Description = "Активатор через командную строку (IRM).", Tag = "Function1" });
        TweakItems.Add(new TweakItem { Title = "Активация WinRAR", Description = "Убирает назойливое окно 'Купи меня'.", Tag = "WinRARActivation" });
        TweakItems.Add(new TweakItem { Title = "Завершение в панели задач", Description = "Добавляет кнопку 'Завершить задачу' в панели задач.", Tag = "Function2", ShowSwitch = true, IsChecked = isTaskEndEnabled });
        TweakItems.Add(new TweakItem { Title = "Залипание клавиш", Description = "Отключает сообщение при нажатии Shift.", Tag = "Function3", ShowSwitch = true, IsChecked = isStickyEnabled });
        TweakItems.Add(new TweakItem { Title = "Частичный обход блокировок", Description = "Заменяет hosts для доступа к ИИ чат-ботам.", Tag = "Function4" });
        TweakItems.Add(new TweakItem { Title = "Тёмная тема", Description = "Включает тёмную тему Windows для системы и приложений.", Tag = "Function5", ShowSwitch = true, IsChecked = ThemeChanger.IsSystemInDarkMode() });
    }

    private async void TileButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn || btn.Tag is not string tag) return;

        var item = TweakItems.FirstOrDefault(x => x.Tag == tag);

        try
        {
            switch (tag)
            {
                case "Function1":
                    await RunPowershellCommand("irm https://get.activated.win | iex");
                    break;
                case "Function2":
                    if (item != null) ToggleTaskbarEndTask(item);
                    break;
                case "Function3":
                    if (item != null) ToggleStickyKeys(item);
                    break;
                case "WinRARActivation":
                    await ActivateWinRAR();
                    break;
                case "Function4":
                    await ReplaceHostsFileAsync("https://raw.githubusercontent.com/Internet-Helper/GeoHideDNS/refs/heads/main/hosts/hosts");
                    break;
                case "Function5":
                    if (item != null) ThemeChanger.ToggleWindowsTheme(item);
                    break;
            }
        }
        catch (Exception ex)
        {
            CustomMessageBox.Show($"Произошла ошибка: {ex.Message}", "Ошибка", MessageBoxButton.OK);
        }
    }

    // --- Logic Methods ---

    private async Task RunPowershellCommand(string command)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = command,
                Verb = "runas",
                UseShellExecute = true,
                WindowStyle = ProcessWindowStyle.Hidden
            };
            using var process = Process.Start(psi);
            if (process != null) await process.WaitForExitAsync();
        }
        catch (Exception ex)
        {
            CustomMessageBox.Show($"Не удалось запустить PowerShell: {ex.Message}", "Ошибка", MessageBoxButton.OK);
        }
    }

    private void ToggleTaskbarEndTask(TweakItem item)
    {
        const string keyPath = @"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced\TaskbarDeveloperSettings";
        try
        {
            int newVal = item.IsChecked ? 0 : 1;
            Registry.SetValue(keyPath, "TaskbarEndTask", newVal, RegistryValueKind.DWord);
            item.IsChecked = newVal == 1;
        }
        catch (Exception ex)
        {
            CustomMessageBox.Show($"Ошибка реестра: {ex.Message}", "Error", MessageBoxButton.OK);
        }
    }

    private void ToggleStickyKeys(TweakItem item)
    {
        try
        {
            var sk = new STICKYKEYS { cbSize = (uint)Marshal.SizeOf(typeof(STICKYKEYS)) };
            if (!SystemParametersInfo(SPI_GETSTICKYKEYS, sk.cbSize, ref sk, 0)) return;

            if ((sk.dwFlags & SKF_STICKYKEYSON) != 0) sk.dwFlags &= ~SKF_STICKYKEYSON;
            else sk.dwFlags |= SKF_STICKYKEYSON;

            if (SystemParametersInfo(SPI_SETSTICKYKEYS, sk.cbSize, ref sk, 0))
            {
                item.IsChecked = (sk.dwFlags & SKF_STICKYKEYSON) != 0;
            }
        }
        catch (Exception ex)
        {
            CustomMessageBox.Show($"Ошибка StickyKeys: {ex.Message}", "Error", MessageBoxButton.OK);
        }
    }

    private async Task ActivateWinRAR()
    {
        string? winRarPath = GetRegistryPath(@"SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths\WinRAR.exe");

        if (string.IsNullOrEmpty(winRarPath) || !Directory.Exists(winRarPath))
        {
            CustomMessageBox.Show("WinRAR не найден. Установите его перед активацией.", "Ошибка", MessageBoxButton.OK);
            return;
        }

        try
        {
            string keyContent = "RAR registration data\r\nWinRAR\r\nUnlimited Company License\r\nUID=4b914fb772c8376bf571\r\n6412212250f5711ad072cf351cfa39e2851192daf8a362681bbb1d\r\ncd48da1d14d995f0bbf960fce6cb5ffde62890079861be57638717\r\n7131ced835ed65cc743d9777f2ea71a8e32c7e593cf66794343565\r\nb41bcf56929486b8bcdac33d50ecf773996052598f1f556defffbd\r\n982fbe71e93df6b6346c37a3890f3c7edc65d7f5455470d13d1190\r\n6e6fb824bcf25f155547b5fc41901ad58c0992f570be1cf5608ba9\r\naef69d48c864bcd72d15163897773d314187f6a9af350808719796";
            await File.WriteAllTextAsync(Path.Combine(winRarPath, "rarreg.key"), keyContent);
            CustomMessageBox.Show("WinRAR успешно активирован!", "Успех", MessageBoxButton.OK);
        }
        catch (UnauthorizedAccessException)
        {
            CustomMessageBox.Show("Запустите программу от имени Администратора.", "Ошибка доступа", MessageBoxButton.OK);
        }
        catch (Exception ex)
        {
            CustomMessageBox.Show($"Ошибка записи файла: {ex.Message}", "Ошибка", MessageBoxButton.OK);
        }
    }

    private string? GetRegistryPath(string keyPath)
    {
        using (var keyLM = Registry.LocalMachine.OpenSubKey(keyPath))
            if (keyLM?.GetValue(null) is string pathLM) return Path.GetDirectoryName(pathLM);

        using (var keyCU = Registry.CurrentUser.OpenSubKey(keyPath))
            if (keyCU?.GetValue(null) is string pathCU) return Path.GetDirectoryName(pathCU);

        return null;
    }

    private async Task<bool> ReplaceHostsFileAsync(string url)
    {
        if (CustomMessageBox.Show("Заменить файл hosts?", "Внимание", MessageBoxButton.YesNo) != CustomMessageBox.MessageBoxResult.Yes) return false;

        string hostsPath = Path.Combine(Environment.SystemDirectory, @"drivers\etc\hosts");
        try
        {
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
            CustomMessageBox.Show("Hosts успешно обновлен.\nПо пути '%SystemRoot%\\System32\\drivers\\etc' создан бэкап hosts.bak, при необходимости замените обратно.", "Успех", MessageBoxButton.OK);
            return true;
        }
        catch (UnauthorizedAccessException)
        {
            CustomMessageBox.Show("Запустите программу от имени Администратора.", "Ошибка", MessageBoxButton.OK);
            return false;
        }
        catch (Exception ex)
        {
            CustomMessageBox.Show($"Ошибка: {ex.Message}", "Fail", MessageBoxButton.OK);
            return false;
        }
    }
    private void SetRandomGif()
    {
        // 1. Создаем список имен файлов вручную (так как нельзя сканировать ресурсы внутри EXE)
        var gifFiles = new List<string>
    {
        "bocchi.gif",
        "lucy.gif"
        // Добавь сюда все свои файлы из папки Assets
    };

        if (gifFiles.Count > 0)
        {
            // 2. Выбираем случайное имя
            Random rnd = new Random();
            string randomFileName = gifFiles[rnd.Next(gifFiles.Count)];

            // 3. Создаем URI (путь к ресурсу)
            // Убедись, что папка называется Assets (с большой буквы, если так в проекте)
            var uri = new Uri($"pack://application:,,,/Assets/{randomFileName}");

            // 4. Загружаем картинку
            BitmapImage bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.UriSource = uri;
            bitmap.EndInit();

            // 5. Устанавливаем через библиотеку WpfAnimatedGif
            ImageBehavior.SetAnimatedSource(GIF, bitmap);
        }
    }
    // --- Native ---
    private const uint SPI_GETSTICKYKEYS = 0x003A;
    private const uint SPI_SETSTICKYKEYS = 0x003B;
    private const uint SKF_STICKYKEYSON = 0x00000001;

    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    private struct STICKYKEYS
    {
        public uint cbSize;
        public uint dwFlags;
    }

    [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SystemParametersInfo(uint uiAction, uint uiParam, ref STICKYKEYS pvParam, uint fWinIni);
}


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
                object value = key?.GetValue(SystemUsesLightThemeKey);
                return value != null && value is int intValue && intValue == 0;
            }
        }
        catch
        {
            return false; // Если не удалось прочитать, считаем светлой
        }
    }

    public static void ToggleWindowsTheme(TweakItem Item)
    {
        bool isDark = IsSystemInDarkMode();
        int newSystemValue = isDark ? 1 : 0; // Переключаем систему
        int newAppsValue = newSystemValue;   // Переключаем приложения в ту же тему

        try
        {
            using (RegistryKey key = Registry.CurrentUser.CreateSubKey(PersonalizeKey, true))
            {
                key.SetValue(SystemUsesLightThemeKey, newSystemValue, RegistryValueKind.DWord);
                key.SetValue(AppsUseLightThemeKey, newAppsValue, RegistryValueKind.DWord);
            }

            // Сообщаем системе, чтобы изменения вступили в силу
            SendMessageTimeout(new IntPtr(0xFFFF), WM_SETTINGCHANGE, IntPtr.Zero, "ImmersiveColorSet",
                SMTO_ABORTIFHUNG, 100, out _);
            isDark = IsSystemInDarkMode();
            Item.IsChecked = isDark;
            if (!isDark)
            {
                ApplicationThemeManager.Apply(ApplicationTheme.Light);

            }
            if (isDark)
            {
                ApplicationThemeManager.Apply(ApplicationTheme.Dark);

            }
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Ошибка при переключении темы: {ex.Message}");
        }
    }
}
