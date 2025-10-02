using Helinstaller.ViewModels.Pages;
using System.Windows.Controls;
using Wpf.Ui;
using Wpf.Ui.Abstractions.Controls;

namespace Helinstaller.Views.Pages
{
    public partial class DashboardPage : INavigableView<DashboardViewModel>
    {
        public DashboardViewModel ViewModel { get; }
        private readonly INavigationService _navigationService;

        public DashboardPage(DashboardViewModel viewModel, INavigationService navigationService)
        {
            ViewModel = viewModel;
            _navigationService = navigationService;
            DataContext = this;
            InitializeComponent();
        }

        private void TileButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button button && button.Tag is string tag)
            {
                ViewModel.OnNavigateToApp(tag);
                var appPageViewModel = new AppPageViewmodel
                {
                    Title = ViewModel.AppTitle,
                    Description = ViewModel.AppDescription,
                    IconPath = ViewModel.AppIconPath,
                    PreviewPath = ViewModel.AppPreviewPath,
                    IsInstalling = ViewModel.IsInstalling,
                    ProgressValue = ViewModel.ProgressValue
                };

                bool v = _navigationService.Navigate(typeof(AppPage), appPageViewModel);
            }
            ;

        }
    }
}
