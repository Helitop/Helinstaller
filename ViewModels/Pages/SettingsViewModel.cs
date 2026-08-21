using CommunityToolkit.Mvvm.Messaging;
using Helinstaller.Helpers;
using Helinstaller.Models;
using Wpf.Ui.Abstractions.Controls;
using Wpf.Ui.Appearance;

namespace Helinstaller.ViewModels.Pages
{
    public partial class SettingsViewModel : ObservableObject, INavigationAware
    {
        private bool _isInitialized = false;

        [ObservableProperty]
        private string _appVersion = String.Empty;

        [ObservableProperty]
        private ApplicationTheme _currentTheme = ApplicationTheme.Unknown;

        public Task OnNavigatedToAsync()
        {
            if (!_isInitialized)
                InitializeViewModel();

            return Task.CompletedTask;
        }

        [ObservableProperty]
        private bool _isVisualizerEnabled = Models.AppSettings.IsVisualizerEnabled;

        [ObservableProperty]
        private bool _isMusicAutoPlayEnabled = Models.AppSettings.IsMusicAutoPlayEnabled;

        [ObservableProperty]
        private int _installTimeoutSeconds = Models.AppSettings.InstallTimeoutSeconds;

        // Новые свойства для слайдеров:
        [ObservableProperty]
        private int _proxyPingTimeoutMs = Models.AppSettings.ProxyPingTimeoutMs;

        [ObservableProperty]
        private int _proxyMaxParallelism = Models.AppSettings.ProxyMaxParallelism;

        partial void OnIsVisualizerEnabledChanged(bool value)
        {
            AppSettings.IsVisualizerEnabled = value;
            CommunityToolkit.Mvvm.Messaging.WeakReferenceMessenger.Default.Send(new VisualizerStatusChangedMessage(value));
            AppSettings.Save();
        }

        partial void OnIsMusicAutoPlayEnabledChanged(bool value)
        {
            AppSettings.IsMusicAutoPlayEnabled = value;
            AppSettings.Save();
        }

        partial void OnInstallTimeoutSecondsChanged(int value)
        {
            AppSettings.InstallTimeoutSeconds = value;
            AppSettings.Save();
        }

        partial void OnProxyPingTimeoutMsChanged(int value)
        {
            AppSettings.ProxyPingTimeoutMs = value;
            AppSettings.Save();
        }

        partial void OnProxyMaxParallelismChanged(int value)
        {
            AppSettings.ProxyMaxParallelism = value;
            AppSettings.Save();
        }

        [RelayCommand]
        private void OpenLog()
        {
            Logger.LogInfo("Пользователь запросил просмотр файла логов.");
            Logger.OpenLogFile();
        }

        public Task OnNavigatedFromAsync() => Task.CompletedTask;

        private void InitializeViewModel()
        {
            IsVisualizerEnabled = Models.AppSettings.IsVisualizerEnabled;
            IsMusicAutoPlayEnabled = Models.AppSettings.IsMusicAutoPlayEnabled;
            InstallTimeoutSeconds = Models.AppSettings.InstallTimeoutSeconds;
            ProxyPingTimeoutMs = Models.AppSettings.ProxyPingTimeoutMs;
            ProxyMaxParallelism = Models.AppSettings.ProxyMaxParallelism;

            CurrentTheme = ApplicationThemeManager.GetAppTheme();
            AppVersion = $"Helinstaller - {GetAssemblyVersion()}";
            _isInitialized = true;
        }

        private string GetAssemblyVersion()
        {
            return System.Reflection.Assembly.GetExecutingAssembly().GetName().Version?.ToString()
                ?? String.Empty;
        }

        [RelayCommand]
        private void OnChangeTheme(string parameter)
        {
            switch (parameter)
            {
                case "theme_light":
                    if (CurrentTheme == ApplicationTheme.Light)
                        break;

                    ApplicationThemeManager.Apply(ApplicationTheme.Light);
                    CurrentTheme = ApplicationTheme.Light;
                    break;

                default:
                    if (CurrentTheme == ApplicationTheme.Dark)
                        break;

                    ApplicationThemeManager.Apply(ApplicationTheme.Dark);
                    CurrentTheme = ApplicationTheme.Dark;
                    break;
            }
        }
    }
}