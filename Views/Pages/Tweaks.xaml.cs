using Helinstaller.Helpers;
using Helinstaller.Models;
using Helinstaller.ViewModels.Pages;
using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media.Imaging;
using Windows.Security.Credentials.UI;
using Wpf.Ui.Abstractions.Controls;
using Wpf.Ui.Controls;
using WpfAnimatedGif;

namespace Helinstaller.Views.Pages;

public partial class Tweaks : INavigableView<TweaksViewModel>
{
    public TweaksViewModel ViewModel { get; }
    public ObservableCollection<TweakItem> TweakItems { get; set; } = new();
    private bool _isInitialized = false;

    public Tweaks(TweaksViewModel viewModel)
    {
        ViewModel = viewModel;
        DataContext = this;
        InitializeComponent();

        BuildTweakCards();
    }

    private void Page_Loaded(object sender, RoutedEventArgs e)
    {
        if (!_isInitialized)
        {
            _isInitialized = true;

            // Загружаем GIF с небольшой задержкой, чтобы не мешать открытию страницы
            Task.Delay(150).ContinueWith(_ => Dispatcher.InvokeAsync(SetRandomGif));

            // Фоновый опрос тумблеров
            _ = RefreshTweakStatesAsync();
        }
    }

    private void BuildTweakCards()
    {
        TweakItems.Add(new TweakItem { Title = "Активация Windows/Office", Description = "Цифровая лицензия через MAS. Безопасно и навсегда.", Tag = "Function1", Icon = SymbolRegular.WindowShield24 });
        TweakItems.Add(new TweakItem { Title = "Активация WinRAR", Description = "Убирает назойливое окно 'Купи меня' навсегда.", Tag = "WinRARActivation", Icon = SymbolRegular.Archive24 });
        TweakItems.Add(new TweakItem
        {
            Title = "Контроль учетных записей (UAC)",
            Description = "Всплывающие уведомления при запуске программ от администратора. Изменения требуют перезагрузки.",
            Tag = "TweakUAC",
            ShowSwitch = true,
            IsChecked = false,
            Icon = SymbolRegular.Shield24
        });
        TweakItems.Add(new TweakItem
        {
            Title = "Брандмауэр Windows (Firewall)",
            Description = "Встроенный межсетевой экран для фильтрации сетевой активности приложений.",
            Tag = "TweakFirewall",
            ShowSwitch = true,
            IsChecked = false,
            Icon = SymbolRegular.ShieldGlobe24
        });
        TweakItems.Add(new TweakItem
        {
            Title = "Вкладки Edge в Alt+Tab",
            Description = "Отключает показ отдельных вкладок браузера при переключении окон через Alt+Tab (остаются только сами окна).",
            Tag = "TweakAltTab",
            ShowSwitch = true,
            IsChecked = false,
            Icon = SymbolRegular.Tab24
        });
        TweakItems.Add(new TweakItem
        {
            Title = "Завершение в панели задач",
            Description = "Кнопка 'Завершить задачу' при нажатии ПКМ по иконке в панели.",
            Tag = "Function2",
            ShowSwitch = true,
            IsChecked = false,
            Icon = SymbolRegular.Desktop24
        });
        TweakItems.Add(new TweakItem
        {
            Title = "Дезинфекция (Anti-Yandex)",
            Description = "Вырезает Яндекс.Музыку из Store и блокирует навязывание поиска в Edge/Windows.",
            Tag = "AntiYandex",
            Icon = SymbolRegular.Delete24
        });
        TweakItems.Add(new TweakItem
        {
            Title = "Обход блокировок ИИ (Xbox DNS)",
            Description = "Доступ к ChatGPT, Claude, Gemini и Xbox Live без VPN через быстрый Smart DNS.",
            Tag = "Function4",
            ShowSwitch = true,
            IsChecked = false,
            Icon = SymbolRegular.ShieldGlobe24
        });
        TweakItems.Add(new TweakItem
        {
            Title = "Тёмная тема",
            Description = "Принудительный переход системы и приложений на тёмную сторону.",
            Tag = "Function5",
            ShowSwitch = true,
            IsChecked = false,
            Icon = SymbolRegular.WeatherMoon24
        });
        TweakItems.Add(new TweakItem
        {
            Title = "Залипание клавиш",
            Description = "Отключает писк и окно при многократном нажатии Shift.",
            Tag = "Function3",
            ShowSwitch = true,
            IsChecked = false,
            Icon = SymbolRegular.Keyboard24
        });
    }

    private async Task RefreshTweakStatesAsync()
    {
        await Task.Run(() =>
        {
            // 1. Alt+Tab
            int altTabVal = (int)(Registry.GetValue(@"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced", "MultiTaskingAltTabFilter", 0) ?? 0);
            bool isAltTabTabsDisabled = altTabVal == 3;

            // 2. UAC
            int uacVal = (int)(Registry.GetValue(@"HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\System", "EnableLUA", 1) ?? 1);
            bool isUacEnabled = uacVal == 1;

            // 3. Брандмауэр Windows
            bool isFirewallEnabled = false;
            try
            {
                int fwStandard = (int)(Registry.GetValue(@"HKEY_LOCAL_MACHINE\SYSTEM\CurrentControlSet\Services\SharedAccess\Parameters\FirewallPolicy\StandardProfile", "EnableFirewall", 1) ?? 1);
                int fwPublic = (int)(Registry.GetValue(@"HKEY_LOCAL_MACHINE\SYSTEM\CurrentControlSet\Services\SharedAccess\Parameters\FirewallPolicy\PublicProfile", "EnableFirewall", 1) ?? 1);
                isFirewallEnabled = fwStandard == 1 || fwPublic == 1;
            }
            catch { }

            // 4. Кнопка завершения в панели задач
            int taskbarVal = (int)(Registry.GetValue(@"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced\TaskbarDeveloperSettings", "TaskbarEndTask", 0) ?? 0);
            bool isTaskEndEnabled = taskbarVal == 1;

            // 5. Залипание клавиш
            var sk = new STICKYKEYS { cbSize = (uint)Marshal.SizeOf(typeof(STICKYKEYS)) };
            SystemParametersInfo(SPI_GETSTICKYKEYS, sk.cbSize, ref sk, 0);
            bool isStickyEnabled = (sk.dwFlags & SKF_STICKYKEYSON) != 0;

            // 6. Xbox DNS
            bool isXboxDnsEnabled = false;
            try
            {
                var interfaces = System.Net.NetworkInformation.NetworkInterface.GetAllNetworkInterfaces();
                foreach (var ni in interfaces)
                {
                    if (ni.OperationalStatus == System.Net.NetworkInformation.OperationalStatus.Up &&
                        (ni.NetworkInterfaceType == System.Net.NetworkInformation.NetworkInterfaceType.Ethernet ||
                         ni.NetworkInterfaceType == System.Net.NetworkInformation.NetworkInterfaceType.Wireless80211))
                    {
                        var ipProps = ni.GetIPProperties();
                        if (ipProps.DnsAddresses.Any(dns => dns.ToString() == "111.88.96.50"))
                        {
                            isXboxDnsEnabled = true;
                            break;
                        }
                    }
                }
            }
            catch { }

            // 7. Тема Windows
            bool isDarkTheme = ThemeChanger.IsSystemInDarkMode();

            // Переводим тумблеры в актуальное положение
            Dispatcher.Invoke(() =>
            {
                SetTweakChecked("TweakAltTab", isAltTabTabsDisabled);
                SetTweakChecked("TweakUAC", isUacEnabled);
                SetTweakChecked("TweakFirewall", isFirewallEnabled);
                SetTweakChecked("Function2", isTaskEndEnabled);
                SetTweakChecked("Function3", isStickyEnabled);
                SetTweakChecked("Function4", isXboxDnsEnabled);
                SetTweakChecked("Function5", isDarkTheme);
            });
        });
    }

    private void SetTweakChecked(string tag, bool isChecked)
    {
        var item = TweakItems.FirstOrDefault(x => x.Tag == tag);
        if (item != null) item.IsChecked = isChecked;
    }

    private bool _isBusy = false;

    private async void TileButton_Click(object sender, RoutedEventArgs e)
    {
        if (_isBusy) return;
        if (sender is not System.Windows.Controls.Button btn || btn.Tag is not string tag) return;

        var item = TweakItems.FirstOrDefault(x => x.Tag == tag);
        if (item == null) return;

        _isBusy = true;
        bool previousState = item.IsChecked;

        try
        {
            if (tag is "TweakUAC" or "TweakFirewall" or "Function4" or "AntiYandex")
            {
                bool verified = await VerifyUserIdentityAsync("Подтвердите изменение параметров системы");
                if (!verified)
                {
                    item.IsChecked = previousState;
                    CapsuleToastService.Show("Действие отменено пользователем", ToastType.Warning);
                    return;
                }
            }

            switch (tag)
            {
                case "TweakAltTab":
                    await ToggleAltTabEdgeTabs(item);
                    break;

                case "Function1":
                    var activationDlg = new ActivationDialog { Owner = Window.GetWindow(this) };
                    activationDlg.ShowDialog();
                    break;

                case "Function2":
                    await ToggleTaskbarEndTask(item);
                    break;

                case "Function3":
                    await ToggleStickyKeys(item);
                    break;

                case "WinRARActivation":
                    await ActivateWinRAR();
                    break;

                case "TweakUAC":
                    await ToggleUAC(item);
                    break;

                case "TweakFirewall":
                    await ToggleFirewall(item);
                    break;

                case "Function4":
                    await ToggleXboxDnsAsync(item, !previousState);
                    break;

                case "Function5":
                    await ThemeChanger.ToggleWindowsTheme(item);
                    string themeText = ThemeChanger.IsSystemInDarkMode() ? "Включена тёмная тема" : "Включена светлая тема";
                    CapsuleToastService.Show(themeText, ToastType.Info);
                    break;

                case "AntiYandex":
                    await RunYandexAnnihilator();
                    break;
            }
        }
        catch (Exception ex)
        {
            item.IsChecked = previousState;
            CapsuleToastService.Show($"Ошибка: {ex.Message}", ToastType.Error);
        }
        finally
        {
            _isBusy = false;
        }
    }

    private void ShowHelpButton_Click(object sender, RoutedEventArgs e)
    {
        Helinstaller.Views.Windows.HelpDialog.ShowHelp(Window.GetWindow(this), "tweaks");
    }

    private async Task ToggleAltTabEdgeTabs(TweakItem item)
    {
        const string keyPath = @"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced";
        try
        {
            int currentVal = (int)(Registry.GetValue(keyPath, "MultiTaskingAltTabFilter", 0) ?? 0);
            int newVal = currentVal == 3 ? 0 : 3;

            Registry.SetValue(keyPath, "MultiTaskingAltTabFilter", newVal, RegistryValueKind.DWord);
            item.IsChecked = newVal == 3;

            if (item.IsChecked)
                CapsuleToastService.Show("Вкладки Edge скрыты из Alt+Tab (только окна)", ToastType.Success);
            else
                CapsuleToastService.Show("Показ вкладок Edge в Alt+Tab включен", ToastType.Info);
        }
        catch (Exception ex)
        {
            CapsuleToastService.Show($"Ошибка реестра: {ex.Message}", ToastType.Error);
        }
    }

    private async Task ToggleUAC(TweakItem item)
    {
        const string keyPath = @"HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\System";
        try
        {
            int currentVal = (int)(Registry.GetValue(keyPath, "EnableLUA", 1) ?? 1);
            int newVal = currentVal == 1 ? 0 : 1;

            Registry.SetValue(keyPath, "EnableLUA", newVal, RegistryValueKind.DWord);
            item.IsChecked = newVal == 1;

            if (item.IsChecked)
                CapsuleToastService.Show("UAC включен. Перезагрузите ПК для применения", ToastType.Info);
            else
                CapsuleToastService.Show("UAC отключен. Перезагрузите ПК для применения", ToastType.Warning);
        }
        catch (Exception ex)
        {
            CapsuleToastService.Show($"Ошибка реестра: {ex.Message}", ToastType.Error);
        }
    }

    private async Task ToggleFirewall(TweakItem item)
    {
        try
        {
            bool targetState = !item.IsChecked;
            string stateArg = targetState ? "on" : "off";

            await Task.Run(() =>
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "netsh.exe",
                    Arguments = $"advfirewall set allprofiles state {stateArg}",
                    CreateNoWindow = true,
                    UseShellExecute = false
                };
                using var proc = Process.Start(psi);
                proc?.WaitForExit();
            });

            item.IsChecked = targetState;

            if (targetState)
                CapsuleToastService.Show("Брандмауэр Windows включен", ToastType.Success);
            else
                CapsuleToastService.Show("Брандмауэр Windows отключен", ToastType.Warning);
        }
        catch (Exception ex)
        {
            CapsuleToastService.Show($"Ошибка брандмауэра: {ex.Message}", ToastType.Error);
        }
    }

    private async Task ToggleTaskbarEndTask(TweakItem item)
    {
        const string keyPath = @"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced\TaskbarDeveloperSettings";
        try
        {
            int newVal = item.IsChecked ? 0 : 1;
            Registry.SetValue(keyPath, "TaskbarEndTask", newVal, RegistryValueKind.DWord);
            item.IsChecked = newVal == 1;

            if (item.IsChecked)
                CapsuleToastService.Show("Кнопка «Завершить задачу» добавлена в панель", ToastType.Success);
            else
                CapsuleToastService.Show("Кнопка «Завершить задачу» отключена", ToastType.Info);
        }
        catch (Exception ex)
        {
            CapsuleToastService.Show($"Ошибка реестра: {ex.Message}", ToastType.Error);
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

            if (SystemParametersInfo(SPI_SETSTICKYKEYS, sk.cbSize, ref sk, SPIF_UPDATEINIFILE | SPIF_SENDCHANGE))
            {
                item.IsChecked = (sk.dwFlags & SKF_STICKYKEYSON) != 0;

                if (!item.IsChecked)
                    CapsuleToastService.Show("Залипание клавиш Shift отключено", ToastType.Success);
                else
                    CapsuleToastService.Show("Залипание клавиш включено", ToastType.Info);
            }
        }
        catch (Exception ex)
        {
            CapsuleToastService.Show($"Ошибка системы: {ex.Message}", ToastType.Error);
        }
    }

    private async Task ActivateWinRAR()
    {
        string? winRarPath = GetRegistryPath(@"SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths\WinRAR.exe");

        if (string.IsNullOrEmpty(winRarPath) || !Directory.Exists(winRarPath))
        {
            CapsuleToastService.Show("WinRAR не найден в системе. Сначала установите его", ToastType.Warning);
            return;
        }

        try
        {
            string keyContent = "RAR registration data\r\nWinRAR\r\nUnlimited Company License\r\nUID=4b914fb772c8376bf571\r\n6412212250f5711ad072cf351cfa39e2851192daf8a362681bbb1d\r\ncd48da1d14d995f0bbf960fce6cb5ffde62890079861be57638717\r\n7131ced835ed65cc743d9777f2ea71a8e32c7e593cf66794343565\r\nb41bcf56929486b8bcdac33d50ecf773996052598f1f556defffbd\r\n982fbe71e93df6b6346c37a3890f3c7edc65d7f5455470d13d1190\r\n6e6fb824bcf25f155547b5fc41901ad58c0992f570be1cf5608ba9\r\naef69d48c864bcd72d15163897773d314187f6a9af350808719796";
            await File.WriteAllTextAsync(Path.Combine(winRarPath, "rarreg.key"), keyContent);

            CapsuleToastService.Show("WinRAR успешно активирован! Окно покупки убрано", ToastType.Success);
        }
        catch (UnauthorizedAccessException)
        {
            CapsuleToastService.Show("Доступ запрещен. Запустите программу от администратора", ToastType.Error);
        }
        catch (Exception ex)
        {
            CapsuleToastService.Show($"Ошибка активации: {ex.Message}", ToastType.Error);
        }
    }

    private async Task ToggleXboxDnsAsync(TweakItem item, bool targetState)
    {
        string primaryDns = "111.88.96.50";
        string secondaryDns = "111.88.96.51";

        try
        {
            string adapterName = GetActiveNetworkAdapterName();
            if (string.IsNullOrEmpty(adapterName))
            {
                CapsuleToastService.Show("Активный сетевой адаптер (Ethernet/Wi-Fi) не найден", ToastType.Error);
                return;
            }

            bool success;
            if (targetState)
            {
                success = await SetDnsViaPowerShellAsync(adapterName, primaryDns, secondaryDns);
                if (!success) throw new Exception("Не удалось применить DNS-серверы.");

                item.IsChecked = true;
                CapsuleToastService.Show($"Xbox Smart DNS включен ({adapterName})", ToastType.Success);
            }
            else
            {
                success = await ResetDnsViaPowerShellAsync(adapterName);
                if (!success) throw new Exception("Не удалось сбросить DNS.");

                item.IsChecked = false;
                CapsuleToastService.Show($"DNS возвращен в режим DHCP ({adapterName})", ToastType.Info);
            }

            FlushDnsCache();
        }
        catch (Exception ex)
        {
            CapsuleToastService.Show($"Ошибка сети: {ex.Message}", ToastType.Error);
        }
    }

    private async Task RunYandexAnnihilator()
    {
        var msg = new Wpf.Ui.Controls.MessageBox
        {
            Title = "Дезинфекция Яндекса",
            Content = "1. Будет удалена предустановленная региональная Яндекс.Музыка (UWP).\n" +
                      "2. Регион рекомендаций Windows и Microsoft Store переключится на США (GeoID 244 / US).\n" +
                      "3. Отключится авто-установка тиктоков и промо-приложений в меню «Пуск».\n\n" +
                      "Продолжить?",
            PrimaryButtonText = "Очистить",
            CloseButtonText = "Отмена"
        };

        if (await msg.ShowDialogAsync() != Wpf.Ui.Controls.MessageBoxResult.Primary) return;

        try
        {
            string script = "Get-AppxPackage -Name '*Yandex.Music*' -AllUsers | Remove-AppxPackage -AllUsers -ErrorAction SilentlyContinue; " +
                            "Get-AppxProvisionedPackage -Online | Where-Object { $_.DisplayName -like '*Yandex.Music*' } | Remove-AppxProvisionedPackage -Online -ErrorAction SilentlyContinue; " +
                            "Set-WinHomeLocation -GeoId 244";

            await Task.Run(() => {
                var ps = new ProcessStartInfo
                {
                    FileName = "powershell.exe",
                    Arguments = $"-NoProfile -NonInteractive -ExecutionPolicy Bypass -Command \"{script}\"",
                    CreateNoWindow = true,
                    UseShellExecute = false,
                    Verb = "runas",
                    WindowStyle = ProcessWindowStyle.Hidden
                };
                using var proc = Process.Start(ps);
                proc?.WaitForExit();
            });

            using (var geoUser = RegistryKey.OpenBaseKey(RegistryHive.CurrentUser, RegistryView.Registry64)
                                           .CreateSubKey(@"Control Panel\International\Geo"))
            {
                geoUser?.SetValue("Nation", "244", RegistryValueKind.String);
                geoUser?.SetValue("Name", "US", RegistryValueKind.String);
            }

            using (var cdmKey = RegistryKey.OpenBaseKey(RegistryHive.CurrentUser, RegistryView.Registry64)
                                           .CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\ContentDeliveryManager"))
            {
                cdmKey?.SetValue("SilentInstalledAppsEnabled", 0, RegistryValueKind.DWord);
                cdmKey?.SetValue("PreInstalledAppsEnabled", 0, RegistryValueKind.DWord);
                cdmKey?.SetValue("OemPreInstalledAppsEnabled", 0, RegistryValueKind.DWord);
                cdmKey?.SetValue("RegionalContentReportingEnabled", 0, RegistryValueKind.DWord);
                cdmKey?.SetValue("SubscribedContent-314559Enabled", 0, RegistryValueKind.DWord);
                cdmKey?.SetValue("SubscribedContent-338388Enabled", 0, RegistryValueKind.DWord);
                cdmKey?.SetValue("SystemPaneSuggestionsEnabled", 0, RegistryValueKind.DWord);
            }

            string[] policyPathsToDelete = {
                @"SOFTWARE\Policies\Microsoft\Edge",
                @"SOFTWARE\Policies\Google\Chrome",
                @"SOFTWARE\Policies\Chromium",
                @"SOFTWARE\Policies\BraveSoftware\Brave"
            };
            foreach (var path in policyPathsToDelete)
            {
                try { RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64).DeleteSubKeyTree(path, false); } catch { }
                try { RegistryKey.OpenBaseKey(RegistryHive.CurrentUser, RegistryView.Registry64).DeleteSubKeyTree(path, false); } catch { }
            }

            CapsuleToastService.Show("Дезинфекция Яндекса успешно завершена!", ToastType.Success);
        }
        catch (Exception ex)
        {
            CapsuleToastService.Show($"Ошибка очистки: {ex.Message}", ToastType.Error);
        }
    }

    private async Task<bool> VerifyUserIdentityAsync(string reason)
    {
        try
        {
            var availability = await UserConsentVerifier.CheckAvailabilityAsync();
            if (availability == UserConsentVerifierAvailability.NotConfiguredForUser ||
                availability == UserConsentVerifierAvailability.DeviceNotPresent)
            {
                return true;
            }

            if (availability != UserConsentVerifierAvailability.Available)
            {
                return false;
            }

            var result = await UserConsentVerifier.RequestVerificationAsync(reason);
            return result == UserConsentVerificationResult.Verified;
        }
        catch
        {
            return false;
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

    private string GetActiveNetworkAdapterName()
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "netsh",
                Arguments = "interface show interface",
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                StandardOutputEncoding = System.Text.Encoding.GetEncoding(866)
            };

            using var process = Process.Start(psi);
            string output = process.StandardOutput.ReadToEnd();
            process.WaitForExit();

            foreach (string line in output.Split('\n'))
            {
                if (line.Contains("Подключено") || line.Contains("Connected"))
                {
                    var parts = line.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length > 3)
                    {
                        return string.Join(" ", parts.Skip(3)).Trim();
                    }
                }
            }
        }
        catch { }

        return "Ethernet";
    }

    private async Task<bool> SetDnsViaPowerShellAsync(string adapterName, string primary, string secondary)
    {
        return await Task.Run(() =>
        {
            try
            {
                string script = $"Set-DnsClientServerAddress -InterfaceAlias \"{adapterName}\" -ServerAddresses ('{primary}','{secondary}')";

                var psi = new ProcessStartInfo
                {
                    FileName = "powershell.exe",
                    Arguments = $"-NoProfile -NonInteractive -ExecutionPolicy Bypass -Command \"{script}\"",
                    CreateNoWindow = true,
                    UseShellExecute = false,
                    WindowStyle = ProcessWindowStyle.Hidden
                };

                using var proc = Process.Start(psi);
                proc?.WaitForExit();
                return proc?.ExitCode == 0;
            }
            catch
            {
                return false;
            }
        });
    }

    private async Task<bool> ResetDnsViaPowerShellAsync(string adapterName)
    {
        return await Task.Run(() =>
        {
            try
            {
                string script = $"Set-DnsClientServerAddress -InterfaceAlias \"{adapterName}\" -ResetServerAddresses";

                var psi = new ProcessStartInfo
                {
                    FileName = "powershell.exe",
                    Arguments = $"-NoProfile -NonInteractive -ExecutionPolicy Bypass -Command \"{script}\"",
                    CreateNoWindow = true,
                    UseShellExecute = false,
                    WindowStyle = ProcessWindowStyle.Hidden
                };

                using var proc = Process.Start(psi);
                proc?.WaitForExit();
                return proc?.ExitCode == 0;
            }
            catch
            {
                return false;
            }
        });
    }

    private void FlushDnsCache()
    {
        try { DnsFlushResolverCache(); } catch { }
    }

    [DllImport("dnsapi.dll", SetLastError = true)]
    private static extern bool DnsFlushResolverCache();

    private void SetRandomGif()
    {
        try
        {
            var gifFiles = new List<string> { "bocchi.gif", "lucy.gif", "larp.gif" };
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
        catch { }
    }

    private const uint SPI_GETSTICKYKEYS = 0x003A;
    private const uint SPI_SETSTICKYKEYS = 0x003B;
    private const uint SKF_STICKYKEYSON = 0x00000001;
    private const uint SPIF_UPDATEINIFILE = 0x0001;
    private const uint SPIF_SENDCHANGE = 0x0002;

    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    private struct STICKYKEYS { public uint cbSize; public uint dwFlags; }

    [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SystemParametersInfo(uint uiAction, uint uiParam, ref STICKYKEYS pvParam, uint fWinIni);
}