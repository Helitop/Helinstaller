using Helinstaller.ViewModels.Pages;
using Wpf.Ui.Abstractions.Controls;

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
    }
}