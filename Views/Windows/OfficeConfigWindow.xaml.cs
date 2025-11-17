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
using System.Windows.Shapes;
using Wpf.Ui.Controls;

namespace Helinstaller.Views.Windows
{
    public partial class OfficeConfigWindow : FluentWindow
    {

        public OfficeConfiguration Configuration { get; set; }

        public OfficeConfigWindow()
        {
            InitializeComponent();
            Configuration = new OfficeConfiguration();
            this.DataContext = this; // Устанавливаем DataContext для привязки
        }

        private void InstallButton_Click(object sender, RoutedEventArgs e)
        {
            this.DialogResult = true; // Возвращаем true, что означает "Начать установку"
            this.Close();
        }
    }
}
