using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Wpf.Ui.Controls;
using System;
using System.IO; // Добавлено для работы с файлами
using System.Windows;
using Wpf.Ui.Controls;
using System.Text;


namespace Helinstaller.Views.Windows
{
    public partial class OfficeConfigWindow : FluentWindow
    {
        public OfficeConfiguration Configuration { get; set; }

        public OfficeConfigWindow()
        {
            InitializeComponent();
            Configuration = new OfficeConfiguration();
            this.DataContext = this;
        }

        // В файле OfficeConfigWindow.xaml.cs

        private void InstallButton_Click(object sender, RoutedEventArgs e)
        {
            // Мы просто говорим "ОК", файл создаст основной метод InstallOffice
            this.DialogResult = true;
            this.Close();
        }

        private void CloseButton_click(object sender, RoutedEventArgs e)
        {
            this.DialogResult = false;
        }
    }
}