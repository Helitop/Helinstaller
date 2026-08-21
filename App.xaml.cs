using Helinstaller.Services;
using Helinstaller.ViewModels.Pages;
using Helinstaller.ViewModels.Windows;
using Helinstaller.Views.Pages;
using Helinstaller.Views.Windows;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using System.IO;
using System.Windows.Threading;
using Wpf.Ui;
using Wpf.Ui.DependencyInjection;
using System.Diagnostics;
using System.Security.Principal;
using System.Text;
using Velopack;
using Helinstaller.Helpers;

namespace Helinstaller
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App
    {
        public App()
        {
            // Velopack должен быть запущен как можно раньше. 
            // Конструктор App — идеальное место для этого в стандартном WPF.
            VelopackApp.Build().Run();
        }

        private static readonly IHost _host = Host
            .CreateDefaultBuilder()
            .ConfigureAppConfiguration(c => { c.SetBasePath(Path.GetDirectoryName(AppContext.BaseDirectory)); })
            .ConfigureServices((context, services) =>
            {
                services.AddNavigationViewPageProvider();

                services.AddHostedService<ApplicationHostService>();

                // Theme manipulation
                services.AddSingleton<IThemeService, ThemeService>();

                // TaskBar manipulation
                services.AddSingleton<ITaskBarService, TaskBarService>();

                // Service containing navigation, same as INavigationWindow... but without window
                services.AddSingleton<INavigationService, NavigationService>();

                // Main window with navigation
                services.AddSingleton<INavigationWindow, MainWindow>();
                services.AddSingleton<MainWindowViewModel>();

                services.AddSingleton<DashboardPage>();
                services.AddSingleton<DashboardViewModel>();
                services.AddSingleton<SettingsPage>();
                services.AddSingleton<SettingsViewModel>();
                services.AddSingleton<Tweaks>();
                services.AddSingleton<TweaksViewModel>();
                services.AddSingleton<SystemDashboardPage>();
                services.AddSingleton<SystemDashboardViewModel>();
                services.AddSingleton<Donate>();
                services.AddSingleton<DonateViewmodel>();
                services.AddSingleton<Advices>();

                services.AddSingleton<Ventoy>();

                services.AddSingleton<DownloadsPage>();
                services.AddSingleton<DownloadsViewModel>();
                services.AddSingleton<ProxyPage>();
                services.AddSingleton<ProxyViewModel>();
                // Custom Business Services
                services.AddSingleton<IWingetService, WingetService>();
                services.AddSingleton<IDownloadService, DownloadService>();
                services.AddSingleton<IUsbDriveService, UsbDriveService>();
                services.AddSingleton<IVentoyService, VentoyService>();

            }).Build();

        /// <summary>
        /// Gets services.
        /// </summary>
        public static IServiceProvider Services
        {
            get { return _host.Services; }
        }

        /// <summary>
        /// Occurs when the application is loading.
        /// </summary>
        /// 


        private async void OnStartup(object sender, StartupEventArgs e)
        {
            Logger.LogInfo("=== Запуск Helinstaller ===");
            Logger.LogInfo($"ОС: {Environment.OSVersion}, .NET: {Environment.Version}");

            if (!IsRunAsAdmin())
            {
                Logger.LogWarning("Запуск без прав администратора. Инициализация перезапуска с повышенными привилегиями...");
                var processInfo = new ProcessStartInfo
                {
                    FileName = Environment.ProcessPath,
                    UseShellExecute = true,
                    Verb = "runas"
                };

                try
                {
                    Process.Start(processInfo);
                }
                catch (Exception ex)
                {
                    Logger.LogError("Пользователь отказался от повышения прав UAC", ex);
                    MessageBox.Show("Для работы твиков и Ventoy необходимы права администратора.",
                                    "Доступ ограничен", MessageBoxButton.OK, MessageBoxImage.Warning);
                }

                Application.Current.Shutdown();
                return;
            }

            Logger.LogInfo("Права администратора успешно подтверждены.");
            System.Text.Encoding.RegisterProvider(System.Text.CodePagesEncodingProvider.Instance);
            await _host.StartAsync();
        }

        // Метод проверки прав администратора
        private static bool IsRunAsAdmin()
        {
            using var identity = WindowsIdentity.GetCurrent();
            var principal = new WindowsPrincipal(identity);
            return principal.IsInRole(WindowsBuiltInRole.Administrator);
        }
        /// <summary>
        /// Occurs when the application is closing.
        /// </summary>
        private async void OnExit(object sender, ExitEventArgs e)
        {
            Logger.LogInfo("=== Завершение работы Helinstaller ===");
            await _host.StopAsync();

            _host.Dispose();
        }

        /// <summary>
        /// Occurs when an exception is thrown by an application but not handled.
        /// </summary>
        private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
        {
            Logger.LogError("Критический сбой приложения (Unhandled Exception)", e.Exception);
            MessageBox.Show($"Произошла критическая ошибка. Лог сохранен в файл log.txt рядом с программой.\n\nДетали: {e.Exception.Message}",
                            "Критическая ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
            e.Handled = true;
        }
    }
}
