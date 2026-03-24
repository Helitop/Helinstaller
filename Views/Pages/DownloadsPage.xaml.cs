using Helinstaller.ViewModels.Pages;
using Wpf.Ui.Abstractions.Controls;

namespace Helinstaller.Views.Pages
{
    public partial class DownloadsPage : INavigableView<DownloadsViewModel>
    {
        public DownloadsViewModel ViewModel { get; }
        public DownloadsPage(DownloadsViewModel viewModel)
        {
            ViewModel = viewModel;
            DataContext = ViewModel;
            InitializeComponent();
        }
    }
}