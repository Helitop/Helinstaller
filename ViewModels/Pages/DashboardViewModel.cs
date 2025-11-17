using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Wpf.Ui.Abstractions.Controls;
using Wpf.Ui.Appearance;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using System.Linq;

namespace Helinstaller.ViewModels.Pages
{
    // Класс для структуры данных приложения, десериализованных из JSON.
    // Заголовки свойств соответствуют ключам в JSON.
    public record AppInfo(
        string Name,
        string Title,
        string Description,
        string IconPath,
        string PreviewPath,
        string DownloadUrl
    );

    public partial class DashboardViewModel : ObservableObject, INavigationAware
    {
        private bool _isInitialized = false;

        // Список для хранения данных, загруженных из JSON.
        private List<AppInfo> _applications = new List<AppInfo>();

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
        private bool _isInstalled = false;

        [ObservableProperty]
        private bool _isChecking = false;

        [ObservableProperty]
        private double _progressValue = 0;

        [ObservableProperty]
        private string _downloadUrl = string.Empty;

        // Определяем команду с атрибутом [RelayCommand] для навигации
        [RelayCommand]
        public void OnNavigateToApp(string appName)
        {
            // Ищем приложение в загруженном списке по его Name
            var selectedApp = _applications.FirstOrDefault(a => a.Name == appName);

            if (selectedApp != null)
            {
                // Заполняем ObservableProperties данными из найденного объекта AppInfo
                AppTitle = selectedApp.Title;
                AppDescription = selectedApp.Description;
                AppIconPath = selectedApp.IconPath;
                AppPreviewPath = selectedApp.PreviewPath;

                // Сброс статусов для нового приложения (по необходимости)
                IsInstalling = false;
                IsInstalled = false;
                ProgressValue = 0;

                DownloadUrl = selectedApp.DownloadUrl;

                // Тут можно добавить логику навигации, если у вас есть INavigationService
                // Пример: _navigationService.NavigateTo(typeof(AppPage), selectedApp);
            }
            else
            {
                // Обработка случая, когда приложение не найдено
                System.Diagnostics.Debug.WriteLine($"Error: Application '{appName}' not found in loaded data.");
            }
        }

        public async Task OnNavigatedToAsync()
        {
            if (!_isInitialized)
                await InitializeViewModel();
        }

        public Task OnNavigatedFromAsync() => Task.CompletedTask;

        private async Task InitializeViewModel()
        {
            if (_isInitialized) return;

            // Загрузка данных приложений
            await LoadApplicationData("apps.json");

            _isInitialized = true;
        }

        // Метод для загрузки данных из JSON-файла
        private async Task LoadApplicationData(string filePath)
        {
            try
            {
                // В реальном WPF-приложении вам может потребоваться получить 
                // путь к файлу ресурса или убедиться, что файл скопирован в выходной каталог.
                // Для простоты, предполагаем, что "apps.json" находится рядом с исполняемым файлом.
                string jsonString = await File.ReadAllTextAsync(filePath);

                var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                var loadedApps = JsonSerializer.Deserialize<List<AppInfo>>(jsonString, options);

                if (loadedApps != null)
                {
                    _applications = loadedApps;
                    System.Diagnostics.Debug.WriteLine($"Successfully loaded {_applications.Count} applications from {filePath}.");
                }
            }
            catch (FileNotFoundException)
            {
                System.Diagnostics.Debug.WriteLine($"Error: Application data file not found at {filePath}.");
            }
            catch (JsonException ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error deserializing JSON: {ex.Message}");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"An unexpected error occurred during data loading: {ex.Message}");
            }
        }
    }
}