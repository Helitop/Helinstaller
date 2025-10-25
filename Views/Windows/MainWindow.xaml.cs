using Helinstaller.ViewModels.Windows;
using System.Net.Http;
using System.Security.Authentication;
using System.Security.Policy;
using System.Threading;
using System.Windows.Controls;
using System.Windows.Media.Animation;
using Wpf.Ui;
using Wpf.Ui.Abstractions;
using Wpf.Ui.Appearance;
using Wpf.Ui.Controls;


namespace Helinstaller.Views.Windows
{
    public partial class MainWindow : INavigationWindow
    {
        public MainWindowViewModel ViewModel { get; }
        public MainWindow(
            MainWindowViewModel viewModel,
            INavigationViewPageProvider navigationViewPageProvider,
            INavigationService navigationService
        )
        {
            ViewModel = viewModel;
            DataContext = this;

            SystemThemeWatcher.Watch(this);

            InitializeComponent();
            SetPageService(navigationViewPageProvider);

            navigationService.SetNavigationControl(RootNavigation);

        }

        #region INavigationWindow methods

        public INavigationView GetNavigation() => RootNavigation;

        public bool Navigate(Type pageType) => RootNavigation.Navigate(pageType);

        public void SetPageService(INavigationViewPageProvider navigationViewPageProvider) => RootNavigation.SetPageProviderService(navigationViewPageProvider);

        public void ShowWindow() => Show();

        public void CloseWindow() => Close();


        #endregion INavigationWindow methods

        /// <summary>
        /// Raises the closed event.
        /// </summary>
        protected override void OnClosed(EventArgs e)
        {
            base.OnClosed(e);

            // Make sure that closing this window will begin the process of closing the application.
            Application.Current.Shutdown();
        }

        INavigationView INavigationWindow.GetNavigation()
        {
            throw new NotImplementedException();
        }

        public void SetServiceProvider(IServiceProvider serviceProvider)
        {
            throw new NotImplementedException();
        }

        private async void FluentWindow_Initialized(object sender, EventArgs e)
        {
            await ConnectionCheck();
        }

        private async Task ConnectionCheck()
        {
            while (true) {
            bool success = true;
            await Task.Delay(1000);
                using (var client = new HttpClient())
                {
                    try
                    {
                        // Попытка выполнить HTTPS-запрос
                        HttpResponseMessage response = await client.GetAsync("https://google.com");
                        response.EnsureSuccessStatusCode(); // Выбросит исключение, если статус 4xx или 5xx
                        desc.Text = "Подключено";
                        ring.Visibility = Visibility.Collapsed;
                        stat.Visibility = Visibility.Visible;
                        stat.Symbol = SymbolRegular.CloudCheckmark48;
                    }
                    catch (HttpRequestException ex)
                    {
                        success = false;
                        stat.Symbol = SymbolRegular.CloudError48;
                        // 1. Ловим общее исключение HTTP-запроса
                        desc.Text = ($"Ошибка при выполнении запроса: {ex.Message}");

                        // 2. Проверяем внутреннее исключение (InnerException) для деталей HTTPS/SSL
                        if (ex.InnerException is AuthenticationException authEx)
                        {
                            // Это исключение указывает на ошибку SSL/TLS, например:
                            // - Проблема с сертификатом (просрочен, не доверен)
                            // - Проблема с протоколом TLS
                            desc.Text = ($"\n-> ОШИБКА SSL/TLS (AuthenticationException): {authEx.Message}");

                            // В некоторых случаях AuthenticationException также имеет InnerException
                            if (authEx.InnerException != null)
                            {
                                desc.Text = ($"--> Внутренняя причина: {authEx.InnerException.Message}");
                            }
                        }
                        else
                        {
                            success = false;
                            // Другие ошибки, не связанные напрямую с SSL (например, DNS-ошибка, таймаут)
                            desc.Text = ($"-> Другая сетевая ошибка: {ex.InnerException?.Message ?? "Нет внутренней ошибки"}");
                        }
                    }
                    catch (Exception ex)
                    {
                        success = false;
                        stat.Symbol = SymbolRegular.ErrorCircle48;
                        // Ловим любые другие неожиданные исключения
                        desc.Text = ($"Неожиданная ошибка: {ex.Message}");
                    }
                    await Task.Delay(1000);
                    if ( success )
                        break;
                }
            }
            MainGrid.Visibility = Visibility.Visible;
            DoubleAnimation fadeInAnimation = new DoubleAnimation();
            fadeInAnimation.From = 0.0;
            fadeInAnimation.To = 1.0;
            fadeInAnimation.Duration = new Duration(TimeSpan.FromSeconds(1));
            DoubleAnimation fadeOutAnimation = new DoubleAnimation();
            fadeOutAnimation.From = 1.0;
            fadeOutAnimation.To = 0.0;
            fadeOutAnimation.Duration = new Duration(TimeSpan.FromSeconds(1));
            fadeOutAnimation.Completed += FadeOutAnimation_Completed;
            MainGrid.BeginAnimation(Grid.OpacityProperty, fadeInAnimation);
            LoadingPanel.BeginAnimation(Grid.OpacityProperty, fadeOutAnimation);
        }

            private void FadeOutAnimation_Completed(object sender, EventArgs e)
                {
                    LoadingPanel.Visibility = Visibility.Collapsed;
                    
                }
        }
    }