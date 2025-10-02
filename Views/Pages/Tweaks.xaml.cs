using Helinstaller.ViewModels.Pages;
using Microsoft.Win32;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Controls;
using Wpf.Ui.Abstractions.Controls;

namespace Helinstaller.Views.Pages
{
    public partial class Tweaks : INavigableView<TweaksViewModel>
    {
        public TweaksViewModel ViewModel { get; }

        public const uint SPI_GETSTICKYKEYS = 0x003A;
        public const uint SPI_SETSTICKYKEYS = 0x003B;

        [StructLayout(LayoutKind.Sequential, Pack = 4)]
        public struct STICKYKEYS
        {
            public uint cbSize;
            public uint dwFlags;
        }

        public const uint SKF_STICKYKEYSON = 0x00000001;
        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        public static extern bool SystemParametersInfo(uint uiAction, uint uiParam, ref STICKYKEYS pvParam, uint fWinIni);

        public Tweaks(TweaksViewModel viewModel)
        {
            ViewModel = viewModel;
            DataContext = this;

            InitializeComponent();
            const string keyPath = @"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced\TaskbarDeveloperSettings";
            const string valueName = "TaskbarEndTask";
            int currentValue = (int)Registry.GetValue(keyPath, valueName, 0);
            EndTaskSwitch.IsChecked = Convert.ToBoolean(currentValue);

            STICKYKEYS sk = new STICKYKEYS();
            sk.cbSize = (uint)Marshal.SizeOf(sk);
            SystemParametersInfo(SPI_GETSTICKYKEYS, sk.cbSize, ref sk, 0);

            if ((sk.dwFlags & SKF_STICKYKEYSON) != 0)
            {
                StickySwitch.IsChecked = true;
            }
            else
            {
                StickySwitch.IsChecked = false;
            }
        }



        private void TileButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button button)
            {
                // Получаем значение из свойства Tag
                string tag = button.Tag?.ToString();

                switch (tag)
                {
                    case "Function1":
                        // Логика для первого функционала
                        _ = Function1Action(); // Используем '_' для игнорирования предупреждения о не-awaited задаче
                        break;
                    case "Function2":
                        // Логика для первого функционала
                        _ = Function2Action(); // Используем '_' для игнорирования предупреждения о не-awaited задаче
                        break;
                    case "Function3":
                        _ = Function3Action();
                        break;
                    case "WinRARActivation":
                        _ = FunctionWinRARActivation();
                        break;
                    default:
                        CustomMessageBox.Show($"Нажата кнопка с тегом: {tag}");
                        break;
                }
            }
        }

        private async Task Function1Action()
        {

            try
            {
                var processInfo = new System.Diagnostics.ProcessStartInfo
                {
                    Verb = "runas",
                    LoadUserProfile = true,
                    FileName = "powershell.exe",
                    Arguments = "irm https://get.activated.win | iex",
                    RedirectStandardOutput = false,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                var p = System.Diagnostics.Process.Start(processInfo);
            }
            finally
            {

            }
        }
        private async Task Function2Action()
        {
            const string keyPath = @"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced\TaskbarDeveloperSettings";
            const string valueName = "TaskbarEndTask";

            try
            {
                int currentValue = (int)Registry.GetValue(keyPath, valueName, 0);
                int newValue = currentValue == 1 ? 0 : 1;
                Registry.SetValue(keyPath, valueName, newValue, RegistryValueKind.DWord);
                EndTaskSwitch.IsChecked = Convert.ToBoolean(newValue);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Ошибка при изменении реестра: {ex.Message}");
            }
        }
        private async Task Function3Action()
        {
            try
            {
                STICKYKEYS sk = new STICKYKEYS();
                sk.cbSize = (uint)Marshal.SizeOf(sk);

                // Получаем текущее состояние залипания клавиш
                SystemParametersInfo(SPI_GETSTICKYKEYS, sk.cbSize, ref sk, 0);

                // Переключаем флаг
                if ((sk.dwFlags & SKF_STICKYKEYSON) != 0)
                {
                    sk.dwFlags &= ~SKF_STICKYKEYSON; // Выключаем
                    StickySwitch.IsChecked = false;
                }
                else
                {
                    sk.dwFlags |= SKF_STICKYKEYSON; // Включаем
                    StickySwitch.IsChecked = true;
                }

                // Устанавливаем новое состояние
                SystemParametersInfo(SPI_SETSTICKYKEYS, sk.cbSize, ref sk, 0);
            }
            finally
            {
            }
        }
        private async Task FunctionWinRARActivation()
        {
            string winRarPath = GetWinRarInstallPath();
            string fileName = "rarreg.key";
            if (string.IsNullOrEmpty(winRarPath))
            {
                Console.WriteLine("Не удалось найти папку установки WinRAR.");
                return;
            }

            string destinationPath = Path.Combine(winRarPath, fileName);

            try
            {
                // Записываем весь текст в файл.
                File.WriteAllText(destinationPath, "RAR registration data\r\nWinRAR\r\nUnlimited Company License\r\nUID=4b914fb772c8376bf571\r\n6412212250f5711ad072cf351cfa39e2851192daf8a362681bbb1d\r\ncd48da1d14d995f0bbf960fce6cb5ffde62890079861be57638717\r\n7131ced835ed65cc743d9777f2ea71a8e32c7e593cf66794343565\r\nb41bcf56929486b8bcdac33d50ecf773996052598f1f556defffbd\r\n982fbe71e93df6b6346c37a3890f3c7edc65d7f5455470d13d1190\r\n6e6fb824bcf25f155547b5fc41901ad58c0992f570be1cf5608ba9\r\naef69d48c864bcd72d15163897773d314187f6a9af350808719796");
                CustomMessageBox MB = new CustomMessageBox($"Файл '{fileName}' успешно создан по пути: {destinationPath}", "Установка WinRAR", System.Windows.MessageBoxButton.OK);
                MB.ShowDialog();
            }
            catch (Exception ex)
            {
                CustomMessageBox MB = new CustomMessageBox($"Произошла ошибка при создании файла: {ex.Message}", "Установка WinRAR", System.Windows.MessageBoxButton.OK);
            }
        }
        private static string GetWinRarInstallPath()
        {
            try
            {
                // Путь к ключу App Paths для WinRAR.exe
                string appPathsKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths\WinRAR.exe";

                using (RegistryKey key = Registry.LocalMachine.OpenSubKey(appPathsKey))
                {
                    if (key != null)
                    {
                        // Значение по умолчанию содержит полный путь к исполняемому файлу
                        string exePath = (string)key.GetValue(null);
                        if (!string.IsNullOrEmpty(exePath))
                        {
                            // Возвращаем директорию, в которой находится WinRAR.exe
                            return Path.GetDirectoryName(exePath);
                        }
                    }
                }

                // Если не найден в LocalMachine, можно попробовать CurrentUser
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(appPathsKey))
                {
                    if (key != null)
                    {
                        string exePath = (string)key.GetValue(null);
                        if (!string.IsNullOrEmpty(exePath))
                        {
                            return Path.GetDirectoryName(exePath);
                        }
                    }
                }
            }
            catch (System.Security.SecurityException)
            {
            }
            catch (Exception ex)
            {
                CustomMessageBox MB = new CustomMessageBox($"Ошибка при доступе к реестру: {ex.Message}", "Ошибка установки Zapret", System.Windows.MessageBoxButton.OK);
                MB.ShowDialog();
            }

            return null;
        }
    }
}
