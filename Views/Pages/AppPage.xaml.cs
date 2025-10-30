using Helinstaller.ViewModels.Pages;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Transactions;
using System.Windows;
using System.Windows.Controls;
using Wpf.Ui.Abstractions.Controls;
using Wpf.Ui.Controls; // Для доступа к ProgressRing

namespace Helinstaller.Views.Pages
{
    public partial class AppPage : INavigableView<AppPageViewmodel>
    {
        public AppPageViewmodel ViewModel { get; }

        public AppPage(AppPageViewmodel viewModel)
        {
            ViewModel = viewModel;
            DataContext = ViewModel;
            InitializeComponent();
        }

        private void Page_Loaded(object sender, RoutedEventArgs e)
        {
            ViewModel.CheckCommand.Execute(null);
        }

        private void InstallButton_Click(object sender, RoutedEventArgs e)
        {
            ForceInstallSwitch.IsChecked = false;
        }
    }
}