using Helinstaller.ViewModels.Pages;
using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using Wpf.Ui.Abstractions.Controls;
using Wpf.Ui.Appearance;
using Wpf.Ui.Controls;
using WpfAnimatedGif;

namespace Helinstaller.Views.Pages;

public class TweakItem : INotifyPropertyChanged
{
    public required string Title { get; set; }
    public required string Description { get; set; }
    public required string Tag { get; set; }
    public bool ShowSwitch { get; set; }
    public Wpf.Ui.Controls.SymbolRegular Icon { get; set; }

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

        TweakItems.Add(new TweakItem { Title = "Активация Windows/Office", Description = "Цифровая лицензия через MAS. Безопасно и навсегда.", Tag = "Function1", Icon = SymbolRegular.WindowShield24 });
        TweakItems.Add(new TweakItem { Title = "Активация WinRAR", Description = "Убирает назойливое окно 'Купи меня' навсегда.", Tag = "WinRARActivation", Icon = SymbolRegular.Archive24 });
        TweakItems.Add(new TweakItem { Title = "Завершение в панели задач", Description = "Кнопка 'Завершить задачу' при нажатии ПКМ по иконке в панели.", Tag = "Function2", ShowSwitch = true, IsChecked = isTaskEndEnabled, Icon = SymbolRegular.Desktop24 });
        TweakItems.Add(new TweakItem { Title = "Залипание клавиш", Description = "Отключает писк и окно при многократном нажатии Shift.", Tag = "Function3", ShowSwitch = true, IsChecked = isStickyEnabled, Icon = SymbolRegular.Keyboard24 });
        TweakItems.Add(new TweakItem { Title = "Обход блокировок ИИ", Description = "Доступ к ChatGPT, Claude и Gemini без VPN через системный hosts.", Tag = "Function4", Icon = SymbolRegular.ShieldGlobe24 });
        TweakItems.Add(new TweakItem { Title = "Тёмная тема", Description = "Принудительный переход системы и приложений на тёмную сторону.", Tag = "Function5", ShowSwitch = true, IsChecked = ThemeChanger.IsSystemInDarkMode(), Icon = SymbolRegular.WeatherMoon24 });
    }

    private async void TileButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not System.Windows.Controls.Button btn || btn.Tag is not string tag) return;
        var item = TweakItems.FirstOrDefault(x => x.Tag == tag);

        try
        {
            switch (tag)
            {
                case "Function1":
                    var activationDlg = new ActivationDialog();
                    activationDlg.Owner = Window.GetWindow(this);
                    activationDlg.ShowDialog();
                    break;
                case "Function2":
                    if (item != null) await ToggleTaskbarEndTask(item);
                    break;
                case "Function3":
                    if (item != null) await ToggleStickyKeys(item);
                    break;
                case "WinRARActivation":
                    await ActivateWinRAR();
                    break;
                case "Function4":
                    await ReplaceHostsFileAsync("https://raw.githubusercontent.com/Internet-Helper/GeoHideDNS/refs/heads/main/hosts/hosts");
                    break;
                case "Function5":
                    if (item != null) await ThemeChanger.ToggleWindowsTheme(item);
                    break;
            }
        }
        catch (Exception ex)
        {
            await ShowUiMessageBox("Ошибка", ex.Message);
        }
    }

    // Вспомогательный метод для красивых асинхронных уведомлений
    private async Task ShowUiMessageBox(string title, string content)
    {
        var msg = new Wpf.Ui.Controls.MessageBox
        {
            Title = title,
            Content = content,
            CloseButtonText = "ОК"
        };
        await msg.ShowDialogAsync();
    }

    private async Task ToggleTaskbarEndTask(TweakItem item)
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
            await ShowUiMessageBox("Ошибка реестра", ex.Message);
        }
    }

    private async Task ToggleStickyKeys(TweakItem item)
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
            await ShowUiMessageBox("Ошибка системы", ex.Message);
        }
    }

    private async Task ActivateWinRAR()
    {
        string? winRarPath = GetRegistryPath(@"SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths\WinRAR.exe");

        if (string.IsNullOrEmpty(winRarPath) || !Directory.Exists(winRarPath))
        {
            await ShowUiMessageBox("Ошибка", "WinRAR не найден в системе. Сначала установите его.");
            return;
        }

        try
        {
            string keyContent = "RAR registration data\r\nWinRAR\r\nUnlimited Company License\r\nUID=4b914fb772c8376bf571\r\n6412212250f5711ad072cf351cfa39e2851192daf8a362681bbb1d\r\ncd48da1d14d995f0bbf960fce6cb5ffde62890079861be57638717\r\n7131ced835ed65cc743d9777f2ea71a8e32c7e593cf66794343565\r\nb41bcf56929486b8bcdac33d50ecf773996052598f1f556defffbd\r\n982fbe71e93df6b6346c37a3890f3c7edc65d7f5455470d13d1190\r\n6e6fb824bcf25f155547b5fc41901ad58c0992f570be1cf5608ba9\r\naef69d48c864bcd72d15163897773d314187f6a9af350808719796";
            await File.WriteAllTextAsync(Path.Combine(winRarPath, "rarreg.key"), keyContent);
            await ShowUiMessageBox("Успех", "WinRAR успешно активирован! Теперь окно о покупке не будет вас беспокоить.");
        }
        catch (UnauthorizedAccessException)
        {
            await ShowUiMessageBox("Доступ запрещен", "Недостаточно прав. Запустите Helinstaller от имени администратора.");
        }
        catch (Exception ex)
        {
            await ShowUiMessageBox("Ошибка", ex.Message);
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
        var msg = new Wpf.Ui.Controls.MessageBox();
        msg.Title = "Настройка обхода блокировок";
        msg.Content = "Эта функция пропишет в системный файл 'hosts' адреса для прямого доступа к OpenAI (ChatGPT), Claude и Gemini и не только.\n\n" +
                      "• Это работает без VPN и не влияет на общую скорость интернета.\n" +
                      "• Будет создана резервная копия старого файла.\n" +
                      "• Это не поможет при блокировке сервиса страной/провайдером.\n\n" +
                      "Применить изменения?";
        msg.IsPrimaryButtonEnabled = true;
        msg.PrimaryButtonText = "Применить";
        msg.CloseButtonText = "Отмена";

        var result = await msg.ShowDialogAsync();
        if (result != Wpf.Ui.Controls.MessageBoxResult.Primary) return false;

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
            await ShowUiMessageBox("Успех", "Файл hosts обновлен! Бэкап создан рядом (hosts.bak).\n\nТеперь сайты ИИ должны открываться напрямую. Если нет — очистите кэш браузера.");
            return true;
        }
        catch (UnauthorizedAccessException)
        {
            await ShowUiMessageBox("Ошибка доступа", "Не удалось отредактировать файл. Запустите программу от имени администратора.");
            return false;
        }
        catch (Exception ex)
        {
            await ShowUiMessageBox("Ошибка загрузки", "Не удалось скачать данные с сервера. Проверьте интернет.\n" + ex.Message);
            return false;
        }
    }

    private void SetRandomGif()
    {
        var gifFiles = new List<string> { "bocchi.gif", "lucy.gif" };
        if (gifFiles.Count > 0)
        {
            Random rnd = new Random();
            string randomFileName = gifFiles[rnd.Next(gifFiles.Count)];
            var uri = new Uri($"pack://application:,,,/Assets/{randomFileName}");
            BitmapImage bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.UriSource = uri;
            bitmap.EndInit();
            ImageBehavior.SetAnimatedSource(GIF, bitmap);
        }
    }

    private const uint SPI_GETSTICKYKEYS = 0x003A;
    private const uint SPI_SETSTICKYKEYS = 0x003B;
    private const uint SKF_STICKYKEYSON = 0x00000001;

    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    private struct STICKYKEYS { public uint cbSize; public uint dwFlags; }

    [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SystemParametersInfo(uint uiAction, uint uiParam, ref STICKYKEYS pvParam, uint fWinIni);
}

public static class ThemeChanger
{
    private const string PersonalizeKey = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";

    public static bool IsSystemInDarkMode()
    {
        try
        {
            using (RegistryKey key = Registry.CurrentUser.OpenSubKey(PersonalizeKey))
            {
                object value = key?.GetValue("SystemUsesLightTheme");
                return value != null && (int)value == 0;
            }
        }
        catch { return false; }
    }

    public static async Task ToggleWindowsTheme(TweakItem Item)
    {
        bool isDark = IsSystemInDarkMode();
        int newVal = isDark ? 1 : 0;

        try
        {
            using (RegistryKey key = Registry.CurrentUser.CreateSubKey(PersonalizeKey, true))
            {
                key.SetValue("SystemUsesLightTheme", newVal, RegistryValueKind.DWord);
                key.SetValue("AppsUseLightTheme", newVal, RegistryValueKind.DWord);
            }

            SendMessageTimeout(new IntPtr(0xFFFF), 0x001A, IntPtr.Zero, "ImmersiveColorSet", 0x0002, 100, out _);

            isDark = IsSystemInDarkMode();
            Item.IsChecked = isDark;
            ApplicationThemeManager.Apply(isDark ? ApplicationTheme.Dark : ApplicationTheme.Light);
        }
        catch (Exception ex)
        {
            var msg = new Wpf.Ui.Controls.MessageBox { Title = "Ошибка темы", Content = ex.Message, CloseButtonText = "ОК" };
            await msg.ShowDialogAsync();
        }
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern int SendMessageTimeout(IntPtr hWnd, uint Msg, IntPtr wParam, string lParam, uint fuFlags, uint uTimeout, out IntPtr lpdwResult);
}