using Helinstaller.ViewModels.Pages;
using Helinstaller.Views.Windows;
using System.Windows.Controls;
using Wpf.Ui.Abstractions.Controls;

namespace Helinstaller.Views.Pages
{
    public partial class SystemDashboardPage : INavigableView<SystemDashboardViewModel>
    {
        public SystemDashboardViewModel ViewModel { get; }

        public SystemDashboardPage(SystemDashboardViewModel viewModel)
        {
            ViewModel = viewModel;
            DataContext = this;
            InitializeComponent();
        }
        private void ShowHelpButton_Click(object sender, RoutedEventArgs e)
        {
            HelpDialog.ShowHelp(Window.GetWindow(this), "systemdashboard");
        }
    }
}