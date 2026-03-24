using Helinstaller.ViewModels.Pages; // Может быть не нужен, но оставлен для совместимости с другими файлами
using Microsoft.Win32;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using Wpf.Ui;
using Wpf.Ui.Controls;

namespace Helinstaller.Views.Pages
{
    /// <summary>
    /// Логика взаимодействия для EditorPage.xaml
    /// </summary>
    public partial class Editor : Page
    {
        private AppInfo _currentAppInfo;
        private readonly INavigationService _navigationService;
        private const string JsonPath = "apps.json";

        // *** НОВАЯ КОНСТАНТА ДЛЯ ПАПКИ РЕСУРСОВ ***
        private const string AssetsFolder = "Assets/";

        /// <summary>
        /// Конструктор для добавления нового приложения.
        /// </summary>
        public Editor(INavigationService navigationService)
        {
            InitializeComponent();
            _navigationService = navigationService;
            // Установка заголовка для режима "Добавить"
            PageTitle.Text = "Добавить новое приложение";
            AppNameTextBox.IsReadOnly = false;
        }

        /// <summary>
        /// Конструктор для редактирования существующего приложения.
        /// </summary>
        public Editor(AppInfo app, INavigationService navigationService)
        {
            InitializeComponent();
            _navigationService = navigationService;
            _currentAppInfo = app;

            // Установка заголовка для режима "Редактировать"
            PageTitle.Text = $"Редактировать: {app.Title}";
            AppNameTextBox.IsReadOnly = true; // Запрещаем редактировать уникальный Name

            // Загрузка данных в поля
            LoadApp(app);
        }
        private async void MagicFill_Click(object sender, RoutedEventArgs e)
        {
            string url = AppDownloadUrlTextBox.Text.Trim();
            if (string.IsNullOrEmpty(url)) return;

            // Показываем визуально, что мы работаем (можно через кнопку)
            var data = await Helpers.MetadataService.GetMetadataAsync(url);

            if (string.IsNullOrEmpty(AppTitleTextBox.Text)) AppTitleTextBox.Text = data.Title;
            if (string.IsNullOrEmpty(AppDescriptionTextBox.Text)) AppDescriptionTextBox.Text = data.Description;
            if (string.IsNullOrEmpty(AppIconPathTextBox.Text)) AppIconPathTextBox.Text = data.IconUrl;

            // Сразу обновляем превью, если оно завязано на текстбокс
            CustomMessageBox.Show("Данные подтянуты из сети!", "Магия");
        }
        private void LoadApp(AppInfo app)
        {
            AppNameTextBox.Text = app.Name;
            AppTitleTextBox.Text = app.Title;
            AppDescriptionTextBox.Text = app.Description;
            AppIconPathTextBox.Text = app.IconPath;
            AppPreviewPathTextBox.Text = app.PreviewPath;

            // Проверяем, есть ли уже префикс github:
            if (app.DownloadUrl != null && app.DownloadUrl.StartsWith("github:"))
            {
                GithubSourceToggle.IsChecked = true;
                AppDownloadUrlTextBox.Text = app.DownloadUrl.Replace("github:", "");
            }
            else
            {
                GithubSourceToggle.IsChecked = false;
                AppDownloadUrlTextBox.Text = app.DownloadUrl;
            }

            SaveButton.Content = "Сохранить изменения";
        }

        /// <summary>
        /// Открывает диалог выбора файла, копирует файл в AssetsFolder и возвращает относительный путь.
        /// </summary>
        private string HandleFileBrowseAndCopy()
        {
            OpenFileDialog openFileDialog = new OpenFileDialog
            {
                // Фильтр для распространенных форматов изображений
                Filter = "Файлы изображений|*.jpg;*.jpeg;*.png;*.ico;*.bmp|Все файлы (*.*)|*.*",
                Title = "Выберите файл изображения",
                InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.MyPictures) // Начнем с папки изображений
            };

            if (openFileDialog.ShowDialog() == true)
            {
                string selectedPath = openFileDialog.FileName;
                string fileName = Path.GetFileName(selectedPath);

                try
                {
                    // 1. Создаем целевую папку, если ее нет
                    // Path.Combine корректно обрабатывает разделители
                    string targetDirectory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, AssetsFolder);
                    Directory.CreateDirectory(targetDirectory);

                    // 2. Определяем новое имя файла с GUID, чтобы избежать конфликтов и кэширования
                    string fileExtension = Path.GetExtension(fileName);
                    string newFileName = $"{Path.GetFileNameWithoutExtension(fileName)}_{Guid.NewGuid().ToString().Substring(0, 8)}{fileExtension}";
                    string targetFilePath = Path.Combine(targetDirectory, newFileName);

                    // 3. Копируем файл
                    File.Copy(selectedPath, targetFilePath, true);

                    // 4. Возвращаем путь относительно корневой папки приложения
                    // Используем замену разделителя для унификации путей в XAML
                    return Path.Combine(AssetsFolder, newFileName).Replace('\\', '/');
                }
                catch (Exception ex)
                {
                    CustomMessageBox.Show($"Ошибка при копировании файла: {ex.Message}", "Ошибка Файловой Системы");
                    return null;
                }
            }
            return null;
        }

        // ОБНОВЛЕННЫЙ ОБРАБОТЧИК КЛИКА ДЛЯ ICON PATH
        private void BrowseIconPath_Click(object sender, RoutedEventArgs e)
        {
            string path = HandleFileBrowseAndCopy();
            if (path != null)
            {
                AppIconPathTextBox.Text = path;
            }
        }

        // ОБНОВЛЕННЫЙ ОБРАБОТЧИК КЛИКА ДЛЯ PREVIEW PATH
        private void BrowsePreviewPath_Click(object sender, RoutedEventArgs e)
        {
            string path = HandleFileBrowseAndCopy();
            if (path != null)
            {
                AppPreviewPathTextBox.Text = path;
            }
        }

        private async void SaveButton_Click(object sender, RoutedEventArgs e)
        {
            string rawUrl = AppDownloadUrlTextBox.Text.Trim();
            string finalUrl = (GithubSourceToggle.IsChecked == true && !rawUrl.StartsWith("github:"))
                ? $"github:{rawUrl}"
                : rawUrl;

            string appName = AppNameTextBox.Text.Trim();
            string iconPath = AppIconPathTextBox.Text.Trim();

            // --- ЛОГИКА СКАЧИВАНИЯ ИКОНКИ ---
            if (iconPath.StartsWith("http", StringComparison.OrdinalIgnoreCase))
            {
                SaveButton.IsEnabled = false;
                SaveButton.Content = "Скачивание иконки...";

                // Пробуем скачать и сохранить локально (с прозрачностью)
                var localPath = await Helpers.MetadataService.DownloadIconAsync(iconPath, appName);
                if (localPath != null) iconPath = localPath;

                SaveButton.IsEnabled = true;
                SaveButton.Content = "Сохранить изменения";
            }

            var newApp = new AppInfo
            {
                Name = appName,
                Title = AppTitleTextBox.Text.Trim(),
                Description = AppDescriptionTextBox.Text.Trim(),
                IconPath = iconPath,
                PreviewPath = AppPreviewPathTextBox.Text.Trim(),
                DownloadUrl = finalUrl
            };

            if (string.IsNullOrWhiteSpace(newApp.Name) || string.IsNullOrWhiteSpace(newApp.Title) || string.IsNullOrWhiteSpace(newApp.DownloadUrl))
            {
                CustomMessageBox.Show("Заполните обязательные поля.", "Ошибка");
                return;
            }

            try
            {
                List<AppInfo> appList = new();
                if (File.Exists(JsonPath))
                {
                    string json = File.ReadAllText(JsonPath);
                    appList = JsonSerializer.Deserialize<List<AppInfo>>(json) ?? new();
                }

                if (_currentAppInfo == null)
                {
                    if (appList.Any(a => a.Name.Equals(newApp.Name, StringComparison.OrdinalIgnoreCase)))
                    {
                        CustomMessageBox.Show("Имя уже занято.", "Ошибка");
                        return;
                    }
                    appList.Add(newApp);
                }
                else
                {
                    var index = appList.FindIndex(a => a.Name.Equals(_currentAppInfo.Name, StringComparison.OrdinalIgnoreCase));
                    if (index != -1) appList[index] = newApp;
                }

                var options = new JsonSerializerOptions { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping };
                File.WriteAllText(JsonPath, JsonSerializer.Serialize(appList, options));

                _navigationService.Navigate(typeof(DashboardPage));
            }
            catch (Exception ex)
            {
                CustomMessageBox.Show($"Ошибка: {ex.Message}", "Ошибка");
            }
        }

        private void BackButton_Click(object sender, RoutedEventArgs e)
        {
            // Просто возвращаемся на предыдущую страницу (Dashboard)
            _navigationService.Navigate(typeof(DashboardPage));
        }

        private void AppPreviewPathTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            // Логика автоматического обновления превью
        }
    }
}