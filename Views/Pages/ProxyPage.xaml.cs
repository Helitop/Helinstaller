using Helinstaller.ViewModels.Pages;
using Wpf.Ui.Abstractions.Controls;

namespace Helinstaller.Views.Pages
{
    public partial class ProxyPage : INavigableView<ProxyViewModel>
    {
        public ProxyViewModel ViewModel { get; }

        public ProxyPage(ProxyViewModel viewModel)
        {
            ViewModel = viewModel;
            DataContext = this;
            InitializeComponent();
        }
    }
}