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
using System.Text;
using Velopack;

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

                services.AddSingleton<Donate>();
                services.AddSingleton<DonateViewmodel>();
                services.AddSingleton<Advices>();

                services.AddSingleton<Ventoy>();

                services.AddSingleton<DownloadsPage>();
                services.AddSingleton<DownloadsViewModel>();

                // Custom Business Services
                services.AddSingleton<IWingetService, WingetService>();
                services.AddSingleton<IDownloadService, DownloadService>();
                services.AddSingleton<IUsbDriveService, UsbDriveService>();
                services.AddSingleton<IVentoyService, VentoyService>();
                services.AddSingleton<ITweaksService, TweaksService>();
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
            System.Text.Encoding.RegisterProvider(System.Text.CodePagesEncodingProvider.Instance);
            await _host.StartAsync();
        }

        /// <summary>
        /// Occurs when the application is closing.
        /// </summary>
        private async void OnExit(object sender, ExitEventArgs e)
        {
            await _host.StopAsync();

            _host.Dispose();
        }

        /// <summary>
        /// Occurs when an exception is thrown by an application but not handled.
        /// </summary>
        private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
        {
            // For more info see https://docs.microsoft.com/en-us/dotnet/api/system.windows.application.dispatcherunhandledexception?view=windowsdesktop-6.0
        }
    }
}
