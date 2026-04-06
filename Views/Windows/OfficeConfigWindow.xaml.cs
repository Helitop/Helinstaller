using System.Windows;
using Wpf.Ui.Controls;
using Helinstaller.Models; // Убедитесь, что этот using есть, чтобы видеть класс модели

namespace Helinstaller.Views.Windows
{
    public partial class OfficeConfigWindow : FluentWindow
    {
        // ЭТО ТО САМОЕ СВОЙСТВО, КОТОРОГО НЕ ХВАТАЕТ
        public OfficeConfiguration Configuration { get; set; }

        public OfficeConfigWindow()
        {
            InitializeComponent();

            // Инициализируем модель настроек
            Configuration = new OfficeConfiguration();

            // Устанавливаем контекст данных, чтобы привязки (Binding) в XAML заработали
            this.DataContext = this;
        }

        private void InstallButton_Click(object sender, RoutedEventArgs e)
        {
            // Закрываем окно с результатом "True", чтобы начать установку
            this.DialogResult = true;
            this.Close();
        }

        private void CloseButton_click(object sender, RoutedEventArgs e)
        {
            // Закрываем окно с результатом "False"
            this.DialogResult = false;
            this.Close();
        }
    }
}