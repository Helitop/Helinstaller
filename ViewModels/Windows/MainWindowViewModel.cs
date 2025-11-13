using Helinstaller.Views.Pages;
using System.Collections.ObjectModel;
using Wpf.Ui;
using Wpf.Ui.Controls;

namespace Helinstaller.ViewModels.Windows
{
    public partial class MainWindowViewModel : ObservableObject
    {
        private readonly INavigationService _navigationService;

        public MainWindowViewModel(INavigationService navigationService)
        {
            _navigationService = navigationService;
        }

        [ObservableProperty]
        private string _applicationTitle = "Helinstaller";

        [ObservableProperty]
        private ObservableCollection<object> _menuItems = new()
        {
            new NavigationViewItem()
            {
                Content = "Приложения",
                Icon = new SymbolIcon { Symbol = SymbolRegular.Home24 },
                TargetPageType = typeof(Views.Pages.DashboardPage),
            },
            new NavigationViewItem()
            {
                Content = "Твики",
                Icon = new SymbolIcon { Symbol = SymbolRegular.EditSettings24 },
                TargetPageType = typeof(Views.Pages.Tweaks)
            },
            new NavigationViewItem()
            {
                Content = "Установить приложение?",
                Visibility = Visibility.Collapsed,
                TargetPageType = typeof(Views.Pages.AppPage)
            },
            new NavigationViewItem()
            {
                Content = "Советы",
                Icon = new SymbolIcon { Symbol = SymbolRegular.Question16 },
                TargetPageType = typeof(Views.Pages.Advices)

            },
            new NavigationViewItem()
            {
                Content = "Установка Windows",
                Icon = new SymbolIcon { Symbol = SymbolRegular.ArrowDownload48 },
                TargetPageType = typeof(Views.Pages.Ventoy)

            },
            new NavigationViewItem()
            {
                Content = "Пожертвование",
                Visibility = Visibility.Collapsed,
                TargetPageType = typeof(Views.Pages.Donate)
            }
        };

        [ObservableProperty]
        private ObservableCollection<object> _footerMenuItems = new()
        {
            new NavigationViewItem()
            {
                Content = "Параметры",
                Icon = new SymbolIcon { Symbol = SymbolRegular.Settings24 },
                TargetPageType = typeof(Views.Pages.SettingsPage)
            }
        };

        [ObservableProperty]
        private ObservableCollection<MenuItem> _trayMenuItems = new()
        {
            new MenuItem { Header = "Приложения", Tag = "tray_home" }
        };

        [RelayCommand]
        public void ToDonation()
        {
            _navigationService.Navigate(typeof(Donate));
        }
    }
}