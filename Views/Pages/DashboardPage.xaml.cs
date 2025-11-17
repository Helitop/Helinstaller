using Helinstaller.ViewModels.Pages;
using System.Text.Json;
using System.Windows.Controls;
using Wpf.Ui;
using System.IO;
using Wpf.Ui.Abstractions.Controls;
using Wpf.Ui.Controls;

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

            LoadAppsAndGenerateButtons();
        }

        private void LoadAppsAndGenerateButtons()
        {
            try
            {
                string jsonPath = "apps.json"; // путь к твоему JSON-файлу
                string json = File.ReadAllText(jsonPath);

                var apps = JsonSerializer.Deserialize<List<AppInfo>>(json);

                if (apps == null) return;

                foreach (var app in apps)
                {
                    AppsWrapPanel.Children.Add(CreateAppTile(app));
                }
            }
            catch (Exception ex)
            {
                CustomMessageBox.Show($"Ошибка загрузки приложений: {ex.Message}");
            }
        }

        private Wpf.Ui.Controls.Button CreateAppTile(AppInfo app)
        {
            var button = new Wpf.Ui.Controls.Button
            {
                Width = 140,
                Height = 140,
                Margin = new Thickness(5),
                Appearance = Wpf.Ui.Controls.ControlAppearance.Secondary,
                Tag = app.Name
            };

            button.Click += TileButton_Click;

            var grid = new Grid();
            grid.RowDefinitions.Add(new RowDefinition());
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            // Иконка
            if (!string.IsNullOrEmpty(app.IconPath))
            {
                var icon = new Wpf.Ui.Controls.Image
                {
                    Source = new System.Windows.Media.Imaging.BitmapImage(new Uri(app.IconPath, UriKind.Relative)),
                    VerticalAlignment = VerticalAlignment.Center,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    MaxWidth = 80,
                    MaxHeight = 80
                };

                Grid.SetRow(icon, 0);
                grid.Children.Add(icon);
            }
            else
            {
                var icon = new Wpf.Ui.Controls.SymbolIcon
                {
                    Symbol = SymbolRegular.Folder48,
                    FontSize = 80,
                    VerticalAlignment = VerticalAlignment.Center,
                    HorizontalAlignment = HorizontalAlignment.Center
                };

                Grid.SetRow(icon, 0);
                grid.Children.Add(icon);
            }

            // Текст
            var text = new Wpf.Ui.Controls.TextBlock
            {
                Text = app.Title,
                FontSize = 16,
                FontWeight = FontWeights.SemiBold,
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 5, 0, 5)
            };

            Grid.SetRow(text, 1);
            grid.Children.Add(text);

            button.Content = grid;

            return button;
        }

        private async void TileButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Wpf.Ui.Controls.Button button && button.Tag is string tag)
            {
                ViewModel.OnNavigateToApp(tag);

                var vm = new AppPageViewmodel
                {
                    Title = ViewModel.AppTitle,
                    Description = ViewModel.AppDescription,
                    IconPath = ViewModel.AppIconPath,
                    PreviewPath = ViewModel.AppPreviewPath,
                    IsInstalling = ViewModel.IsInstalling,
                    ProgressValue = ViewModel.ProgressValue,
                    IsChecking = ViewModel.IsChecking,
                    IsInstalled = ViewModel.IsInstalled,
                    DownloadUrl = ViewModel.DownloadUrl
                };

                if (vm.CheckCommand is IAsyncRelayCommand asyncCommand)
                    asyncCommand.ExecuteAsync(null);

                _navigationService.Navigate(typeof(AppPage), vm);
            }
        }
    }
}
