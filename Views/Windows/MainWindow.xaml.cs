using Helinstaller.ViewModels.Windows;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Security.Authentication;
using System.Security.Policy;
using System.Threading;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using Windows.System.UserProfile;
using Wpf.Ui;
using Wpf.Ui.Abstractions;
using Wpf.Ui.Appearance;
using Wpf.Ui.Controls;



namespace Helinstaller.Views.Windows
{
    public partial class MainWindow : INavigationWindow
    {
        public MainWindowViewModel ViewModel { get; }
        private Storyboard _glowStoryboard; // Переменная для хранения Storyboard
        private bool _hasBeenClicked = false; // Флаг, чтобы свечение не появлялось повторно
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

        private bool _socialExpanded = false;

        private void SocialToggleButton_Click(object sender, RoutedEventArgs e)
        {
            SocialToggleButton.Appearance = ControlAppearance.Secondary;

            double targetWidth = _socialExpanded ? 0 : 600;
            double targetOpacity = _socialExpanded ? 0 : 1;

            var widthAnim = new DoubleAnimation
            {
                To = targetWidth,
                Duration = TimeSpan.FromMilliseconds(400),
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
            };

            var opacityAnim = new DoubleAnimation
            {
                To = targetOpacity,
                Duration = TimeSpan.FromMilliseconds(350)
            };

            SocialPanelContainer.BeginAnimation(FrameworkElement.WidthProperty, widthAnim);
            SocialPanel.BeginAnimation(UIElement.OpacityProperty, opacityAnim);

            _socialExpanded = !_socialExpanded;
        }

        // ---------- Исправленный обработчик ----------
        private void SocialLink_Click(object sender, RoutedEventArgs e)
        {

            // приведение к Button — у него точно есть CommandParameter
            if (sender is Wpf.Ui.Controls.Button btn)
            {
                string? url = null;

                // сначала берем CommandParameter (если указан), иначе пробуем Tag
                if (btn.CommandParameter is string cp && !string.IsNullOrWhiteSpace(cp))
                    url = cp;
                else if (btn.Tag is string tag && !string.IsNullOrWhiteSpace(tag))
                    url = tag;

                if (!string.IsNullOrWhiteSpace(url))
                {
                    try
                    {
                        Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
                    }
                    catch
                    {
                        // тихо игнорируем ошибки открытия
                    }
                }
            }
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
            // В коде C# (например, в конструкторе окна или в обработчике события Loaded)
            string userName = Environment.UserName; // Получаем имя пользователя
            string greeting = "";
            int hour = DateTime.Now.Hour; // Получаем текущий час (от 0 до 23)

            if (hour >= 5 && hour < 12)
            {
                greeting = "Доброе утро";
            }
            else if (hour >= 12 && hour < 17)
            {
                greeting = "Добрый день";
            }
            else if (hour >= 17 && hour < 23)
            {
                greeting = "Добрый вечер";
            }
            else // 23:00 - 04:59
            {
                greeting = "Доброй ночи";
            }

            // Формируем итоговое сообщение
            string fullMessage = $"{greeting}, {userName}!";

            Picture.Source = GetUserAvatar();

            Message.Text = fullMessage;
            User.Text = userName;
        }

        public static BitmapImage? GetUserAvatar()
        {
            // Папка, где Windows хранит аватарки
            string dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Microsoft", "Windows", "AccountPicture");

            if (!Directory.Exists(dir))
                return null;

            // Ищем самую крупную аватарку
            var files = Directory.GetFiles(dir, "user*.png")
                                 .Concat(Directory.GetFiles(dir, "user*.jpg"))
                                 .Concat(Directory.GetFiles(dir, "user*.bmp"))
                                 .OrderByDescending(f => new FileInfo(f).Length)
                                 .ToList();

            if (!files.Any())
                return null;

            string path = files.First();

            try
            {
                using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read))
                {
                    var bmp = new BitmapImage();
                    bmp.BeginInit();
                    bmp.CacheOption = BitmapCacheOption.OnLoad;
                    bmp.StreamSource = stream;
                    bmp.EndInit();
                    bmp.Freeze();
                    return bmp;
                }
            }
            catch
            {
                return null;
            }
        }
        private async Task ConnectionCheck()
        {
            while (true) {
            bool success = true;
            await Task.Delay(500);
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
                    await Task.Delay(500);
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