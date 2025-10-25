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

        private async void TileButton_Click(object sender, RoutedEventArgs e)
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
                    ProgressValue = ViewModel.ProgressValue,
                    IsChecking = ViewModel.IsChecking,
                    IsInstalled = ViewModel.IsInstalled
                };
                // 1. Запуск асинхронной команды БЕЗ await.
                // Команда Check() начнет работать в фоновом режиме, но UI-поток
                // сразу перейдет к следующей строке (навигации).
                if (appPageViewModel.CheckCommand is IAsyncRelayCommand asyncCommand)
                {
                    // ВАЖНО: УДАЛИТЕ await. 
                    // Это запустит задачу, но не заблокирует текущий метод.
                    asyncCommand.ExecuteAsync(null);
                }

                // 2. Сразу же выполняем синхронную навигацию.
                // Пользователь мгновенно видит новую страницу.
                bool v = _navigationService.Navigate(typeof(AppPage), appPageViewModel);

            }
            ;
        }
    }
}
