using Wpf.Ui.Abstractions.Controls;
using Wpf.Ui.Appearance;

namespace Helinstaller.ViewModels.Pages
{
    public partial class DashboardViewModel : ObservableObject, INavigationAware
    {
        private bool _isInitialized = false;

        [ObservableProperty]
        private ApplicationTheme _currentTheme = ApplicationTheme.Unknown;

        // Эти свойства будут заполняться и передаваться на AppPage
        [ObservableProperty]
        private string _appTitle = string.Empty;

        [ObservableProperty]
        private string _appDescription = string.Empty;

        [ObservableProperty]
        private string _appIconPath = string.Empty;

        [ObservableProperty]
        private string _appPreviewPath = string.Empty;

        [ObservableProperty]
        private bool _isInstalling = false;

        [ObservableProperty]
        private double _progressValue = 0;

        // Определяем команду с атрибутом [RelayCommand] для навигации
        [RelayCommand]
        public void OnNavigateToApp(string appName)
        {
            switch (appName)
            {

                case "WinRAR":
                    AppTitle = "WinRAR";
                    AppDescription = "Файловый архиватор, который позволяет сжимать, архивировать и распаковывать файлы.";
                    AppIconPath = "/Assets/winrar.png";
                    AppPreviewPath = "/Assets/winrarprev.png";
                    break;
                case "Discord":
                    AppTitle = "Discord";
                    AppDescription = "А кто не знает? Захотелось в Доту с друзяками - заходишь сюда.";
                    AppIconPath = "/Assets/discord.png";
                    AppPreviewPath = "/Assets/discordprev.png";
                    break;
                case "Steam":
                    AppTitle = "Steam";
                    AppDescription = "Тут игрушки качаешь, увы конечно платно. Если хочется бесплатно, то жмёшь на установку зелёной буковки µ";
                    AppIconPath = "/Assets/steam.png";
                    AppPreviewPath = "/Assets/steamprev.png";
                    break;
                case "µTorrent":
                    AppTitle = "µTorrent";
                    AppDescription = "Как раз тот случай когда хочется юзать стим, а денюжки нет.";
                    AppIconPath = "/Assets/µtorrent.png";
                    AppPreviewPath = "/Assets/utorrentprev.png";
                    break;
                case "Zapret":
                    AppTitle = "Zapret";
                    AppDescription = "Если не работает Дискордик или ютуб - запускай.";
                    AppIconPath = "";
                    AppPreviewPath = "/Assets/zapretprev.png";
                    break;
                case "EP":
                    AppTitle = "ExplorerPatcher";
                    AppDescription = "Набор приколов для проводника. Если неприятна панель задач из 11 Винды - настраивай через эту штуку.";
                    AppIconPath = "";
                    AppPreviewPath = "/Assets/epprev.png";
                    break;
                case "PT":
                    AppTitle = "PowerToys";
                    AppDescription = "Много различных инструментов прям в Винде. К примеру пипетка и линейка экрана по пикселям.";
                    AppIconPath = "/Assets/powertoys.png";
                    AppPreviewPath = "/Assets/powertoysprev.png";
                    break;
                case "Meridius":
                    AppTitle = "Meridius";
                    AppDescription = "ПК-Клиент ВК Музыки. Эквалайзер, плавное затухание - всё есть, всё здесь.";
                    AppIconPath = "/Assets/meridius.png";
                    AppPreviewPath = "/Assets/meridiusprev.png";
                    break;
                case "TB":
                    AppTitle = "TranslucentTB";
                    AppDescription = "Прозрачная панель задач. ";
                    AppIconPath = "/Assets/translucenttb.png";
                    AppPreviewPath = "/Assets/translucenttbprev.png";
                    break;
                case "Office":
                    AppTitle = "Office";
                    AppDescription = "Набор офисных приложений: Word, Excel, Powerpoint, Access, Visio - Всё тут. \nДля незнающих: Access позволяет создавать и редактировать БД, Visio же полезен для создания векторных схем.";
                    AppIconPath = "/Assets/office.png";
                    AppPreviewPath = "/Assets/officeprev.png";
                    break;
                case "Nvidia":
                    AppTitle = "Nvidia App";
                    AppDescription = "Драйвера, запись экрана, монитор производительности для видеоадаптеров NVIDIA";
                    AppIconPath = "/Assets/nvidia.png";
                    AppPreviewPath = "/Assets/nvidiaprev.png";
                    break;
                case "AMD":
                    AppTitle = "AMD Adrenalin";
                    AppDescription = "Драйвера, запись экрана, монитор производительности для видеоадаптеров AMD";
                    AppIconPath = "/Assets/amd.png";
                    AppPreviewPath = "/Assets/amdprev.png";
                    break;

            }
        }

        public Task OnNavigatedToAsync()
        {
            if (!_isInitialized)
                InitializeViewModel();
            return Task.CompletedTask;
        }

        public Task OnNavigatedFromAsync() => Task.CompletedTask;

        private void InitializeViewModel()
        {
            _isInitialized = true;
        }
    }
}